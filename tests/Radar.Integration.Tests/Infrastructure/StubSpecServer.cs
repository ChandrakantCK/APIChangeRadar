using System.Collections.Concurrent;
using System.Net;

namespace Radar.Integration.Tests.Infrastructure
{
    /// <summary>
    /// Stands in for the internet: the crawler's HttpClient sends its requests here instead.
    /// Each test maps its own URLs; anything unmapped is a 404.
    /// </summary>
    public sealed class StubSpecServer : HttpMessageHandler
    {
        public sealed record SeenRequest(string Url, string? IfNoneMatch, string? UserAgent);

        private readonly ConcurrentDictionary<string, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _routes = new();

        public ConcurrentQueue<SeenRequest> Requests { get; } = new();

        public void Map(string url, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) =>
            _routes[url] = respond;

        public void Map(string url, Func<HttpRequestMessage, HttpResponseMessage> respond) =>
            Map(url, (request, _) => Task.FromResult(respond(request)));

        public IReadOnlyList<SeenRequest> RequestsTo(string url) => Requests.Where(r => r.Url == url).ToList();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requests.Enqueue(new SeenRequest(
                url,
                request.Headers.TryGetValues("If-None-Match", out var etags) ? etags.Single() : null,
                request.Headers.UserAgent.ToString()));

            return _routes.TryGetValue(url, out var respond)
                ? respond(request, cancellationToken)
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        // IHttpClientFactory disposes handlers when it recycles them; this one is shared by the whole test run.
        protected override void Dispose(bool disposing)
        {
        }
    }
}
