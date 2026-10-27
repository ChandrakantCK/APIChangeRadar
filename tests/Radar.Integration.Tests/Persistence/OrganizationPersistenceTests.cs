using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radar.Domain.Organizations;
using Radar.Infrastructure.Persistence;
using Radar.Integration.Tests.Infrastructure;

namespace Radar.Integration.Tests.Persistence
{
    [Collection(ApiCollection.Name)]
    public class OrganizationPersistenceTests(RadarApiFactory factory)
    {
        [Fact]
        public async Task Organization_round_trips_through_the_database()
        {
            var organization = new Organization("  Test Corp  ");

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Organizations.Add(organization);
                await db.SaveChangesAsync();
            }

            // A fresh scope means a fresh DbContext, so this really reads from Postgres.
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var loaded = await db.Organizations.SingleAsync(o => o.Id == organization.Id);

                Assert.Equal("Test Corp", loaded.Name);
                Assert.Equal(OrganizationPlan.Free, loaded.Plan);
                Assert.Null(loaded.TrialEndsAt);
            }
        }
    }
}
