namespace Radar.Domain.Crawling
{
    /// <summary>
    /// One attempt to download a spec source: what we asked, what came back, and whether the content changed.
    /// Only ever added; the latest successful run also tells the next run which ETag to send.
    /// </summary>
    public sealed class CrawlRun
    {
        public const int ETagMaxLength = 200;
        public const int ErrorMaxLength = 500;

        // Used by EF Core when loading rows from the database.
        private CrawlRun()
        {
        }

        private CrawlRun(Guid specSourceId, DateTimeOffset startedAt, TimeSpan duration, CrawlOutcome outcome)
        {
            Id = Guid.CreateVersion7();
            SpecSourceId = specSourceId;
            StartedAt = startedAt;
            DurationMs = (int)Math.Clamp(duration.TotalMilliseconds, 0, int.MaxValue);
            Outcome = outcome;
        }

        public Guid Id { get; private set; }

        public Guid SpecSourceId { get; private set; }

        public DateTimeOffset StartedAt { get; private set; }

        public int DurationMs { get; private set; }

        public CrawlOutcome Outcome { get; private set; }

        public int? HttpStatus { get; private set; }

        /// <summary>The server's version tag for the content; sent back as If-None-Match next time.</summary>
        public string? ETag { get; private set; }

        public DateTimeOffset? LastModified { get; private set; }

        public long? ContentLength { get; private set; }

        /// <summary>SHA-256 of the downloaded bytes (hex). Same hash = same content.</summary>
        public string? ContentHash { get; private set; }

        public string? Error { get; private set; }

        public static CrawlRun Downloaded(
            Guid specSourceId, DateTimeOffset startedAt, TimeSpan duration, bool changed, int httpStatus,
            string? etag, DateTimeOffset? lastModified, long contentLength, string contentHash) =>
            new(specSourceId, startedAt, duration, changed ? CrawlOutcome.Changed : CrawlOutcome.Unchanged)
            {
                HttpStatus = httpStatus,
                ETag = Truncate(etag, ETagMaxLength),
                LastModified = lastModified,
                ContentLength = contentLength,
                ContentHash = contentHash,
            };

        /// <summary>304: nothing was downloaded, so the previous run's tag and hash still describe the content.</summary>
        public static CrawlRun NotModified(Guid specSourceId, DateTimeOffset startedAt, TimeSpan duration, CrawlRun previous) =>
            new(specSourceId, startedAt, duration, CrawlOutcome.NotModified)
            {
                HttpStatus = 304,
                ETag = previous.ETag,
                LastModified = previous.LastModified,
                ContentLength = previous.ContentLength,
                ContentHash = previous.ContentHash,
            };

        public static CrawlRun Failed(Guid specSourceId, DateTimeOffset startedAt, TimeSpan duration, int? httpStatus, string error) =>
            new(specSourceId, startedAt, duration, CrawlOutcome.Failed)
            {
                HttpStatus = httpStatus,
                Error = Truncate(error, ErrorMaxLength),
            };

        private static string? Truncate(string? value, int maxLength) =>
            value is null || value.Length <= maxLength ? value : value[..maxLength];
    }
}
