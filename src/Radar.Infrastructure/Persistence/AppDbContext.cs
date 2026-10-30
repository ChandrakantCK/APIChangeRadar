using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Radar.Application.Abstractions;
using Radar.Domain.Auditing;
using Radar.Domain.Catalog;
using Radar.Domain.Common;
using Radar.Domain.Organizations;
using Radar.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

namespace Radar.Infrastructure.Persistence
{
    public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentOrganization currentOrganization)
        : IdentityUserContext<ApplicationUser, Guid>(options), IDataProtectionKeyContext
    {
        public DbSet<Organization> Organizations => Set<Organization>();
        public DbSet<Membership> Memberships => Set<Membership>();
        public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
        public DbSet<ApiProvider> ApiProviders => Set<ApiProvider>();
        public DbSet<SpecSource> SpecSources => Set<SpecSource>();

        // Read by the tenant query filter each time a query runs (EF Core re-evaluates context members).
        private Guid? CurrentOrganizationId => currentOrganization.OrganizationId;

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

            // Every ITenantOwned entity gets: WHERE organization_id = <current organization>.
            // With no current organization the comparison is with NULL, so nothing is returned (fail closed).
            var applyFilter = typeof(AppDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;
            foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                         .Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType)))
            {
                applyFilter.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
            }
        }

        private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
            where TEntity : class, ITenantOwned
        {
            modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            GuardTenantWrites();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            GuardTenantWrites();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        // Second line of defence: inside an organization-scoped request, refuse to write another organization's data.
        private void GuardTenantWrites()
        {
            if (CurrentOrganizationId is not { } current)
            {
                return;
            }

            foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
            {
                var isWrite = entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted;
                if (isWrite && entry.Entity.OrganizationId != current)
                {
                    throw new InvalidOperationException(
                        $"Refusing to write {entry.Entity.GetType().Name} belonging to another organization.");
                }
            }
        }
    }
}