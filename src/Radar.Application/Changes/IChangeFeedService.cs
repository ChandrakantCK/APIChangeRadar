namespace Radar.Application.Changes
{
    /// <summary>One release of a watched API: when we saw it and how many changes of each severity it has.</summary>
    public sealed record ChangeSetView(
        Guid Id,
        Guid SourceId,
        string ProviderName,
        string SourceName,
        DateTimeOffset DetectedAt,
        string? FromApiVersion,
        string? ToApiVersion,
        int BreakingCount,
        int WarningCount,
        int InfoCount);

    /// <param name="NextBefore">Pass as "before" to get the next page; null on the last page.</param>
    public sealed record ChangeFeed(IReadOnlyList<ChangeSetView> Items, DateTimeOffset? NextBefore);

    /// <param name="Severity">"Breaking", "Warning" or "Info".</param>
    public sealed record ChangeView(string RuleId, string Severity, string Method, string Path, string Location, string Message);

    /// <param name="Changes">Most severe first; at most ChangeSet.MaxStoredChanges, while the counts cover all of them.</param>
    public sealed record ChangeSetDetail(ChangeSetView Summary, IReadOnlyList<ChangeView> Changes);

    /// <summary>Which releases to show: all of them, or only those with something at least this severe.</summary>
    public enum FeedSeverity
    {
        All,
        Warning,
        Breaking
    }

    public interface IChangeFeedService
    {
        public const int DefaultPageSize = 20;
        public const int MaxPageSize = 50;

        /// <summary>Releases of the APIs the current organization watches, newest first.</summary>
        Task<ChangeFeed> ListAsync(FeedSeverity severity, DateTimeOffset? before, int pageSize, CancellationToken cancellationToken = default);

        /// <summary>Null when there is no such release or the current organization doesn't watch its API.</summary>
        Task<ChangeSetDetail?> GetAsync(Guid changeSetId, CancellationToken cancellationToken = default);
    }
}
