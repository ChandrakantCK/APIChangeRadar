using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Radar.Domain.Catalog;
using Radar.Domain.Crawling;
using Radar.Domain.Snapshots;

namespace Radar.Infrastructure.Persistence.Configurations
{
    internal sealed class SpecSnapshotConfiguration : IEntityTypeConfiguration<SpecSnapshot>
    {
        public void Configure(EntityTypeBuilder<SpecSnapshot> builder)
        {
            builder.ToTable("spec_snapshots");
            builder.HasKey(s => s.Id);

            builder.Property(s => s.Hash).HasMaxLength(64).IsFixedLength();
            builder.Property(s => s.SpecVersion).HasMaxLength(SpecSnapshot.SpecVersionMaxLength).IsRequired();
            builder.Property(s => s.ApiVersion).HasMaxLength(SpecSnapshot.ApiVersionMaxLength);
            builder.Property(s => s.CompressedContent).IsRequired();

            builder.HasOne<SpecSource>()
                .WithMany()
                .HasForeignKey(s => s.SpecSourceId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne<CrawlRun>()
                .WithMany()
                .HasForeignKey(s => s.CrawlRunId)
                .OnDelete(DeleteBehavior.Cascade);

            // "The latest snapshot of this source" is the common question.
            builder.HasIndex(s => new { s.SpecSourceId, s.CapturedAt });
        }
    }
}
