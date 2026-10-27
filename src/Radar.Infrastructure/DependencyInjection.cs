using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Radar.Infrastructure.Persistence;
using Radar.Application.Accounts;
using Radar.Infrastructure.Identity;

namespace Radar.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("Database")
                ?? throw new InvalidOperationException("Connection string 'Database' is not configured.");

            services.AddDbContext<AppDbContext>(options => options
                .UseNpgsql(connectionString)
                .UseSnakeCaseNamingConvention());

            services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;

                // NIST SP 800-63B: length matters; forced character mixes do not.
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
            }).AddEntityFrameworkStores<AppDbContext>();

            services.AddScoped<IAccountService, AccountService>();

            services.AddHealthChecks()
                .AddDbContextCheck<AppDbContext>("database");

            return services;
        }
    }
}
