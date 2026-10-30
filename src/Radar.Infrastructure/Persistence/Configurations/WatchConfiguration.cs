using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Radar.Domain.Catalog;
using Radar.Domain.Organizations;
using Radar.Domain.Watches;

namespace Radar.Infrastructure.Persistence.Configurations
{
    internal sealed class WatchConfiguration : IEntityTypeConfiguration<Watch>
    {
        public void Configure(EntityTypeBuilder<Watch> builder)
        {
            builder.HasKey(w => w.Id);

            builder.HasOne<Organization>()
                .WithMany()
                .HasForeignKey(w => w.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            // A catalog source cannot be deleted while someone watches it (deactivate it instead).
            builder.HasOne<SpecSource>()
                .WithMany()
                .HasForeignKey(w => w.SpecSourceId)
                .OnDelete(DeleteBehavior.Restrict);

            // Watching the same source twice makes no sense; the database guarantees it even under races.
            builder.HasIndex(w => new { w.OrganizationId, w.SpecSourceId }).IsUnique();
        }
    }
}
