using Radar.Domain.Organizations;

namespace Radar.Application.Organizations
{
    public sealed record OrganizationAccess(Guid OrganizationId, MembershipRole Role);

    public sealed record OrganizationDetails(Guid Id, string Name, string Plan, string Role);

    public sealed record AuditEntryView(DateTimeOffset OccurredAt, string Action, string? Target, Guid? ActorUserId, string? IpAddress);

    public enum RenameStatus
    {
        Renamed,
        Forbidden,
        Invalid
    }

    public sealed record RenameResult(RenameStatus Status, OrganizationDetails? Organization, string? Error);

    public interface IOrganizationService
    {
        /// <summary>
        /// Which organization a user may act on: the requested one if they are a member of it,
        /// otherwise (no request) their first organization. Null means "not a member".
        /// </summary>
        Task<OrganizationAccess?> ResolveAccessAsync(Guid userId, Guid? requestedOrganizationId, CancellationToken cancellationToken = default);

        Task<OrganizationDetails?> GetCurrentAsync(CancellationToken cancellationToken = default);

        /// <summary>Owners and Admins only. Records an audit entry.</summary>
        Task<RenameResult> RenameCurrentAsync(string? name, Guid actorUserId, string? ipAddress, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<AuditEntryView>> GetAuditLogAsync(CancellationToken cancellationToken = default);
    }
}