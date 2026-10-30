using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Radar.Domain.Catalog;
using Radar.Infrastructure.Persistence.Seed;

namespace Radar.Infrastructure.Persistence.Configurations
{
    internal sealed class ApiProviderConfiguration : IEntityTypeConfiguration<ApiProvider>
    {
        public void Configure(EntityTypeBuilder<ApiProvider> builder)
        {
            builder.HasKey(p => p.Id);

            builder.Property(p => p.Slug).HasMaxLength(ApiProvider.SlugMaxLength).IsRequired();
            builder.Property(p => p.Name).HasMaxLength(ApiProvider.NameMaxLength).IsRequired();
            builder.Property(p => p.WebsiteUrl).HasMaxLength(ApiProvider.UrlMaxLength).IsRequired();

            builder.HasIndex(p => p.Slug).IsUnique();

            builder.HasMany(p => p.Sources)
                .WithOne()
                .HasForeignKey(s => s.ProviderId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasData(CatalogSeed.Providers);
        }
    }
}
