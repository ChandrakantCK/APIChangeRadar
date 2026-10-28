using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radar.Api.Authentication;
using Radar.Api.Endpoints;
using Radar.Infrastructure.Identity;
using Radar.Infrastructure.Persistence;
using Radar.Integration.Tests.Infrastructure;

namespace Radar.Integration.Tests.Auth
{
    [Collection(ApiCollection.Name)]
    public class RefreshTokenTests(RadarApiFactory factory)
    {
        private const string ValidPassword = "correct horse battery staple";

        // We handle cookies by hand so tests can replay an OLD refresh token on purpose.
        private HttpClient CreateClient() =>
            factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        private async Task<(string Email, string RefreshToken)> SignUpAndLogInAsync(HttpClient client)
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
            return (email, ReadRefreshCookie(login)!);
        }

        private static string? SetCookieHeader(HttpResponseMessage response) =>
            response.Headers.TryGetValues("Set-Cookie", out var values)
                ? values.FirstOrDefault(v => v.StartsWith(RefreshTokenCookie.Name + "="))
                : null;

        private static string? ReadRefreshCookie(HttpResponseMessage response)
        {
            var header = SetCookieHeader(response);
            var value = header?.Split(';')[0][(RefreshTokenCookie.Name.Length + 1)..];
            return string.IsNullOrEmpty(value) ? null : value;
        }

        private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string? refreshToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh");
            if (refreshToken is not null)
            {
                request.Headers.Add("Cookie", $"{RefreshTokenCookie.Name}={refreshToken}");
            }

            return client.SendAsync(request);
        }

        [Fact]
        public async Task Login_sets_a_locked_down_cookie_and_stores_only_a_hash()
        {
            var client = CreateClient();
            var email = $"user-{Guid.NewGuid():N}@example.com";
            (await client.PostAsJsonAsync("/auth/signup", new { email, password = ValidPassword })).EnsureSuccessStatusCode();
            using (var scope = factory.Services.CreateScope())
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = await userManager.FindByEmailAsync(email);
                await userManager.ConfirmEmailAsync(user!, await userManager.GenerateEmailConfirmationTokenAsync(user!));
            }

            var login = await client.PostAsJsonAsync("/auth/login", new { email, password = ValidPassword });

            var header = SetCookieHeader(login)!.ToLowerInvariant();
            Assert.Contains("httponly", header);
            Assert.Contains("secure", header);
            Assert.Contains("samesite=strict", header);
            Assert.Contains("path=/auth", header);

            var raw = ReadRefreshCookie(login)!;
            using var check = factory.Services.CreateScope();
            var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.RefreshTokens.AnyAsync(t => t.TokenHash == raw));
            Assert.True(await db.RefreshTokens.AnyAsync(t => t.TokenHash == RefreshTokenService.Hash(raw)));
        }

        [Fact]
        public async Task Refresh_returns_a_new_access_token_and_rotates_the_refresh_token()
        {
            var client = CreateClient();
            var (_, first) = await SignUpAndLogInAsync(client);

            var response = await RefreshAsync(client, first);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
            Assert.False(string.IsNullOrEmpty(body!.AccessToken));

            var second = ReadRefreshCookie(response);
            Assert.NotNull(second);
            Assert.NotEqual(first, second);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var old = await db.RefreshTokens.SingleAsync(t => t.TokenHash == RefreshTokenService.Hash(first));
            var replacement = await db.RefreshTokens.SingleAsync(t => t.TokenHash == RefreshTokenService.Hash(second));
            Assert.NotNull(old.RevokedAt);
            Assert.Equal(replacement.Id, old.ReplacedById);
            Assert.Equal(old.FamilyId, replacement.FamilyId);
        }

        // The Phase 1 "done" test: replaying a used refresh token kills the whole session.
        [Fact]
        public async Task Reusing_a_rotated_refresh_token_revokes_the_whole_family()
        {
            var client = CreateClient();
            var (_, first) = await SignUpAndLogInAsync(client);

            // The legitimate client refreshes: first -> second.
            var rotated = await RefreshAsync(client, first);
            var second = ReadRefreshCookie(rotated)!;

            // An attacker replays the stolen, already-used first token.
            var replay = await RefreshAsync(client, first);
            Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

            // Now even the legitimate newest token is dead: the whole family was revoked.
            var afterReuse = await RefreshAsync(client, second);
            Assert.Equal(HttpStatusCode.Unauthorized, afterReuse.StatusCode);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var familyId = (await db.RefreshTokens.SingleAsync(t => t.TokenHash == RefreshTokenService.Hash(first))).FamilyId;
            var family = await db.RefreshTokens.Where(t => t.FamilyId == familyId).ToListAsync();
            Assert.All(family, t => Assert.NotNull(t.RevokedAt));
            Assert.Contains(family, t => t.RevokedReason == "reuse-detected");
        }

        [Fact]
        public async Task Missing_or_unknown_refresh_token_is_401_and_clears_the_cookie()
        {
            var client = CreateClient();

            var missing = await RefreshAsync(client, refreshToken: null);
            var unknown = await RefreshAsync(client, "not-a-real-token");

            Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
            Assert.Null(ReadRefreshCookie(unknown));
        }

        [Fact]
        public async Task Expired_refresh_token_is_401()
        {
            var client = CreateClient();
            var (_, token) = await SignUpAndLogInAsync(client);

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.RefreshTokens
                    .Where(t => t.TokenHash == RefreshTokenService.Hash(token))
                    .ExecuteUpdateAsync(set => set.SetProperty(t => t.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
            }

            var response = await RefreshAsync(client, token);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}