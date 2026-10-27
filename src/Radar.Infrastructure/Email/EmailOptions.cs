namespace Radar.Infrastructure.Email
{
    public sealed class EmailOptions
    {
        public const string SectionName = "Email";

        /// <summary>Sender shown to the recipient, e.g. "API Change Radar &lt;no-reply@example.com&gt;".</summary>
        public string From { get; set; } = "API Change Radar <no-reply@apichangeradar.local>";

        public SmtpOptions Smtp { get; set; } = new();
    }

    public sealed class SmtpOptions
    {
        /// <summary>Leave empty to disable sending (emails are only logged).</summary>
        public string? Host { get; set; }

        public int Port { get; set; } = 1025;

        public string? UserName { get; set; }

        public string? Password { get; set; }
    }
}