using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radar.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Radar.Application.Abstractions;
using System.Security.Cryptography;

namespace Radar.Integration.Tests.Infrastructure
{
    /// <summary>
    /// Boots the real API in memory against a throwaway Postgres container.
    /// One instance (one container) is shared by every test class in the "Api" collection.
    /// </summary>
    public sealed class RadarApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
    {
        private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .Build();
        public FakeEmailSender Emails { get; } = new();

        // A fresh random signing key for every test run (32 bytes, base64).
        public string JwtSigningKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        public async Task InitializeAsync()
        {
            await _postgres.StartAsync();

            // Create the schema exactly as production does: by applying the migrations.
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Database", _postgres.GetConnectionString());
            builder.UseSetting("Jwt:SigningKey", JwtSigningKey);

            // All test requests come from one "client", so lift the auth rate limit for normal tests.
            // RateLimitTests lowers it again for itself.
            builder.UseSetting("RateLimiting:AuthPermitLimit", "100000");
            // Swap the real email sender for one that records messages in memory.
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(Emails);
            });
        }

        async Task IAsyncLifetime.DisposeAsync()
        {
            await _postgres.DisposeAsync();
            await base.DisposeAsync();
        }
    }
}
