using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Radar.Application.Abstractions;

namespace Radar.Infrastructure.Email
{
    /// <summary>
    /// Sends email through Resend's HTTP API (https://resend.com/docs/api-reference/emails/send-email).
    /// HTTPS on port 443 works everywhere, unlike SMTP, which many hosts block.
    /// </summary>
    internal sealed class ResendEmailSender(HttpClient http, IOptions<EmailOptions> options) : IEmailSender
    {
        public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            var settings = options.Value;

            using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
            {
                Content = JsonContent.Create(new
                {
                    from = settings.From,
                    to = new[] { message.To },
                    subject = message.Subject,
                    text = message.TextBody,
                    html = message.HtmlBody,
                }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Resend.ApiKey);

            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // Resend explains what's wrong (bad key, unverified sender...) in the body; the key itself isn't in it.
                var details = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Resend rejected the email ({(int)response.StatusCode}): {details}");
            }
        }
    }
}
