using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Radar.Domain.Catalog;
using Radar.Domain.Changes;
using Radar.Domain.Snapshots;

namespace Radar.Infrastructure.Persistence.Configurations
{
    internal sealed class ChangeSetConfiguration : IEntityTypeConfiguration<ChangeSet>
    {
        public void Configure(EntityTypeBuilder<ChangeSet> builder)
        {
            builder.HasKey(c => c.Id);

            builder.Property(c => c.FromApiVersion).HasMaxLength(SpecSnapshot.ApiVersionMaxLength);
            builder.Property(c => c.ToApiVersion).HasMaxLength(SpecSnapshot.ApiVersionMaxLength);

            builder.HasOne<SpecSource>()
                .WithMany()
                .HasForeignKey(c => c.SpecSourceId)
                .OnDelete(DeleteBehavior.Cascade);

            // Snapshots are pruned (newest 5 kept) but the history of changes stays: the link just becomes null.
            builder.HasOne<SpecSnapshot>()
                .WithMany()
                .HasForeignKey(c => c.FromSnapshotId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasOne<SpecSnapshot>()
                .WithMany()
                .HasForeignKey(c => c.ToSnapshotId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasMany(c => c.Changes)
                .WithOne()
                .HasForeignKey(c => c.ChangeSetId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.Navigation(c => c.Changes).UsePropertyAccessMode(PropertyAccessMode.Field);

            // The feed: newest first, for the sources an organization watches.
            builder.HasIndex(c => new { c.SpecSourceId, c.DetectedAt });
        }
    }
}
