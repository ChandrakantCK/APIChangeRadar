namespace Radar.Domain.Snapshots
{
    /// <summary>
    /// One version of a spec as we stored it: canonical JSON (keys sorted, no whitespace), Brotli-compressed.
    /// A new snapshot is only taken when the canonical hash differs from the latest one,
    /// so consecutive snapshots of a source always differ in content.
    /// </summary>
    public sealed class SpecSnapshot
    {
        public const int SpecVersionMaxLength = 50;
        public const int ApiVersionMaxLength = 100;

        // Used by EF Core when loading rows from the database.
        private SpecSnapshot()
        {
        }

        public SpecSnapshot(
            Guid specSourceId, Guid crawlRunId, DateTimeOffset capturedAt, string hash,
            string specVersion, string? apiVersion, long canonicalLength, byte[] compressedContent)
        {
            if (hash.Length != 64)
            {
                throw new ArgumentException("Hash must be a SHA-256 hex string.", nameof(hash));
            }

            Id = Guid.CreateVersion7();
            SpecSourceId = specSourceId;
            CrawlRunId = crawlRunId;
            CapturedAt = capturedAt;
            Hash = hash;
            SpecVersion = Truncate(specVersion, SpecVersionMaxLength)!;
            ApiVersion = Truncate(apiVersion, ApiVersionMaxLength);
            CanonicalLength = canonicalLength;
            CompressedContent = compressedContent;
        }

        public Guid Id { get; private set; }

        public Guid SpecSourceId { get; private set; }

        /// <summary>The download this snapshot came from.</summary>
        public Guid CrawlRunId { get; private set; }

        public DateTimeOffset CapturedAt { get; private set; }

        /// <summary>SHA-256 of the canonical JSON (hex): same hash = same API description.</summary>
        public string Hash { get; private set; } = null!;

        /// <summary>For example "OpenAPI 3.0.0" or "Swagger 2.0".</summary>
        public string SpecVersion { get; private set; } = null!;

        /// <summary>The API's own version from info.version, for example "2024-06-20".</summary>
        public string? ApiVersion { get; private set; }

        public long CanonicalLength { get; private set; }

        /// <summary>Canonical JSON, Brotli-compressed (specs compress roughly 10:1).</summary>
        public byte[] CompressedContent { get; private set; } = null!;

        private static string? Truncate(string? value, int maxLength) =>
            value is null || value.Length <= maxLength ? value : value[..maxLength];
    }
}
