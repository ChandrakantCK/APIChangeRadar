namespace Radar.Application.Watches
{
    public sealed record WatchView(
        Guid Id,
        Guid SourceId,
        string ProviderName,
        string SourceName,
        string SpecUrl,
        DateTimeOffset CreatedAt,
        DateTimeOffset? LastCheckedAt,
        string? LastCheckOutcome);

    /// <summary>The current organization's watches plus how many its plan allows.</summary>
    public sealed record WatchList(int Used, int Limit, IReadOnlyList<WatchView> Items);

    public enum AddWatchStatus
    {
        Added,
        AlreadyWatching,
        LimitReached,
        SourceNotFound
    }

    public sealed record AddWatchResult(AddWatchStatus Status, WatchView? Watch, string? Error);

    public interface IWatchService
    {
        Task<WatchList> ListAsync(CancellationToken cancellationToken = default);

        /// <summary>Enforces the plan limit and records an audit entry.</summary>
        Task<AddWatchResult> AddAsync(Guid sourceId, Guid actorUserId, string? ipAddress, CancellationToken cancellationToken = default);

        /// <summary>False when there is no such watch in the current organization.</summary>
        Task<bool> RemoveAsync(Guid watchId, Guid actorUserId, string? ipAddress, CancellationToken cancellationToken = default);
    }
}
