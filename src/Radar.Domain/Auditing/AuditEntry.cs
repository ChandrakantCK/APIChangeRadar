using Radar.Domain.Common;

namespace Radar.Domain.Auditing
{
    /// <summary>
    /// A record of a security-relevant action inside an organization ("who did what, when, from where").
    /// Entries are only ever added, never changed.
    /// </summary>
    public sealed class AuditEntry : ITenantOwned
    {
        public const int ActionMaxLength = 100;
        public const int TargetMaxLength = 200;

        // Used by EF Core when loading rows from the database.
        private AuditEntry()
        {
        }

        public AuditEntry(Guid organizationId, Guid? actorUserId, string action, string? target, string? ipAddress)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                throw new ArgumentException("Action is required.", nameof(action));
            }

            Id = Guid.CreateVersion7();
            OrganizationId = organizationId;
            ActorUserId = actorUserId;
            Action = action;
            Target = target;
            IpAddress = ipAddress;
            OccurredAt = DateTimeOffset.UtcNow;
        }

        public Guid Id { get; private set; }

        public Guid OrganizationId { get; private set; }

        /// <summary>Who did it. Kept even if that user is later deleted, so no foreign key.</summary>
        public Guid? ActorUserId { get; private set; }

        /// <summary>For example "organization.created" or "organization.renamed".</summary>
        public string Action { get; private set; } = null!;

        public string? Target { get; private set; }

        public string? IpAddress { get; private set; }

        public DateTimeOffset OccurredAt { get; private set; }
    }
}