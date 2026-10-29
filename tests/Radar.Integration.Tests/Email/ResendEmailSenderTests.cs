using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Radar.Application.Abstractions;
using Radar.Infrastructure.Email;

namespace Radar.Integration.Tests.Email
{
    public class ResendEmailSenderTests
    {
        // Captures the outgoing request instead of calling Resend for real.
        private sealed class RecordingHandler(HttpStatusCode status, string body = "{\"id\":\"test\"}") : HttpMessageHandler
        {
            public HttpRequestMessage? Request { get; private set; }

            public string? RequestBody { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Request = request;
                RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(status) { Content = new StringContent(body) };
            }
        }

        private static ResendEmailSender CreateSender(RecordingHandler handler) =>
            new(
                new HttpClient(handler) { BaseAddress = new Uri("https://api.resend.com/") },
                Options.Create(new EmailOptions
                {
                    From = "API Change Radar <onboarding@resend.dev>",
                    Resend = new ResendOptions { ApiKey = "re_test_key" },
                }));

        private static readonly EmailMessage Message = new("someone@example.com", "Hello", "plain text", "<p>html</p>");

        [Fact]
        public async Task Sends_a_json_post_to_the_emails_endpoint_with_the_api_key()
        {
            var handler = new RecordingHandler(HttpStatusCode.OK);

            await CreateSender(handler).SendAsync(Message);

            Assert.Equal(HttpMethod.Post, handler.Request!.Method);
            Assert.Equal("https://api.resend.com/emails", handler.Request.RequestUri!.ToString());
            Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
            Assert.Equal("re_test_key", handler.Request.Headers.Authorization.Parameter);

            using var json = JsonDocument.Parse(handler.RequestBody!);
            Assert.Equal("API Change Radar <onboarding@resend.dev>", json.RootElement.GetProperty("from").GetString());
            Assert.Equal("someone@example.com", json.RootElement.GetProperty("to")[0].GetString());
            Assert.Equal("Hello", json.RootElement.GetProperty("subject").GetString());
            Assert.Equal("plain text", json.RootElement.GetProperty("text").GetString());
            Assert.Equal("<p>html</p>", json.RootElement.GetProperty("html").GetString());
        }

        [Fact]
        public async Task A_rejected_email_throws_with_resends_explanation()
        {
            var handler = new RecordingHandler(HttpStatusCode.Forbidden, "{\"message\":\"You can only send testing emails to your own email address\"}");

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateSender(handler).SendAsync(Message));

            Assert.Contains("403", error.Message);
            Assert.Contains("only send testing emails", error.Message);
        }
    }
}
