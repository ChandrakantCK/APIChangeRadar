using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radar.Application.Crawling;
using Radar.Diffing;
using Radar.Diffing.Changes;
using Radar.Diffing.Normalization;
using Radar.Domain.Changes;
using Radar.Domain.Crawling;
using Radar.Domain.Snapshots;
using Radar.Infrastructure.Persistence;
using Radar.Infrastructure.Snapshots;

namespace Radar.Infrastructure.Crawling
{
    internal sealed class Crawler(
        AppDbContext db,
        SpecFetcher fetcher,
        TimeProvider time,
        IOptions<CrawlerOptions> options,
        ILogger<Crawler> logger) : ICrawler
    {
        public async Task<CrawlSummary> CrawlDueSourcesAsync(CancellationToken cancellationToken = default)
        {
            var triedSince = time.GetUtcNow() - TimeSpan.FromMinutes(options.Value.SourceIntervalMinutes);

            // A background job has no current organization, so the tenant filter would hide every watch.
            // IgnoreQueryFilters is deliberate here: we only ask "does anyone watch this source?",
            // and never read or return any organization's data.
            var due = await db.SpecSources
                .Where(s => s.IsActive
                    && db.Watches.IgnoreQueryFilters().Any(w => w.SpecSourceId == s.Id)
                    && !db.CrawlRuns.Any(r => r.SpecSourceId == s.Id && r.StartedAt > triedSince))
                .OrderBy(s => s.Name)
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);

            var outcomes = new List<CrawlOutcome>(due.Count);
            foreach (var sourceId in due)
            {
                outcomes.Add(await CrawlSourceAsync(sourceId, cancellationToken));
            }

            return new CrawlSummary(
                due.Count,
                outcomes.Count(o => o == CrawlOutcome.Changed),
                outcomes.Count(o => o == CrawlOutcome.Unchanged),
                outcomes.Count(o => o == CrawlOutcome.NotModified),
                outcomes.Count(o => o == CrawlOutcome.Failed));
        }

