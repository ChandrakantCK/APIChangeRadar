using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radar.Application.Crawling;
using Radar.Domain.Crawling;
using Radar.Infrastructure.Persistence;

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

            var startedAt = time.GetUtcNow();
            var started = time.GetTimestamp();
            var result = await fetcher.FetchAsync(source.SpecUrl, previous?.ETag, previous?.LastModified, cancellationToken);
            var duration = time.GetElapsedTime(started);

            var run = result.Kind switch
            {
                FetchKind.Downloaded => CrawlRun.Downloaded(
                    sourceId, startedAt, duration,
                    changed: result.ContentHash != previous?.ContentHash,
                    result.HttpStatus!.Value, result.ETag, result.LastModified, result.ContentLength, result.ContentHash!),
                FetchKind.NotModified when previous is not null => CrawlRun.NotModified(sourceId, startedAt, duration, previous),
                FetchKind.NotModified => CrawlRun.Failed(sourceId, startedAt, duration, 304, "304 Not Modified, but we have never downloaded it."),
                _ => CrawlRun.Failed(sourceId, startedAt, duration, result.HttpStatus, result.Error ?? "Unknown error."),
            };

            db.CrawlRuns.Add(run);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Crawled {Source}: {Outcome} (HTTP {Status}, {Bytes} bytes, {DurationMs} ms){Error}",
                source.Name, run.Outcome, run.HttpStatus, run.ContentLength, run.DurationMs,
                run.Error is null ? string.Empty : $" - {run.Error}");

            return run.Outcome;
        }
    }
}
