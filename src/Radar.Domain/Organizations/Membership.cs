using System;
using System.Collections.Generic;
using System.Text;

namespace Radar.Domain.Organizations
{
    /// <summary>
    /// Links a user to an organization with a role. A user can belong to many organizations.
    /// </summary>
    public sealed class Membership
    {
        // Used by EF Core when loading rows from the database.
        private Membership()
        {
        }

        public Membership(Guid organizationId, Guid userId, MembershipRole role)
        {
            if (organizationId == Guid.Empty)
            {
                throw new ArgumentException("Organization id is required.", nameof(organizationId));
            }

            if (userId == Guid.Empty)
            {
                throw new ArgumentException("User id is required.", nameof(userId));
            }

            OrganizationId = organizationId;
            UserId = userId;
            Role = role;
            CreatedAt = DateTimeOffset.UtcNow;
        }

        public Guid OrganizationId { get; private set; }

        public Guid UserId { get; private set; }

        public MembershipRole Role { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }
    }
}
