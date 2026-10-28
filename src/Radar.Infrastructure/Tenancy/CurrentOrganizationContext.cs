using Radar.Application.Abstractions;
using Radar.Domain.Organizations;

namespace Radar.Infrastructure.Tenancy
{
    /// <summary>
    /// One per request (scoped). Filled in once the caller's membership has been checked;
    /// read by AppDbContext's tenant query filter.
    /// </summary>
    public sealed class CurrentOrganizationContext : ICurrentOrganization
    {
        public Guid? OrganizationId { get; private set; }

        public MembershipRole? Role { get; private set; }

        public void Set(Guid organizationId, MembershipRole role)
        {
            if (OrganizationId is not null && OrganizationId != organizationId)
            {
                throw new InvalidOperationException("The current organization can't change during a request.");
            }

            OrganizationId = organizationId;
            Role = role;
        }
    }
}