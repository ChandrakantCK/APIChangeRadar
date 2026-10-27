using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radar.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

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
        }

        async Task IAsyncLifetime.DisposeAsync()
        {
            await _postgres.DisposeAsync();
            await base.DisposeAsync();
        }
    }
}
