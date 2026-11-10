namespace Radar.Domain.Changes
{
    /// <summary>One difference found by the differ, for example "Field `email` was removed from the 200 response."</summary>
    public sealed class ApiChange
    {
        public const int RuleIdMaxLength = 60;
        public const int MethodMaxLength = 10;
        public const int PathMaxLength = 500;
        public const int LocationMaxLength = 500;
        public const int MessageMaxLength = 2_000;

        // Used by EF Core when loading rows from the database.
        private ApiChange()
        {
        }

        public ApiChange(string ruleId, ChangeSeverity severity, string method, string path, string location, string message)
        {
            Id = Guid.CreateVersion7();
            RuleId = Truncate(ruleId, RuleIdMaxLength);
            Severity = severity;
            Method = Truncate(method, MethodMaxLength);
            Path = Truncate(path, PathMaxLength);
            Location = Truncate(location, LocationMaxLength);
            Message = Truncate(message, MessageMaxLength);
        }

        public Guid Id { get; private set; }

        public Guid ChangeSetId { get; private set; }

        /// <summary>Order within the set, as the differ sorted them (path, method, severity).</summary>
        public int Position { get; private set; }

        public string RuleId { get; private set; } = null!;

        public ChangeSeverity Severity { get; private set; }

        public string Method { get; private set; } = null!;

        public string Path { get; private set; } = null!;

        public string Location { get; private set; } = null!;

        public string Message { get; private set; } = null!;

        internal void AttachTo(Guid changeSetId, int position)
        {
            ChangeSetId = changeSetId;
            Position = position;
        }

        // Specs are third-party input: a very long path or enum list must not fail the insert.
        private static string Truncate(string value, int maxLength) =>
            value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
    }
}
