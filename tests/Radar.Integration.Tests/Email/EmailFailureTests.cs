using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Radar.Application.Abstractions;
using Radar.Integration.Tests.Infrastructure;

namespace Radar.Integration.Tests.Email
{
    [Collection(ApiCollection.Name)]
    public class EmailFailureTests(RadarApiFactory factory)
    {
        // Simulates a mail provider that is down or blocked.
        private sealed class FailingEmailSender : IEmailSender
        {
            public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) =>
                throw new InvalidOperationException("Mail server unreachable.");
        }

        private HttpClient CreateClientWithBrokenEmail() =>
            factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IEmailSender>();
                    services.AddSingleton<IEmailSender, FailingEmailSender>();
                }))
                .CreateClient();

        [Fact]
        public async Task Sign_up_still_succeeds_when_email_fails()
        {
            var client = CreateClientWithBrokenEmail();

            var response = await client.PostAsJsonAsync("/auth/signup",
                new { email = $"user-{Guid.NewGuid():N}@example.com", password = "correct horse battery staple" });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        [Fact]
        public async Task Resend_still_answers_202_when_email_fails()
        {
            var client = CreateClientWithBrokenEmail();
            var email = $"user-{Guid.NewGuid():N}@example.com";
            (await client.PostAsJsonAsync("/auth/signup", new { email, password = "correct horse battery staple" })).EnsureSuccessStatusCode();

            var response = await client.PostAsJsonAsync("/auth/resend-verification", new { email });

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }
    }
}
