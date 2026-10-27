using System.Collections.Concurrent;
using Radar.Application.Abstractions;

namespace Radar.Integration.Tests.Infrastructure
{
    /// <summary>
    /// Records emails instead of sending them, so tests can read the links inside.
    /// </summary>
    public sealed class FakeEmailSender : IEmailSender
    {
        private readonly ConcurrentQueue<EmailMessage> _sent = new();

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            _sent.Enqueue(message);
            return Task.CompletedTask;
        }

        public IReadOnlyList<EmailMessage> SentTo(string email) =>
            _sent.Where(m => string.Equals(m.To, email, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}