using System.Net;
using System.Net.Http.Json;
using System.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Radar.Api.Authentication;
using Radar.Api.Endpoints;
using Radar.Api.Networking;
using Radar.Integration.Tests.Infrastructure;

namespace Radar.Integration.Tests.Networking
{
    [Collection(ApiCollection.Name)]
    public class WebProxyTests(RadarApiFactory factory)
    {
        private const string ProxySecret = "test-proxy-secret";

        private HttpClient CreateClientWithProxySecret(bool withGitHub = false) =>
            factory.WithWebHostBuilder(builder =>
                {
                    builder.UseSetting("WebProxy:Secret", ProxySecret);
                    if (withGitHub)
                    {
                        builder.UseSetting("GitHub:ClientId", "test-client-id");
                        builder.UseSetting("GitHub:ClientSecret", "test-client-secret");
                    }
                })
                .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

        // What the Vercel proxy adds to every forwarded browser request.
        private static HttpRequestMessage ViaWebProxy(HttpMethod method, string path, string secret = ProxySecret)
        {
            var request = new HttpRequestMessage(method, path);
            request.Headers.Add(TrustedWebProxyMiddleware.SecretHeader, secret);
            request.Headers.Add(TrustedWebProxyMiddleware.ClientIpHeader, "198.51.100.7");
            request.Headers.Add(TrustedWebProxyMiddleware.HostHeader, "api-change-radar.vercel.app");
            request.Headers.Add(TrustedWebProxyMiddleware.ProtoHeader, "https");
            request.Headers.Add(TrustedWebProxyMiddleware.PathBaseHeader, "/api");
            return request;
        }

        [Fact]
        public async Task Requests_with_the_right_secret_carry_the_real_client_and_web_address()
        {
            var client = CreateClientWithProxySecret();

            var info = await (await client.SendAsync(ViaWebProxy(HttpMethod.Get, "/diagnostics/client"))).Content.ReadFromJsonAsync<ClientInfo>();

            Assert.Equal("198.51.100.7", info!.ClientIp);
            Assert.Equal("https", info.Scheme);
            Assert.Equal("api-change-radar.vercel.app", info.Host);
            Assert.Equal("/api", info.PathBase);
        }

        [Fact]
        public async Task Wrong_or_unconfigured_secret_is_ignored()
        {
            var configured = CreateClientWithProxySecret();
            var notConfigured = factory.CreateClient();

            var wrongSecret = await (await configured.SendAsync(ViaWebProxy(HttpMethod.Get, "/diagnostics/client", secret: "guess"))).Content.ReadFromJsonAsync<ClientInfo>();
            var noSecretSet = await (await notConfigured.SendAsync(ViaWebProxy(HttpMethod.Get, "/diagnostics/client"))).Content.ReadFromJsonAsync<ClientInfo>();

            Assert.NotEqual("198.51.100.7", wrongSecret!.ClientIp);
            Assert.Equal(string.Empty, wrongSecret.PathBase);
            Assert.NotEqual("198.51.100.7", noSecretSet!.ClientIp);
            Assert.Equal(string.Empty, noSecretSet.PathBase);
        }

        [Fact]
        public async Task Through_the_proxy_the_refresh_cookie_path_is_api_auth()
        {
            var client = CreateClientWithProxySecret();

            var logout = await client.SendAsync(ViaWebProxy(HttpMethod.Post, "/auth/logout"));

            var setCookie = logout.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith(RefreshTokenCookie.Name + "="));
            Assert.Contains("path=/api/auth", setCookie, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Through_the_proxy_github_calls_back_to_the_web_domain()
        {
            var client = CreateClientWithProxySecret(withGitHub: true);

            var start = await client.SendAsync(ViaWebProxy(HttpMethod.Get, "/auth/github/start"));

            Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
            var query = HttpUtility.ParseQueryString(start.Headers.Location!.Query);
            Assert.Equal("https://api-change-radar.vercel.app/api/auth/github/callback", query["redirect_uri"]);
        }
    }
}
