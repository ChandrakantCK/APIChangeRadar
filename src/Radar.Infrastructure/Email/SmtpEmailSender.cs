using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Radar.Application.Abstractions;

namespace Radar.Infrastructure.Email
{
    internal sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
    {
        public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            var settings = options.Value;
            var host = settings.Smtp.Host
                ?? throw new InvalidOperationException("Email:Smtp:Host is not configured.");

            var mime = new MimeMessage();
            mime.From.Add(MailboxAddress.Parse(settings.From));
            mime.To.Add(MailboxAddress.Parse(message.To));
            mime.Subject = message.Subject;
            mime.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();

            using var client = new SmtpClient();

            // Auto = use TLS if the server offers it (real providers), plain if not (Mailpit).
            await client.ConnectAsync(host, settings.Smtp.Port, SecureSocketOptions.Auto, cancellationToken);

            if (!string.IsNullOrEmpty(settings.Smtp.UserName))
            {
                await client.AuthenticateAsync(settings.Smtp.UserName, settings.Smtp.Password ?? string.Empty, cancellationToken);
            }

            await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
    }
}