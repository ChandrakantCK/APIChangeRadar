using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Radar.Domain.Organizations;

namespace Radar.Infrastructure.Persistence.Configurations
{
    internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
    {
        public void Configure(EntityTypeBuilder<Organization> builder)
        {
            builder.HasKey(o => o.Id);

            builder.Property(o => o.Name)
                .HasMaxLength(Organization.NameMaxLength)
                .IsRequired();

            builder.Property(o => o.Plan)
                .HasConversion<string>()
                .HasMaxLength(20);
        }
    }
}
