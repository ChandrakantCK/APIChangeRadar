using Microsoft.EntityFrameworkCore;
using Radar.Application.Abstractions;
using Radar.Application.Changes;
using Radar.Domain.Changes;
using Radar.Infrastructure.Persistence;

namespace Radar.Infrastructure.Changes
{
    internal sealed class ChangeFeedService(AppDbContext db, ICurrentOrganization current) : IChangeFeedService
    {
        public async Task<ChangeFeed> ListAsync(
            FeedSeverity severity, DateTimeOffset? before, int pageSize, CancellationToken cancellationToken = default)
        {
            RequireOrganization();
            pageSize = Math.Clamp(pageSize, 1, IChangeFeedService.MaxPageSize);

            var sets = Watched();
            sets = severity switch
            {
                FeedSeverity.Breaking => sets.Where(c => c.BreakingCount > 0),
                FeedSeverity.Warning => sets.Where(c => c.BreakingCount > 0 || c.WarningCount > 0),
                _ => sets,
            };

            // Keyset paging: "older than the last one you saw" stays correct while new releases arrive,
            // unlike page numbers, which shift.
            if (before is { } cursor)
            {
                sets = sets.Where(c => c.DetectedAt < cursor);
            }

            // One extra row tells us whether there is a next page.
            var rows = await Views(sets.OrderByDescending(c => c.DetectedAt).ThenByDescending(c => c.Id).Take(pageSize + 1))
                .ToListAsync(cancellationToken);

            // Re-sorted in memory: the joins in Views don't promise to keep the subquery's order.
            var items = rows.OrderByDescending(v => v.DetectedAt).ThenByDescending(v => v.Id).Take(pageSize).ToList();
            return new ChangeFeed(items, rows.Count > pageSize ? items[^1].DetectedAt : null);
        }

        public async Task<ChangeSetDetail?> GetAsync(Guid changeSetId, CancellationToken cancellationToken = default)
        {
            RequireOrganization();

            // A release of an API you don't watch is a 404, like another organization's watch.
            var summary = await Views(Watched().Where(c => c.Id == changeSetId)).SingleOrDefaultAsync(cancellationToken);
            if (summary is null)
            {
                return null;
            }

            var changes = await db.ApiChanges
                .Where(c => c.ChangeSetId == changeSetId)
                .OrderBy(c => c.Position)
                .ToListAsync(cancellationToken);

            // Most severe first, then the differ's order (path, method). Sorted in memory: the enum is stored as text.
            var views = changes
                .OrderBy(c => c.Severity)
                .ThenBy(c => c.Position)
                .Select(c => new ChangeView(c.RuleId, c.Severity.ToString(), c.Method, c.Path, c.Location, c.Message))
                .ToList();

            return new ChangeSetDetail(summary, views);
        }

        // Change sets are shared by everyone; the tenant filter on Watches limits them to the current organization's APIs.
        private IQueryable<ChangeSet> Watched() =>
            db.ChangeSets.Where(c => db.Watches.Any(w => w.SpecSourceId == c.SpecSourceId));

        private IQueryable<ChangeSetView> Views(IQueryable<ChangeSet> sets) =>
            from c in sets
            join s in db.SpecSources on c.SpecSourceId equals s.Id
            join p in db.ApiProviders on s.ProviderId equals p.Id
            select new ChangeSetView(
                c.Id, s.Id, p.Name, s.Name, c.DetectedAt, c.FromApiVersion, c.ToApiVersion,
                c.BreakingCount, c.WarningCount, c.InfoCount);

        private Guid RequireOrganization() =>
            current.OrganizationId
            ?? throw new InvalidOperationException("The change feed is only available inside an organization-scoped request.");
    }
}
