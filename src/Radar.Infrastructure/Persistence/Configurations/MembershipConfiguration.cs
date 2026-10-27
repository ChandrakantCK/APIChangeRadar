using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Radar.Domain.Organizations;
using Radar.Infrastructure.Identity;

namespace Radar.Infrastructure.Persistence.Configurations
{
    internal sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
    {
        public void Configure(EntityTypeBuilder<Membership> builder)
        {
            // A user can be in an organization only once.
            builder.HasKey(m => new { m.OrganizationId, m.UserId });

            builder.HasOne<Organization>()
                .WithMany()
                .HasForeignKey(m => m.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // "Which organizations is this user in?" is a common query.
            builder.HasIndex(m => m.UserId);

            builder.Property(m => m.Role)
                .HasConversion<string>()
                .HasMaxLength(20);
        }
    }
}
