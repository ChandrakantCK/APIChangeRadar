namespace Radar.Application.Abstractions
{
    public sealed record EmailMessage(string To, string Subject, string TextBody, string HtmlBody);

    /// <summary>
    /// Sends an email. Mailpit (SMTP) locally; a real provider in production; an in-memory fake in tests.
    /// </summary>
    public interface IEmailSender
    {
        Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
    }
}