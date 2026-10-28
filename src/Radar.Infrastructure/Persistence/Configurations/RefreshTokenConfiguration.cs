using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Radar.Infrastructure.Identity;

namespace Radar.Infrastructure.Persistence.Configurations
{
    internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
    {
        public void Configure(EntityTypeBuilder<RefreshToken> builder)
        {
            builder.HasKey(t => t.Id);

            // Every refresh looks a token up by its hash, and a hash must never appear twice.
            builder.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
            builder.HasIndex(t => t.TokenHash).IsUnique();

            // Reuse detection revokes a whole family at once.
            builder.HasIndex(t => t.FamilyId);

            builder.Property(t => t.RevokedReason).HasMaxLength(50);

            builder.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}