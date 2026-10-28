using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radar.Api.Authentication;
using Radar.Infrastructure.Identity;
using Radar.Infrastructure.Persistence;
using Radar.Integration.Tests.Infrastructure;

namespace Radar.Integration.Tests.Auth
{
    [Collection(ApiCollection.Name)]
    public class LogoutTests(RadarApiFactory factory)
    {
        private const string ValidPassword = "correct horse battery staple";

        private HttpClient CreateClient() =>
            factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        private async Task<string> SignUpAndLogInAsync(HttpClient client)
        {
            var email = $"user-{Guid.NewGuid():N}@example.com";
            (await client.PostAsJsonAsync("/auth/signup", new { email, password = ValidPassword })).EnsureSuccessStatusCode();

            using (var scope = factory.Services.CreateScope())
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = await userManager.FindByEmailAsync(email);
                await userManager.ConfirmEmailAsync(user!, await userManager.GenerateEmailConfirmationTokenAsync(user!));
            }

            var login = await client.PostAsJsonAsync("/auth/login", new { email, password = ValidPassword });
            login.EnsureSuccessStatusCode();
            return ReadRefreshCookie(login)!;
        }

        private static string? ReadRefreshCookie(HttpResponseMessage response)
        {
            if (!response.Headers.TryGetValues("Set-Cookie", out var values))
            {
                return null;
            }

            var header = values.FirstOrDefault(v => v.StartsWith(RefreshTokenCookie.Name + "="));
            var value = header?.Split(';')[0][(RefreshTokenCookie.Name.Length + 1)..];
            return string.IsNullOrEmpty(value) ? null : value;
        }

        private static Task<HttpResponseMessage> PostWithCookieAsync(HttpClient client, string path, string? refreshToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, path);
            if (refreshToken is not null)
            {
                request.Headers.Add("Cookie", $"{RefreshTokenCookie.Name}={refreshToken}");
            }

            return client.SendAsync(request);
        }

        [Fact]
        public async Task Logout_revokes_the_session_and_clears_the_cookie()
        {
            var client = CreateClient();
            var refreshToken = await SignUpAndLogInAsync(client);

            var logout = await PostWithCookieAsync(client, "/auth/logout", refreshToken);

            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
            var setCookie = logout.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith(RefreshTokenCookie.Name + "="));
            Assert.Contains("expires=Thu, 01 Jan 1970", setCookie, StringComparison.OrdinalIgnoreCase);

            // The refresh token no longer works.
            var refresh = await PostWithCookieAsync(client, "/auth/refresh", refreshToken);
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var token = await db.RefreshTokens.SingleAsync(t => t.TokenHash == RefreshTokenService.Hash(refreshToken));
            Assert.Equal("logout", token.RevokedReason);
        }

        [Fact]
        public async Task Logout_without_a_session_is_still_204()
        {
            var client = CreateClient();

            var response = await PostWithCookieAsync(client, "/auth/logout", refreshToken: null);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
    }
}