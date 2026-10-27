using Radar.Integration.Tests.Infrastructure;
using System.Net;

namespace Radar.Integration.Tests.Health
{
    [Collection(ApiCollection.Name)]
    public class HealthEndpointTests(RadarApiFactory factory)
    {
        [Fact]
        public async Task Health_returns_200_and_Healthy_when_database_is_reachable()
        {
            var client = factory.CreateClient();

            var response = await client.GetAsync("/health");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
        }
    }
}
