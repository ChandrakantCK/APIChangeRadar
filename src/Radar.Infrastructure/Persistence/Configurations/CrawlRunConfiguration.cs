using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Radar.Domain.Catalog;
using Radar.Domain.Crawling;

namespace Radar.Infrastructure.Persistence.Configurations
{
    internal sealed class CrawlRunConfiguration : IEntityTypeConfiguration<CrawlRun>
    {
        public void Configure(EntityTypeBuilder<CrawlRun> builder)
        {
            builder.HasKey(r => r.Id);

            builder.Property(r => r.Outcome)
                .HasConversion<string>()
                .HasMaxLength(20);

            builder.Property(r => r.ETag).HasColumnName("etag").HasMaxLength(CrawlRun.ETagMaxLength);
            builder.Property(r => r.ContentHash).HasMaxLength(64).IsFixedLength();
            builder.Property(r => r.Error).HasMaxLength(CrawlRun.ErrorMaxLength);

            builder.HasOne<SpecSource>()
                .WithMany()
                .HasForeignKey(r => r.SpecSourceId)
                .OnDelete(DeleteBehavior.Cascade);

            // Always read as "the latest run(s) for this source".
            builder.HasIndex(r => new { r.SpecSourceId, r.StartedAt });
        }
    }
}
