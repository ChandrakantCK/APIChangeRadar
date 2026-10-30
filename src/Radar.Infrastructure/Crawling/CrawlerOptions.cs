namespace Radar.Infrastructure.Crawling
{
    public sealed class CrawlerOptions
    {
        public const string SectionName = "Crawler";

        /// <summary>Runs the background crawler in this process. Off by default; on in Development and (by env var) production.</summary>
        public bool Enabled { get; set; }

        /// <summary>How often the background service looks for due sources.</summary>
        public int PollMinutes { get; set; } = 15;

        /// <summary>A source is crawled at most once per this many minutes.</summary>
        public int SourceIntervalMinutes { get; set; } = 60;

        public int RequestTimeoutSeconds { get; set; } = 60;

        /// <summary>Downloads larger than this are abandoned (GitHub's spec is ~14 MB today).</summary>
        public long MaxSpecBytes { get; set; } = 50L * 1024 * 1024;
    }
}
