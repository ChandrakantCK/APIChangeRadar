using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Radar.Integration.Tests.Infrastructure;

namespace Radar.Integration.Tests.Auth
{
    [Collection(ApiCollection.Name)]
    public class RateLimitTests(RadarApiFactory factory)
    {
        // A copy of the app with a tiny limit. It has its own rate-limiter state, so other tests aren't affected.
        private HttpClient CreateClientWithLimit(int permitLimit) =>
            factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:AuthPermitLimit", permitLimit.ToString()))
                .CreateClient();

        // Simulates a request that arrived through Render's proxy from the given client IP.
        private static Task<HttpResponseMessage> LoginFromAsync(HttpClient client, string clientIp)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
            {
                Content = JsonContent.Create(new { email = "nobody@example.com", password = "wrong password here" }),
            };
            request.Headers.Add("X-Forwarded-For", clientIp);
            return client.SendAsync(request);
        }

        [Fact]
        public async Task Too_many_auth_requests_from_one_ip_get_429_with_retry_after()
        {
            var client = CreateClientWithLimit(3);

            for (var i = 0; i < 3; i++)
            {
                Assert.Equal(HttpStatusCode.Unauthorized, (await LoginFromAsync(client, "203.0.113.10")).StatusCode);
            }

            var blocked = await LoginFromAsync(client, "203.0.113.10");

            Assert.Equal((HttpStatusCode)429, blocked.StatusCode);
            Assert.True(blocked.Headers.RetryAfter is not null, "429 should tell the client when to retry.");
        }

        [Fact]
        public async Task Each_client_ip_has_its_own_allowance()
        {
            var client = CreateClientWithLimit(3);

            for (var i = 0; i < 4; i++)
            {
                await LoginFromAsync(client, "203.0.113.20");
            }

            // A different client (as reported by the proxy) is not punished for someone else's requests.
            var otherClient = await LoginFromAsync(client, "203.0.113.21");

            Assert.Equal(HttpStatusCode.Unauthorized, otherClient.StatusCode);
        }
    }
}