using Microsoft.EntityFrameworkCore;
using Radar.Application.Abstractions;
using Radar.Application.Organizations;
using Radar.Domain.Auditing;
using Radar.Domain.Organizations;
using Radar.Infrastructure.Persistence;

namespace Radar.Infrastructure.Organizations
{
    internal sealed class OrganizationService(AppDbContext db, ICurrentOrganization current) : IOrganizationService
    {
        public async Task<OrganizationAccess?> ResolveAccessAsync(
            Guid userId, Guid? requestedOrganizationId, CancellationToken cancellationToken = default)
        {
            var memberships = db.Memberships.Where(m => m.UserId == userId);

            if (requestedOrganizationId is { } requested)
            {
                memberships = memberships.Where(m => m.OrganizationId == requested);
            }

            var membership = await memberships
                .OrderBy(m => m.CreatedAt)
                .Select(m => new { m.OrganizationId, m.Role })
                .FirstOrDefaultAsync(cancellationToken);

            return membership is null ? null : new OrganizationAccess(membership.OrganizationId, membership.Role);
        }

        public async Task<OrganizationDetails?> GetCurrentAsync(CancellationToken cancellationToken = default)
        {
            if (current.OrganizationId is not { } organizationId || current.Role is not { } role)
            {
                return null;
            }

            var organization = await db.Organizations.SingleOrDefaultAsync(o => o.Id == organizationId, cancellationToken);
            return organization is null ? null : ToDetails(organization, role);
        }

        public async Task<RenameResult> RenameCurrentAsync(
            string? name, Guid actorUserId, string? ipAddress, CancellationToken cancellationToken = default)
        {
            if (current.OrganizationId is not { } organizationId
                || current.Role is not (MembershipRole.Owner or MembershipRole.Admin))
            {
                return new RenameResult(RenameStatus.Forbidden, null, "Only owners and admins can rename the organization.");
            }

            var organization = await db.Organizations.SingleAsync(o => o.Id == organizationId, cancellationToken);
            try
            {
                organization.Rename(name ?? string.Empty);
            }
            catch (ArgumentException ex)
            {
                return new RenameResult(RenameStatus.Invalid, null, ex.Message);
            }

            db.AuditEntries.Add(new AuditEntry(organizationId, actorUserId, "organization.renamed", organization.Name, ipAddress));
            await db.SaveChangesAsync(cancellationToken);

            return new RenameResult(RenameStatus.Renamed, ToDetails(organization, current.Role.Value), null);
        }

        public async Task<IReadOnlyList<AuditEntryView>> GetAuditLogAsync(CancellationToken cancellationToken = default)
        {
            // Note: no "WHERE organization_id = ..." here on purpose.
            // AppDbContext's global query filter adds it to EVERY query on tenant-owned data.
            return await db.AuditEntries
                .OrderByDescending(e => e.OccurredAt)
                .Take(100)
                .Select(e => new AuditEntryView(e.OccurredAt, e.Action, e.Target, e.ActorUserId, e.IpAddress))
                .ToListAsync(cancellationToken);
        }

        private static OrganizationDetails ToDetails(Organization organization, MembershipRole role) =>
            new(organization.Id, organization.Name, organization.Plan.ToString(), role.ToString());
    }
}