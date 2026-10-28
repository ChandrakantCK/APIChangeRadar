using System.Net;
using System.Net.Http.Json;
using System.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radar.Application.Accounts;
using Radar.Domain.Organizations;
using Radar.Infrastructure.Identity;
using Radar.Infrastructure.Persistence;
using Radar.Integration.Tests.Infrastructure;

namespace Radar.Integration.Tests.Auth
{
    [Collection(ApiCollection.Name)]
    public class GitHubLoginTests(RadarApiFactory factory)
    {
        private const string Password = "correct horse battery staple";

        private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";

        private static string UniqueGitHubId() => Random.Shared.NextInt64(1, long.MaxValue).ToString();

        // The account-linking rules are tested directly on the service: no real GitHub needed.
        private async Task<LoginResult> GitHubLoginAsync(string gitHubId, string? verifiedEmail)
        {
            using var scope = factory.Services.CreateScope();
            var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
            return await accounts.ExternalLoginAsync(new ExternalLoginRequest("GitHub", gitHubId, verifiedEmail));
        }

        private async Task<ApplicationUser?> FindByGitHubIdAsync(string gitHubId)
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByLoginAsync("GitHub", gitHubId);
        }

        [Fact]
        public async Task New_github_user_gets_an_account_without_a_password_and_their_own_organization()
        {
            var gitHubId = UniqueGitHubId();
            var email = UniqueEmail();

            var first = await GitHubLoginAsync(gitHubId, email);
            var again = await GitHubLoginAsync(gitHubId, "changed-on-github@example.com");

            Assert.Equal(LoginStatus.Succeeded, first.Status);
            Assert.NotNull(first.RefreshToken);
            Assert.Equal(LoginStatus.Succeeded, again.Status);

            using var scope = factory.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByLoginAsync("GitHub", gitHubId);
            Assert.NotNull(user);
            Assert.Equal(email, user.Email); // signing in again used the link, not the new email
            Assert.True(user.EmailConfirmed);
            Assert.False(await userManager.HasPasswordAsync(user));

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var membership = await db.Memberships.SingleAsync(m => m.UserId == user.Id);
            Assert.Equal(MembershipRole.Owner, membership.Role);
        }

        [Fact]
        public async Task Existing_verified_account_with_the_same_email_is_linked_and_keeps_its_password()
        {
            var client = factory.CreateClient();
            var email = UniqueEmail();
            (await client.PostAsJsonAsync("/auth/signup", new { email, password = Password })).EnsureSuccessStatusCode();
            using (var scope = factory.Services.CreateScope())
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = await userManager.FindByEmailAsync(email);
                await userManager.ConfirmEmailAsync(user!, await userManager.GenerateEmailConfirmationTokenAsync(user!));
            }

            var gitHubId = UniqueGitHubId();
            var result = await GitHubLoginAsync(gitHubId, email);

            Assert.Equal(LoginStatus.Succeeded, result.Status);
            Assert.Equal(email, (await FindByGitHubIdAsync(gitHubId))!.Email);
            var passwordLogin = await client.PostAsJsonAsync("/auth/login", new { email, password = Password });
            Assert.Equal(HttpStatusCode.OK, passwordLogin.StatusCode);
        }

        // Account pre-hijacking: an attacker signs up with the victim's email first and never verifies it.
        [Fact]
        public async Task Unverified_account_is_taken_over_safely_and_the_attackers_password_stops_working()
        {
            var client = factory.CreateClient();
            var victimEmail = UniqueEmail();
            const string attackerPassword = "attacker chose this password";
            (await client.PostAsJsonAsync("/auth/signup", new { email = victimEmail, password = attackerPassword })).EnsureSuccessStatusCode();

            var result = await GitHubLoginAsync(UniqueGitHubId(), victimEmail);

            Assert.Equal(LoginStatus.Succeeded, result.Status);
            var attackerLogin = await client.PostAsJsonAsync("/auth/login", new { email = victimEmail, password = attackerPassword });
            Assert.Equal(HttpStatusCode.Unauthorized, attackerLogin.StatusCode);
        }

        [Fact]
        public async Task Github_account_without_a_verified_email_is_refused()
        {
            var gitHubId = UniqueGitHubId();

            var result = await GitHubLoginAsync(gitHubId, verifiedEmail: null);

            Assert.Equal(LoginStatus.EmailNotConfirmed, result.Status);
            Assert.Null(await FindByGitHubIdAsync(gitHubId));
        }

        [Fact]
        public async Task Start_is_404_when_github_is_not_configured()
        {
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.GetAsync("/auth/github/start");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task Start_redirects_to_github_with_client_id_callback_and_state()
        {
            var client = factory
                .WithWebHostBuilder(builder =>
                {
                    builder.UseSetting("GitHub:ClientId", "test-client-id");
                    builder.UseSetting("GitHub:ClientSecret", "test-client-secret");
                })
                .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.GetAsync("/auth/github/start");

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            var location = response.Headers.Location!;
            Assert.Equal("github.com", location.Host);
            var query = HttpUtility.ParseQueryString(location.Query);
            Assert.Equal("test-client-id", query["client_id"]);
            Assert.EndsWith("/auth/github/callback", query["redirect_uri"]);
            Assert.Contains("user:email", query["scope"]);
            Assert.False(string.IsNullOrEmpty(query["state"]));
        }
    }
}