        public async Task<CrawlOutcome> CrawlSourceAsync(Guid sourceId, CancellationToken cancellationToken = default)
        {
            var source = await db.SpecSources
                .Where(s => s.Id == sourceId)
                .Select(s => new { s.Name, s.SpecUrl })
                .SingleAsync(cancellationToken);

            // The last good download: its ETag makes the request conditional, its hash tells us if content changed.
            var previous = await db.CrawlRuns
                .Where(r => r.SpecSourceId == sourceId && r.Outcome != CrawlOutcome.Failed)
                .OrderByDescending(r => r.StartedAt)
                .FirstOrDefaultAsync(cancellationToken);

            // Only the hash: never load the stored content just to compare.
            var latestSnapshot = await db.SpecSnapshots
                .Where(s => s.SpecSourceId == sourceId)
                .OrderByDescending(s => s.CapturedAt)
                .Select(s => new { s.Id, s.Hash, s.ApiVersion })
                .FirstOrDefaultAsync(cancellationToken);
            var latestSnapshotHash = latestSnapshot?.Hash;

            // Without a snapshot we need the body, so don't let the server answer "304, you already have it".
            var askIfChanged = latestSnapshotHash is not null ? previous : null;

            var startedAt = time.GetUtcNow();
            var started = time.GetTimestamp();
            var result = await fetcher.FetchAsync(source.SpecUrl, askIfChanged?.ETag, askIfChanged?.LastModified, cancellationToken);
            var duration = time.GetElapsedTime(started);

            NormalizedSpec? normalized = null;
            CrawlRun run;
            switch (result.Kind)
            {
                case FetchKind.Downloaded:
                    var rawChanged = result.ContentHash != previous?.ContentHash;
                    try
                    {
                        if (rawChanged || latestSnapshotHash is null)
                        {
                            normalized = SpecNormalizer.Normalize(result.Content!);
                        }

                        run = CrawlRun.Downloaded(
                            sourceId, startedAt, duration, rawChanged,
                            result.HttpStatus!.Value, result.ETag, result.LastModified, result.ContentLength, result.ContentHash!);
                    }
                    catch (SpecNormalizationException ex)
                    {
                        run = CrawlRun.Failed(sourceId, startedAt, duration, result.HttpStatus, $"Not a usable OpenAPI document: {ex.Message}");
                    }

                    break;
                case FetchKind.NotModified when previous is not null:
                    run = CrawlRun.NotModified(sourceId, startedAt, duration, previous);
                    break;
                case FetchKind.NotModified:
                    run = CrawlRun.Failed(sourceId, startedAt, duration, 304, "304 Not Modified, but we have never downloaded it.");
                    break;
                default:
                    run = CrawlRun.Failed(sourceId, startedAt, duration, result.HttpStatus, result.Error ?? "Unknown error.");
                    break;
            }

            db.CrawlRuns.Add(run);

            // Different canonical hash = the API description really changed (not just its formatting): keep it.
            var snapshotTaken = normalized is not null && normalized.Hash != latestSnapshotHash;
            ChangeSet? changeSet = null;
            if (snapshotTaken)
            {
                var snapshot = new SpecSnapshot(
                    sourceId, run.Id, startedAt, normalized!.Hash, normalized.SpecVersion, normalized.ApiVersion,
                    normalized.CanonicalJson.Length, SnapshotCompression.Compress(normalized.CanonicalJson));
                db.SpecSnapshots.Add(snapshot);

                // The first snapshot is the baseline: there is nothing to compare it with.
                if (latestSnapshot is not null)
                {
                    changeSet = await DiffAsync(
                        sourceId, latestSnapshot.Id, latestSnapshot.ApiVersion, snapshot, normalized.CanonicalJson, source.Name, cancellationToken);
                    if (changeSet is not null)
                    {
                        db.ChangeSets.Add(changeSet);
                    }
                }
            }

            // Run, snapshot and changes are saved together: a crash can't leave a snapshot whose changes were never recorded.
            await db.SaveChangesAsync(cancellationToken);

            if (snapshotTaken)
            {
                await DeleteOldSnapshotsAsync(sourceId, cancellationToken);
            }

            logger.LogInformation(
                "Crawled {Source}: {Outcome} (HTTP {Status}, {Bytes} bytes, {DurationMs} ms){Snapshot}{Changes}{Error}",
                source.Name, run.Outcome, run.HttpStatus, run.ContentLength, run.DurationMs,
                snapshotTaken ? $", new snapshot {normalized!.SpecVersion} v{normalized.ApiVersion}" : string.Empty,
                changeSet is null ? string.Empty
                    : $", {changeSet.BreakingCount} breaking / {changeSet.WarningCount} warning / {changeSet.InfoCount} info changes",
                run.Error is null ? string.Empty : $" - {run.Error}");

            return run.Outcome;
        }

        // Compares the previous snapshot with the new one. Null when nothing a client would notice changed
        // (descriptions, examples) or when the differ fails: the snapshot is still kept either way.
        private async Task<ChangeSet?> DiffAsync(
            Guid sourceId, Guid previousSnapshotId, string? previousApiVersion,
            SpecSnapshot snapshot, byte[] canonicalJson, string sourceName, CancellationToken cancellationToken)
        {
            var previousContent = await db.SpecSnapshots
                .Where(s => s.Id == previousSnapshotId)
                .Select(s => s.CompressedContent)
                .SingleAsync(cancellationToken);

            IReadOnlyList<SpecChange> changes;
            try
            {
                changes = SpecDiffer.Diff(SnapshotCompression.Decompress(previousContent), canonicalJson);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Could not compare the new snapshot of {Source} with the previous one", sourceName);
                return null;
            }

            if (changes.Count == 0)
            {
                return null;
            }

            return new ChangeSet(
                sourceId, previousSnapshotId, snapshot.Id, snapshot.CapturedAt, previousApiVersion, snapshot.ApiVersion,
                changes.Select(c => new ApiChange(c.RuleId, c.Severity, c.Method, c.Path, c.Location, c.Message)).ToList());
        }

        private async Task DeleteOldSnapshotsAsync(Guid sourceId, CancellationToken cancellationToken)
        {
            var keep = db.SpecSnapshots
                .Where(s => s.SpecSourceId == sourceId)
                .OrderByDescending(s => s.CapturedAt)
                .Take(options.Value.SnapshotsToKeep)
                .Select(s => s.Id);

            // One DELETE statement in the database; nothing is loaded into memory.
            await db.SpecSnapshots
                .Where(s => s.SpecSourceId == sourceId && !keep.Contains(s.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }
    }
}
