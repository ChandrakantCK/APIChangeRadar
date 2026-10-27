using Microsoft.Extensions.Logging;
using Radar.Application.Abstractions;

namespace Radar.Infrastructure.Email
{
    /// <summary>
    /// Used when no SMTP host is configured (e.g. production before an email provider is set up).
    /// Logs that an email was skipped - never its body, which can contain a secret link.
    /// </summary>
    internal sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            logger.LogWarning("Email '{Subject}' was not sent: no SMTP host is configured.", message.Subject);
            return Task.CompletedTask;
        }
    }
}