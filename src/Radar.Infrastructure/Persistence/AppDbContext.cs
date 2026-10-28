using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Radar.Domain.Organizations;
using Radar.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

namespace Radar.Infrastructure.Persistence
{
    public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityUserContext<ApplicationUser, Guid>(options), IDataProtectionKeyContext
    {
        public DbSet<Organization> Organizations => Set<Organization>();
        public DbSet<Membership> Memberships => Set<Membership>();
        public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Identity's own tables and keys come first.
            base.OnModelCreating(modelBuilder);

            // Friendlier table names than "asp_net_users" etc.
            modelBuilder.Entity<ApplicationUser>().ToTable("users");
            modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
            modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
            modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");

            modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        }
    }
}
