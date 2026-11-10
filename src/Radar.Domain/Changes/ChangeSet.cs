namespace Radar.Domain.Changes
{
    /// <summary>
    /// Everything that changed in one API between two of its snapshots: one "release" in the feed.
    /// Shared by every organization that watches the source, so it is not tenant-owned.
    /// </summary>
    public sealed class ChangeSet
    {
        /// <summary>A huge diff (a rewritten spec) is counted in full but only this many rows are kept.</summary>
        public const int MaxStoredChanges = 5_000;

        private readonly List<ApiChange> _changes = [];

        // Used by EF Core when loading rows from the database.
        private ChangeSet()
        {
        }

        public ChangeSet(
            Guid specSourceId, Guid? fromSnapshotId, Guid toSnapshotId, DateTimeOffset detectedAt,
            string? fromApiVersion, string? toApiVersion, IReadOnlyCollection<ApiChange> changes)
        {
            if (changes.Count == 0)
            {
                throw new ArgumentException("A change set needs at least one change.", nameof(changes));
            }

            Id = Guid.CreateVersion7();
            SpecSourceId = specSourceId;
            FromSnapshotId = fromSnapshotId;
            ToSnapshotId = toSnapshotId;
            DetectedAt = detectedAt;
            FromApiVersion = fromApiVersion;
            ToApiVersion = toApiVersion;
            BreakingCount = changes.Count(c => c.Severity == ChangeSeverity.Breaking);
            WarningCount = changes.Count(c => c.Severity == ChangeSeverity.Warning);
            InfoCount = changes.Count(c => c.Severity == ChangeSeverity.Info);

            var position = 0;
            foreach (var change in changes.Take(MaxStoredChanges))
            {
                change.AttachTo(Id, position++);
                _changes.Add(change);
            }
        }

        public Guid Id { get; private set; }

        public Guid SpecSourceId { get; private set; }

        /// <summary>The older snapshot; null once retention has deleted it.</summary>
        public Guid? FromSnapshotId { get; private set; }

        /// <summary>The newer snapshot; null once retention has deleted it.</summary>
        public Guid? ToSnapshotId { get; private set; }

        public DateTimeOffset DetectedAt { get; private set; }

        public string? FromApiVersion { get; private set; }

        public string? ToApiVersion { get; private set; }

        public int BreakingCount { get; private set; }

        public int WarningCount { get; private set; }

        public int InfoCount { get; private set; }

        public IReadOnlyList<ApiChange> Changes => _changes;
    }
}
