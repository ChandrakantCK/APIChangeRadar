using System.Net.Http.Json;
using Radar.Api.Endpoints;
using Radar.Integration.Tests.Infrastructure;

namespace Radar.Integration.Tests.Networking
{
    [Collection(ApiCollection.Name)]
    public class ForwardedHeadersTests(RadarApiFactory factory)
    {
        private async Task<ClientInfo> GetClientInfoAsync(string forwardedFor, string? forwardedProto = null)
        {
            var client = factory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Get, "/diagnostics/client");
            request.Headers.Add("X-Forwarded-For", forwardedFor);
            if (forwardedProto is not null)
            {
                request.Headers.Add("X-Forwarded-Proto", forwardedProto);
            }

            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<ClientInfo>())!;
        }

        [Fact]
        public async Task Client_ip_is_found_behind_cloudflare_and_render()
        {
            // user -> Cloudflare edge (104.16.0.10) -> Render load balancer (10.0.0.5) -> API
            var info = await GetClientInfoAsync("203.0.113.50, 104.16.0.10, 10.0.0.5");

            Assert.Equal("203.0.113.50", info.ClientIp);
        }

        [Fact]
        public async Task Values_a_client_adds_itself_are_ignored()
        {
            // An attacker sends "X-Forwarded-For: 6.6.6.6"; the proxies append the real address after it.
            var info = await GetClientInfoAsync("6.6.6.6, 203.0.113.51, 104.16.0.10");

            Assert.Equal("203.0.113.51", info.ClientIp);
        }

        [Fact]
        public async Task Fewer_proto_values_than_for_values_still_works()
        {
            var info = await GetClientInfoAsync("203.0.113.52, 104.16.0.10", forwardedProto: "https");

            Assert.Equal("203.0.113.52", info.ClientIp);
            Assert.Equal("https", info.Scheme);
        }
    }
}