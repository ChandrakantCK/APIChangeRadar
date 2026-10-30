using System.Buffers;
using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Radar.Infrastructure.Crawling
{
    internal enum FetchKind
    {
        Downloaded,
        NotModified,
        Failed
    }

    internal sealed record FetchResult(
        FetchKind Kind,
        int? HttpStatus,
        string? ETag = null,
        DateTimeOffset? LastModified = null,
        long ContentLength = 0,
        string? ContentHash = null,
        string? Error = null);

    /// <summary>
    /// Downloads a spec politely: identifies itself, asks "has it changed since this version?",
    /// gives up after a timeout, and never reads more than the size cap.
    /// </summary>
    internal sealed class SpecFetcher(IHttpClientFactory httpClientFactory, IOptions<CrawlerOptions> options)
    {
        public const string HttpClientName = "spec-fetcher";

        public async Task<FetchResult> FetchAsync(
            string url, string? etag, DateTimeOffset? lastModified, CancellationToken cancellationToken)
        {
            var settings = options.Value;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            // Conditional request: if our copy is current the server answers 304 with no body.
            if (etag is not null)
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            }

            if (lastModified is not null)
            {
                request.Headers.IfModifiedSince = lastModified;
            }

            try
            {
                var http = httpClientFactory.CreateClient(HttpClientName);

                // ResponseHeadersRead: look at the headers before the body arrives, so a huge file can be refused early.
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                var status = (int)response.StatusCode;

                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    return new FetchResult(FetchKind.NotModified, status);
                }

                if (!response.IsSuccessStatusCode)
                {
                    return new FetchResult(FetchKind.Failed, status, Error: $"HTTP {status} {response.ReasonPhrase}");
                }

                if (response.Content.Headers.ContentLength > settings.MaxSpecBytes)
                {
                    return TooLarge(status, settings.MaxSpecBytes);
                }

                // Hash while streaming: memory stays at one small buffer however big the spec is.
                await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = ArrayPool<byte>.Shared.Rent(81_920);
                long total = 0;
                try
                {
                    int read;
                    while ((read = await body.ReadAsync(buffer, timeout.Token)) > 0)
                    {
                        total += read;
                        if (total > settings.MaxSpecBytes)
                        {
                            // No (or a wrong) Content-Length header: the cap still holds.
                            return TooLarge(status, settings.MaxSpecBytes);
                        }

                        sha256.AppendData(buffer, 0, read);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }

                return new FetchResult(
                    FetchKind.Downloaded,
                    status,
                    response.Headers.ETag?.ToString(),
                    response.Content.Headers.LastModified,
                    total,
                    Convert.ToHexStringLower(sha256.GetHashAndReset()));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new FetchResult(FetchKind.Failed, null, Error: $"Timed out after {settings.RequestTimeoutSeconds} s.");
            }
            catch (HttpRequestException ex)
            {
                return new FetchResult(FetchKind.Failed, (int?)ex.StatusCode, Error: ex.Message);
            }
        }

        private static FetchResult TooLarge(int status, long maxBytes) =>
            new(FetchKind.Failed, status, Error: $"Spec is larger than the limit of {maxBytes:N0} bytes.");
    }
}
