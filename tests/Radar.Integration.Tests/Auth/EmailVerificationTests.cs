using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radar.Infrastructure.Persistence;
using Radar.Integration.Tests.Infrastructure;

namespace Radar.Integration.Tests.Auth
{
    [Collection(ApiCollection.Name)]
    public partial class EmailVerificationTests(RadarApiFactory factory)
    {
        private const string ValidPassword = "correct horse battery staple";

        private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";

        [GeneratedRegex(@"https?://\S+")]
        private static partial Regex LinkPattern();

        private async Task<string> SignUpAsync(HttpClient client)
        {
            var email = UniqueEmail();
            var response = await client.PostAsJsonAsync("/auth/signup", new { email, password = ValidPassword });
            response.EnsureSuccessStatusCode();
            return email;
        }

        // Reads userId and token from the link in the most recent email sent to this address.
        private (string UserId, string Token) ReadVerificationLink(string email)
        {
            var message = factory.Emails.SentTo(email).Last();
            var link = new Uri(LinkPattern().Match(message.TextBody).Value);
            var query = HttpUtility.ParseQueryString(link.Query);
            return (query["userId"]!, query["token"]!);
        }

        private async Task<bool> IsEmailConfirmedAsync(string email)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Users.Where(u => u.Email == email).Select(u => u.EmailConfirmed).SingleAsync();
        }

        [Fact]
        public async Task Sign_up_sends_a_verification_email_with_a_link_to_the_web_app()
        {
            var client = factory.CreateClient();

            var email = await SignUpAsync(client);

            var message = Assert.Single(factory.Emails.SentTo(email));
            Assert.Contains("http://localhost:3000/verify-email?userId=", message.TextBody);
        }

        [Fact]
        public async Task Valid_token_confirms_the_email()
        {
            var client = factory.CreateClient();
            var email = await SignUpAsync(client);
            var (userId, token) = ReadVerificationLink(email);

            var response = await client.PostAsJsonAsync("/auth/verify-email", new { userId, token });

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.True(await IsEmailConfirmedAsync(email));
        }

        [Fact]
        public async Task Tampered_token_is_rejected()
        {
            var client = factory.CreateClient();
            var email = await SignUpAsync(client);
            var (userId, token) = ReadVerificationLink(email);

            var response = await client.PostAsJsonAsync("/auth/verify-email", new { userId, token = token[..^4] + "AAAA" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(await IsEmailConfirmedAsync(email));
        }

        [Fact]
        public async Task Resend_sends_a_new_email_to_an_unverified_user()
        {
            var client = factory.CreateClient();
            var email = await SignUpAsync(client);

            var response = await client.PostAsJsonAsync("/auth/resend-verification", new { email });

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal(2, factory.Emails.SentTo(email).Count);
        }

        [Fact]
        public async Task Resend_for_an_unknown_email_looks_the_same_but_sends_nothing()
        {
            var client = factory.CreateClient();
            var email = UniqueEmail();

            var response = await client.PostAsJsonAsync("/auth/resend-verification", new { email });

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Empty(factory.Emails.SentTo(email));
        }
    }
}