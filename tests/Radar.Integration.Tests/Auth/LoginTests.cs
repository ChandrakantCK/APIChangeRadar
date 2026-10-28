using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Radar.Api.Endpoints;
using Radar.Application.Accounts;
using Radar.Infrastructure.Identity;
using Radar.Integration.Tests.Infrastructure;

namespace Radar.Integration.Tests.Auth
{
    [Collection(ApiCollection.Name)]
    public class LoginTests(RadarApiFactory factory)
    {
        private const string ValidPassword = "correct horse battery staple";

        private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";

        private async Task<string> SignUpAsync(HttpClient client, bool confirmEmail)
        {
            var email = UniqueEmail();
            var response = await client.PostAsJsonAsync("/auth/signup", new { email, password = ValidPassword, organizationName = "Acme" });
            response.EnsureSuccessStatusCode();

            if (confirmEmail)
            {
                // Shortcut for tests: confirm directly instead of going through the email link.
                using var scope = factory.Services.CreateScope();
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = await userManager.FindByEmailAsync(email);
                var token = await userManager.GenerateEmailConfirmationTokenAsync(user!);
                await userManager.ConfirmEmailAsync(user!, token);
            }

            return email;
        }

        private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
            client.PostAsJsonAsync("/auth/login", new { email, password });

        [Fact]
        public async Task Verified_user_gets_a_token_that_opens_me()
        {
            var client = factory.CreateClient();
            var email = await SignUpAsync(client, confirmEmail: true);

            var login = await LoginAsync(client, email, ValidPassword);

            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
            Assert.NotNull(body);
            Assert.Equal("Bearer", body.TokenType);
            Assert.InRange(body.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(14), DateTimeOffset.UtcNow.AddMinutes(16));

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
            var me = await client.GetFromJsonAsync<UserProfile>("/me");

            Assert.NotNull(me);
            Assert.Equal(email, me.Email);
            Assert.True(me.EmailConfirmed);
            var organization = Assert.Single(me.Organizations);
            Assert.Equal("Acme", organization.Name);
            Assert.Equal("Owner", organization.Role);
        }

        [Fact]
        public async Task Me_without_a_token_is_401()
        {
            var client = factory.CreateClient();

            var response = await client.GetAsync("/me");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Wrong_password_and_unknown_email_get_the_same_401()
        {
            var client = factory.CreateClient();
            var email = await SignUpAsync(client, confirmEmail: true);

            var wrongPassword = await LoginAsync(client, email, "not the right password");
            var unknownEmail = await LoginAsync(client, UniqueEmail(), ValidPassword);

            Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        }

        [Fact]
        public async Task Unverified_email_cannot_log_in()
        {
            var client = factory.CreateClient();
            var email = await SignUpAsync(client, confirmEmail: false);

            var response = await LoginAsync(client, email, ValidPassword);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Five_wrong_passwords_lock_the_account_even_for_the_right_one()
        {
            var client = factory.CreateClient();
            var email = await SignUpAsync(client, confirmEmail: true);

            for (var i = 0; i < 5; i++)
            {
                await LoginAsync(client, email, "not the right password");
            }

            var response = await LoginAsync(client, email, ValidPassword);

            Assert.Equal((HttpStatusCode)429, response.StatusCode);
        }

        [Fact]
        public async Task Token_signed_with_another_key_is_rejected()
        {
            var client = factory.CreateClient();
            var email = await SignUpAsync(client, confirmEmail: true);
            var login = await (await LoginAsync(client, email, ValidPassword)).Content.ReadFromJsonAsync<LoginResponse>();
            var real = new JsonWebTokenHandler().ReadJsonWebToken(login!.AccessToken);

            // Same claims, issuer and audience - but an attacker's key.
            var forged = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = real.Issuer,
                Audience = real.Audiences.Single(),
                Expires = DateTime.UtcNow.AddMinutes(10),
                Claims = new Dictionary<string, object> { [JwtRegisteredClaimNames.Sub] = real.Subject },
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)), SecurityAlgorithms.HmacSha256),
            });

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forged);
            var response = await client.GetAsync("/me");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}