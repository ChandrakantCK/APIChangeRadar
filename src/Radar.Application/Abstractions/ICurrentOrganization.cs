using Radar.Domain.Organizations;

namespace Radar.Application.Abstractions
{
    /// <summary>
    /// The organization (tenant) the current request acts on, and the caller's role in it.
    /// Null outside an organization-scoped request - and then tenant data is simply invisible.
    /// </summary>
    public interface ICurrentOrganization
    {
        Guid? OrganizationId { get; }

        MembershipRole? Role { get; }
    }
}