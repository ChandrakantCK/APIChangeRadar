using Radar.Domain.Crawling;

namespace Radar.Application.Crawling
{
    public sealed record CrawlSummary(int Sources, int Changed, int Unchanged, int NotModified, int Failed);

    public interface ICrawler
    {
        /// <summary>
        /// Crawls every active source that at least one organization watches
        /// and that has not been tried within the configured interval. One at a time, to be polite.
        /// </summary>
        Task<CrawlSummary> CrawlDueSourcesAsync(CancellationToken cancellationToken = default);

        /// <summary>Downloads one source now and records a crawl run.</summary>
        Task<CrawlOutcome> CrawlSourceAsync(Guid sourceId, CancellationToken cancellationToken = default);
    }
}
