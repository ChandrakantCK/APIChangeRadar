using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Radar.Infrastructure.Persistence;
using Radar.Application.Accounts;
using Radar.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Radar.Application.Abstractions;
using Radar.Infrastructure.Email;

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
            }).AddEntityFrameworkStores<AppDbContext>()
              .AddDefaultTokenProviders();

            // Email confirmation (and later password reset) links stay valid for 24 hours.
            services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(24));

            // The keys that sign those tokens live in Postgres, so they survive restarts
            // and are shared by every instance of the API.
            services.AddDataProtection()
                .SetApplicationName("APIChangeRadar")
                .PersistKeysToDbContext<AppDbContext>();

            services.Configure<AppOptions>(configuration.GetSection(AppOptions.SectionName));
            services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));

            if (string.IsNullOrWhiteSpace(configuration[$"{EmailOptions.SectionName}:Smtp:Host"]))
            {
                services.AddSingleton<IEmailSender, LoggingEmailSender>();
            }
            else
            {
                services.AddSingleton<IEmailSender, SmtpEmailSender>();
            }

            services.AddScoped<IAccountService, AccountService>();

            services.AddHealthChecks()
                .AddDbContextCheck<AppDbContext>("database");

            return services;
        }
    }
}
