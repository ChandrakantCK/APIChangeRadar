using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Radar.Domain.Auditing;
using Radar.Domain.Organizations;

namespace Radar.Infrastructure.Persistence.Configurations
{
    internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
    {
        public void Configure(EntityTypeBuilder<AuditEntry> builder)
        {
            builder.HasKey(e => e.Id);

            builder.Property(e => e.Action).HasMaxLength(AuditEntry.ActionMaxLength).IsRequired();
            builder.Property(e => e.Target).HasMaxLength(AuditEntry.TargetMaxLength);
            builder.Property(e => e.IpAddress).HasMaxLength(45); // long enough for IPv6

            builder.HasOne<Organization>()
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            // The audit log is always read "for one organization, newest first".
            builder.HasIndex(e => new { e.OrganizationId, e.OccurredAt });
        }
    }
}