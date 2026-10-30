namespace Radar.Domain.Crawling
{
    public enum CrawlOutcome
    {
        /// <summary>New content (or the first download).</summary>
        Changed,

        /// <summary>Downloaded again, but the bytes hash the same as last time.</summary>
        Unchanged,

        /// <summary>The server answered 304: our copy is current, nothing downloaded.</summary>
        NotModified,

        /// <summary>Error status, timeout, too large or network failure.</summary>
        Failed
    }
}
