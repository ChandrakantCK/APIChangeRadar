using Radar.Domain.Common;

namespace Radar.Domain.Watches
{
    /// <summary>
    /// An organization following one spec source from the catalog: "tell us when Stripe's API changes".
    /// </summary>
    public sealed class Watch : ITenantOwned
    {
        // Used by EF Core when loading rows from the database.
        private Watch()
        {
        }

        public Watch(Guid organizationId, Guid specSourceId, Guid createdByUserId)
        {
            if (organizationId == Guid.Empty)
            {
                throw new ArgumentException("Organization id is required.", nameof(organizationId));
            }

            if (specSourceId == Guid.Empty)
            {
                throw new ArgumentException("Spec source id is required.", nameof(specSourceId));
            }

            Id = Guid.CreateVersion7();
            OrganizationId = organizationId;
            SpecSourceId = specSourceId;
            CreatedByUserId = createdByUserId;
            CreatedAt = DateTimeOffset.UtcNow;
        }

        public Guid Id { get; private set; }

        public Guid OrganizationId { get; private set; }

        public Guid SpecSourceId { get; private set; }

        /// <summary>Who added it. Kept even if that user is later deleted, so no foreign key.</summary>
        public Guid CreatedByUserId { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }
    }
}
