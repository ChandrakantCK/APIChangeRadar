using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radar.Application.Crawling;
using Radar.Application.Watches;
using Radar.Domain.Catalog;
using Radar.Domain.Crawling;
using Radar.Domain.Organizations;
using Radar.Domain.Watches;
using Radar.Infrastructure.Crawling;
using Radar.Infrastructure.Persistence;
using Radar.Integration.Tests.Infrastructure;

namespace Radar.Integration.Tests.Crawling
{
    [Collection(ApiCollection.Name)]
    public class CrawlerTests
    {
        private const int MaxSpecBytes = 1_000;

        private readonly StubSpecServer _server = new();
        private readonly WebApplicationFactory<Program> _app;

        public CrawlerTests(RadarApiFactory factory)
        {
            // Same database as the other tests; the crawler's HTTP calls go to the stub, with small limits.
            _app = factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Crawler:RequestTimeoutSeconds", "1");
                builder.UseSetting("Crawler:MaxSpecBytes", MaxSpecBytes.ToString());
                builder.ConfigureTestServices(services =>
                    services.AddHttpClient(SpecFetcher.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _server));
            });
        }

        private async Task<(Guid SourceId, string Url)> CreateSourceAsync(bool watched = false)
        {
            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var provider = new ApiProvider($"crawl-{Guid.NewGuid():N}", "Crawl test", "https://example.com");
            var url = $"https://specs.example.com/{Guid.NewGuid():N}/openapi.json";
            var source = new SpecSource(provider.Id, "api", "Crawl test API", url);
            db.ApiProviders.Add(provider);
            db.SpecSources.Add(source);

            if (watched)
            {
                var organization = new Organization("Crawl watchers");
                db.Organizations.Add(organization);
                db.Watches.Add(new Watch(organization.Id, source.Id, Guid.NewGuid()));
            }

            await db.SaveChangesAsync();
            return (source.Id, url);
        }

        private async Task<CrawlOutcome> CrawlAsync(Guid sourceId)
        {
            using var scope = _app.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ICrawler>().CrawlSourceAsync(sourceId);
        }

        private async Task<List<CrawlRun>> RunsAsync(Guid sourceId)
        {
            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.CrawlRuns.Where(r => r.SpecSourceId == sourceId).OrderBy(r => r.StartedAt).ToListAsync();
        }

        private static HttpResponseMessage Spec(string body, string? etag = null)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (etag is not null)
            {
                response.Headers.ETag = EntityTagHeaderValue.Parse(etag);
            }

            return response;
        }

        private static string Sha256(string body) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body)));

        [Fact]
        public async Task The_first_download_is_a_change_with_hash_etag_and_a_polite_user_agent()
        {
            var (sourceId, url) = await CreateSourceAsync();
            const string body = """{"openapi":"3.1.0"}""";
            _server.Map(url, _ => Spec(body, "\"v1\""));

            Assert.Equal(CrawlOutcome.Changed, await CrawlAsync(sourceId));

            var run = Assert.Single(await RunsAsync(sourceId));
            Assert.Equal(200, run.HttpStatus);
            Assert.Equal("\"v1\"", run.ETag);
            Assert.Equal(body.Length, run.ContentLength);
            Assert.Equal(Sha256(body), run.ContentHash);
            Assert.StartsWith("APIChangeRadar/1.0", _server.RequestsTo(url).Single().UserAgent);
        }

        [Fact]
        public async Task The_next_crawl_is_conditional_and_a_304_downloads_nothing()
        {
            var (sourceId, url) = await CreateSourceAsync();
            _server.Map(url, request => request.Headers.IfNoneMatch.ToString() == "\"v1\""
                ? new HttpResponseMessage(HttpStatusCode.NotModified)
                : Spec("""{"openapi":"3.1.0"}""", "\"v1\""));

            await CrawlAsync(sourceId);
            Assert.Equal(CrawlOutcome.NotModified, await CrawlAsync(sourceId));

            Assert.Equal([null, "\"v1\""], _server.RequestsTo(url).Select(r => r.IfNoneMatch));
            var runs = await RunsAsync(sourceId);
            Assert.Equal(runs[0].ContentHash, runs[1].ContentHash); // the 304 run still knows which content is current
        }

        [Fact]
        public async Task Same_bytes_from_a_server_without_etags_are_unchanged()
        {
            var (sourceId, url) = await CreateSourceAsync();
            _server.Map(url, _ => Spec("""{"openapi":"3.1.0"}"""));

            Assert.Equal(CrawlOutcome.Changed, await CrawlAsync(sourceId));
            Assert.Equal(CrawlOutcome.Unchanged, await CrawlAsync(sourceId));
        }

        [Fact]
        public async Task New_bytes_are_a_change()
        {
            var (sourceId, url) = await CreateSourceAsync();
            var version = 1;
            _server.Map(url, _ => Spec("{\"openapi\":\"3.1.0\",\"info\":{\"version\":\"" + version + "\"}}"));

            await CrawlAsync(sourceId);
            version = 2;
            Assert.Equal(CrawlOutcome.Changed, await CrawlAsync(sourceId));

            var runs = await RunsAsync(sourceId);
            Assert.NotEqual(runs[0].ContentHash, runs[1].ContentHash);
        }

        [Fact]
        public async Task A_failure_is_recorded_and_the_last_good_version_is_still_used_next_time()
        {
            var (sourceId, url) = await CreateSourceAsync();
            var failing = false;
            _server.Map(url, request =>
                failing ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : request.Headers.IfNoneMatch.Count > 0 ? new HttpResponseMessage(HttpStatusCode.NotModified)
                : Spec("""{"openapi":"3.1.0"}""", "\"v1\""));

            await CrawlAsync(sourceId);
            failing = true;
            Assert.Equal(CrawlOutcome.Failed, await CrawlAsync(sourceId));
            failing = false;
            Assert.Equal(CrawlOutcome.NotModified, await CrawlAsync(sourceId));

            var runs = await RunsAsync(sourceId);
            Assert.Equal(500, runs[1].HttpStatus);
            Assert.Contains("500", runs[1].Error);
            Assert.Equal("\"v1\"", _server.RequestsTo(url).Last().IfNoneMatch);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Specs_over_the_size_limit_are_refused(bool announcesLength)
        {
            var (sourceId, url) = await CreateSourceAsync();
            var bytes = new byte[MaxSpecBytes + 1];
            _server.Map(url, _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = announcesLength ? new ByteArrayContent(bytes) : new UnknownLengthContent(bytes),
            });

            Assert.Equal(CrawlOutcome.Failed, await CrawlAsync(sourceId));
            Assert.Contains("larger than the limit", (await RunsAsync(sourceId)).Single().Error);
        }

        [Fact]
        public async Task A_slow_server_times_out()
        {
            var (sourceId, url) = await CreateSourceAsync();
            _server.Map(url, async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                return Spec("{}");
            });

            Assert.Equal(CrawlOutcome.Failed, await CrawlAsync(sourceId));
            Assert.Equal("Timed out after 1 s.", (await RunsAsync(sourceId)).Single().Error);
        }

        [Fact]
        public async Task Only_watched_sources_that_are_due_get_crawled()
        {
            var due = await CreateSourceAsync(watched: true);
            var unwatched = await CreateSourceAsync(watched: false);
            var recent = await CreateSourceAsync(watched: true);
            foreach (var url in new[] { due.Url, unwatched.Url, recent.Url })
            {
                _server.Map(url, _ => Spec("{}"));
            }

            await CrawlAsync(recent.SourceId); // tried moments ago: not due for another hour

            using (var scope = _app.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<ICrawler>().CrawlDueSourcesAsync();
            }

            Assert.Single(_server.RequestsTo(due.Url));
            Assert.Empty(_server.RequestsTo(unwatched.Url));
            Assert.Single(_server.RequestsTo(recent.Url));
        }

        [Fact]
        public async Task The_watch_list_shows_when_a_source_was_last_checked()
        {
            var client = await TestAccounts.CreateSignedInClientAsync(_app);
            var (sourceId, url) = await CreateSourceAsync();
            _server.Map(url, _ => Spec("{}"));
            (await client.PostAsJsonAsync("/organizations/current/watches", new { sourceId })).EnsureSuccessStatusCode();

            Assert.Null((await client.GetFromJsonAsync<WatchList>("/organizations/current/watches"))!.Items.Single().LastCheckedAt);

            await CrawlAsync(sourceId);

            var watch = (await client.GetFromJsonAsync<WatchList>("/organizations/current/watches"))!.Items.Single();
            Assert.NotNull(watch.LastCheckedAt);
            Assert.Equal("Changed", watch.LastCheckOutcome);
        }

        // Streams its bytes without a Content-Length header, like a chunked response.
        private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
        {
            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();

            protected override bool TryComputeLength(out long length)
            {
                length = 0;
                return false;
            }
        }
    }
}
