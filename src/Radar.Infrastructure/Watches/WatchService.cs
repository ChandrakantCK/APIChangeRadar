using Microsoft.EntityFrameworkCore;
using Npgsql;
using Radar.Application.Abstractions;
using Radar.Application.Watches;
using Radar.Domain.Auditing;
using Radar.Domain.Organizations;
using Radar.Domain.Watches;
using Radar.Infrastructure.Persistence;

namespace Radar.Infrastructure.Watches
{
    internal sealed class WatchService(AppDbContext db, ICurrentOrganization current) : IWatchService
    {
        public async Task<WatchList> ListAsync(CancellationToken cancellationToken = default)
        {
            var organizationId = RequireOrganization();
            var plan = await db.Organizations
                .Where(o => o.Id == organizationId)
                .Select(o => o.Plan)
                .SingleAsync(cancellationToken);

            // No organization filter written here: the global query filter on Watch adds it.
            var watches = await Views(db.Watches).ToListAsync(cancellationToken);

            // Sorted in memory: EF cannot translate ORDER BY on a record built by its constructor,
            // and a plan allows at most a few hundred watches.
            var items = watches
                .OrderBy(w => w.ProviderName)
                .ThenBy(w => w.SourceName)
                .ToList();

            return new WatchList(items.Count, PlanLimits.MaxWatches(plan), items);
        }

        public async Task<AddWatchResult> AddAsync(
            Guid sourceId, Guid actorUserId, string? ipAddress, CancellationToken cancellationToken = default)
        {
            var organizationId = RequireOrganization();

            var source = await db.SpecSources
                .Where(s => s.Id == sourceId && s.IsActive)
                .Join(db.ApiProviders, s => s.ProviderId, p => p.Id, (s, p) => new { s.Name, ProviderName = p.Name })
                .SingleOrDefaultAsync(cancellationToken);
            if (source is null)
            {
                return new AddWatchResult(AddWatchStatus.SourceNotFound, null, "There is no such API in the catalog.");
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            // Lock this organization's row until commit. Two requests adding at "2 of 3" at the same moment
            // would otherwise both count 2 and both insert; with the lock the second waits and then counts 3.
            var organization = await db.Organizations
                .FromSql($"SELECT * FROM organizations WHERE id = {organizationId} FOR UPDATE")
                .SingleAsync(cancellationToken);

            if (await db.Watches.AnyAsync(w => w.SpecSourceId == sourceId, cancellationToken))
            {
                return new AddWatchResult(AddWatchStatus.AlreadyWatching, null, "You already watch this API.");
            }

            var limit = PlanLimits.MaxWatches(organization.Plan);
            if (await db.Watches.CountAsync(cancellationToken) >= limit)
            {
                return new AddWatchResult(
                    AddWatchStatus.LimitReached, null,
                    $"The {organization.Plan} plan can watch up to {limit} APIs. Remove one or upgrade.");
            }

            var watch = new Watch(organizationId, sourceId, actorUserId);
            db.Watches.Add(watch);
            db.AuditEntries.Add(new AuditEntry(
                organizationId, actorUserId, "watch.added", $"{source.ProviderName} / {source.Name}", ipAddress));

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Safety net: the unique index caught a duplicate the check above did not see.
                return new AddWatchResult(AddWatchStatus.AlreadyWatching, null, "You already watch this API.");
            }

            await transaction.CommitAsync(cancellationToken);

            var view = await Views(db.Watches.Where(w => w.Id == watch.Id)).SingleAsync(cancellationToken);
            return new AddWatchResult(AddWatchStatus.Added, view, null);
        }

        public async Task<bool> RemoveAsync(
            Guid watchId, Guid actorUserId, string? ipAddress, CancellationToken cancellationToken = default)
        {
            var organizationId = RequireOrganization();

            // Another organization's watch id finds nothing here (query filter), so it is a 404, not a 403:
            // we don't even confirm that the id exists.
            var watch = await db.Watches.SingleOrDefaultAsync(w => w.Id == watchId, cancellationToken);
            if (watch is null)
            {
                return false;
            }

            var view = await Views(db.Watches.Where(w => w.Id == watchId)).SingleAsync(cancellationToken);

            db.Watches.Remove(watch);
            db.AuditEntries.Add(new AuditEntry(
                organizationId, actorUserId, "watch.removed", $"{view.ProviderName} / {view.SourceName}", ipAddress));
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        private IQueryable<WatchView> Views(IQueryable<Watch> watches) =>
            from w in watches
            join s in db.SpecSources on w.SpecSourceId equals s.Id
            join p in db.ApiProviders on s.ProviderId equals p.Id
            select new WatchView(w.Id, s.Id, p.Name, s.Name, s.SpecUrl, w.CreatedAt);

        private Guid RequireOrganization() =>
            current.OrganizationId
            ?? throw new InvalidOperationException("Watches are only available inside an organization-scoped request.");
    }
}
