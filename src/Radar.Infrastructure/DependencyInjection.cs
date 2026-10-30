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
using Radar.Infrastructure.Authentication;
using Radar.Application.Organizations;
using Radar.Infrastructure.Organizations;
using Radar.Infrastructure.Tenancy;
using Radar.Application.Catalog;
using Radar.Infrastructure.Catalog;

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

                // Brute-force protection: 5 wrong passwords in a row lock the account for 15 minutes.
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
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

            // Which email sender: Resend's HTTP API (production), SMTP (Mailpit locally), or just log (nothing configured).
            if (!string.IsNullOrWhiteSpace(configuration[$"{EmailOptions.SectionName}:Resend:ApiKey"]))
            {
                services.AddHttpClient<IEmailSender, ResendEmailSender>(client =>
                {
                    client.BaseAddress = new Uri("https://api.resend.com/");
                    client.Timeout = TimeSpan.FromSeconds(15);
                });
            }
            else if (!string.IsNullOrWhiteSpace(configuration[$"{EmailOptions.SectionName}:Smtp:Host"]))
            {
                services.AddSingleton<IEmailSender, SmtpEmailSender>();
            }
            else
            {
                services.AddSingleton<IEmailSender, LoggingEmailSender>();
            }

            // JWT settings are checked when the app STARTS (not when it is built), so a missing
            // key stops a real deployment but doesn't break tools like 'dotnet ef'.
            services.AddOptions<JwtOptions>()
                .Bind(configuration.GetSection(JwtOptions.SectionName))
                .Validate(o => o.HasValidSigningKey(), "Jwt:SigningKey must be a base64 string of at least 32 bytes.")
                .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer) && !string.IsNullOrWhiteSpace(o.Audience), "Jwt:Issuer and Jwt:Audience are required.")
                .ValidateOnStart();

            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<ITokenService, JwtTokenService>();

            services.AddScoped<RefreshTokenService>();
            // One "current organization" per request: set by the API's OrganizationScopeFilter,
            // read by AppDbContext's tenant query filter.
            services.AddScoped<CurrentOrganizationContext>();
            services.AddScoped<ICurrentOrganization>(sp => sp.GetRequiredService<CurrentOrganizationContext>());
            services.AddScoped<IOrganizationService, OrganizationService>();
            services.AddScoped<ICatalogService, CatalogService>();
            services.AddScoped<IAccountService, AccountService>();

            services.AddHealthChecks()
                .AddDbContextCheck<AppDbContext>("database");

            return services;
        }
    }
}
