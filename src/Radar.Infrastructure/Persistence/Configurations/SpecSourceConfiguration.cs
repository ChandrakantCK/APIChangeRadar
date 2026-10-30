using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Radar.Domain.Catalog;
using Radar.Infrastructure.Persistence.Seed;

namespace Radar.Infrastructure.Persistence.Configurations
{
    internal sealed class SpecSourceConfiguration : IEntityTypeConfiguration<SpecSource>
    {
        public void Configure(EntityTypeBuilder<SpecSource> builder)
        {
            builder.HasKey(s => s.Id);

            builder.Property(s => s.Slug).HasMaxLength(SpecSource.SlugMaxLength).IsRequired();
            builder.Property(s => s.Name).HasMaxLength(SpecSource.NameMaxLength).IsRequired();
            builder.Property(s => s.SpecUrl).HasMaxLength(SpecSource.UrlMaxLength).IsRequired();
            builder.Property(s => s.Repository).HasMaxLength(SpecSource.RepositoryMaxLength);
            builder.Property(s => s.Branch).HasMaxLength(SpecSource.BranchMaxLength);
            builder.Property(s => s.FilePath).HasMaxLength(SpecSource.FilePathMaxLength);

            builder.Property(s => s.Format)
                .HasConversion<string>()
                .HasMaxLength(20);

            // "stripe/api" style addresses: a slug is unique within its provider.
            builder.HasIndex(s => new { s.ProviderId, s.Slug }).IsUnique();

            builder.HasData(CatalogSeed.Sources);
        }
    }
}
