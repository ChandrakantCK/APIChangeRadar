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
using Radar.Domain.Snapshots;
using Radar.Domain.Organizations;
using Radar.Domain.Watches;
using Radar.Infrastructure.Crawling;
using Radar.Infrastructure.Persistence;
using Radar.Infrastructure.Snapshots;
using Radar.Integration.Tests.Infrastructure;

namespace Radar.Integration.Tests.Crawling
{
    [Collection(ApiCollection.Name)]
    public class CrawlerTests
    {
        private const int MaxSpecBytes = 1_000;
        private const string MinimalSpec = """{"openapi":"3.1.0","info":{"title":"Test","version":"1"},"paths":{}}""";

        private readonly StubSpecServer _server = new();
        private readonly WebApplicationFactory<Program> _app;

        public CrawlerTests(RadarApiFactory factory)
        {
            // Same database as the other tests; the crawler's HTTP calls go to the stub, with small limits.
            _app = factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Crawler:RequestTimeoutSeconds", "1");
                builder.UseSetting("Crawler:MaxSpecBytes", MaxSpecBytes.ToString());
                builder.UseSetting("Crawler:SnapshotsToKeep", "2");
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
                _server.Map(url, _ => Spec(MinimalSpec));
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
            _server.Map(url, _ => Spec(MinimalSpec));
            (await client.PostAsJsonAsync("/organizations/current/watches", new { sourceId })).EnsureSuccessStatusCode();

            Assert.Null((await client.GetFromJsonAsync<WatchList>("/organizations/current/watches"))!.Items.Single().LastCheckedAt);

            await CrawlAsync(sourceId);

            var watch = (await client.GetFromJsonAsync<WatchList>("/organizations/current/watches"))!.Items.Single();
            Assert.NotNull(watch.LastCheckedAt);
            Assert.Equal("Changed", watch.LastCheckOutcome);
            Assert.Equal("1", watch.LatestApiVersion);
        }

        private async Task<List<SpecSnapshot>> SnapshotsAsync(Guid sourceId)
        {
            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.SpecSnapshots.Where(s => s.SpecSourceId == sourceId).OrderBy(s => s.CapturedAt).ToListAsync();
        }

        private static string SpecWithPaths(string version, params string[] paths) =>
            "{\"openapi\":\"3.0.3\",\"info\":{\"title\":\"Test\",\"version\":\"" + version + "\"},\"paths\":{"
            + string.Join(",", paths.Select(p => "\"" + p + "\":{}")) + "}}";

        [Fact]
        public async Task The_first_download_is_stored_as_a_compressed_canonical_snapshot()
        {
            var (sourceId, url) = await CreateSourceAsync();
            _server.Map(url, _ => Spec("""
                {
                  "paths": {},
                  "info": { "version": "2024-06-20", "title": "Test" },
                  "openapi": "3.1.0"
                }
                """));

            await CrawlAsync(sourceId);

            var snapshot = Assert.Single(await SnapshotsAsync(sourceId));
            var canonical = """{"info":{"title":"Test","version":"2024-06-20"},"openapi":"3.1.0","paths":{}}""";
            Assert.Equal(canonical, Encoding.UTF8.GetString(SnapshotCompression.Decompress(snapshot.CompressedContent)));
            Assert.Equal(Sha256(canonical), snapshot.Hash);
            Assert.Equal("OpenAPI 3.1.0", snapshot.SpecVersion);
            Assert.Equal("2024-06-20", snapshot.ApiVersion);
            Assert.Equal((await RunsAsync(sourceId)).Single().Id, snapshot.CrawlRunId);
        }

        [Fact]
        public async Task A_formatting_only_change_takes_no_new_snapshot()
        {
            var (sourceId, url) = await CreateSourceAsync();
            var pretty = false;
            _server.Map(url, _ => Spec(pretty
                ? """{ "paths" : { }, "openapi" : "3.1.0", "info" : { "version" : "1", "title" : "Test" } }"""
                : MinimalSpec));

            await CrawlAsync(sourceId);
            pretty = true;

            Assert.Equal(CrawlOutcome.Changed, await CrawlAsync(sourceId)); // the bytes differ...
            Assert.Single(await SnapshotsAsync(sourceId));                  // ...the API description does not
        }

        [Fact]
        public async Task A_real_change_takes_a_new_snapshot_and_only_the_newest_are_kept()
        {
            var (sourceId, url) = await CreateSourceAsync();
            var version = 1;
            _server.Map(url, _ => Spec(SpecWithPaths(version.ToString(), Enumerable.Range(1, version).Select(i => $"/v{i}").ToArray())));

            for (; version <= 3; version++)
            {
                await CrawlAsync(sourceId);
            }

            // SnapshotsToKeep is 2 in these tests: version 1 was deleted.
            Assert.Equal(["2", "3"], (await SnapshotsAsync(sourceId)).Select(s => s.ApiVersion));
        }

        [Theory]
        [InlineData("<html><body>Not found</body></html>")]
        [InlineData("""{"title":"just some JSON"}""")]
        public async Task Documents_that_are_not_openapi_fail_and_take_no_snapshot(string body)
        {
            var (sourceId, url) = await CreateSourceAsync();
            _server.Map(url, _ => Spec(body));

            Assert.Equal(CrawlOutcome.Failed, await CrawlAsync(sourceId));
            Assert.StartsWith("Not a usable OpenAPI document", (await RunsAsync(sourceId)).Single().Error);
            Assert.Empty(await SnapshotsAsync(sourceId));
        }

        [Fact]
        public async Task Without_a_snapshot_the_crawler_downloads_in_full_even_if_it_has_an_etag()
        {
            // Like production right after this feature shipped: earlier crawls stored an ETag but no snapshot.
            var (sourceId, url) = await CreateSourceAsync();
            using (var scope = _app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.CrawlRuns.Add(CrawlRun.Downloaded(
                    sourceId, DateTimeOffset.UtcNow.AddHours(-2), TimeSpan.FromSeconds(1), changed: true,
                    200, "\"v1\"", null, MinimalSpec.Length, Sha256(MinimalSpec)));
                await db.SaveChangesAsync();
            }

            _server.Map(url, request => request.Headers.IfNoneMatch.Count > 0
                ? new HttpResponseMessage(HttpStatusCode.NotModified)
                : Spec(MinimalSpec, "\"v1\""));

            Assert.Equal(CrawlOutcome.Unchanged, await CrawlAsync(sourceId));
            Assert.Null(_server.RequestsTo(url).Single().IfNoneMatch);
            Assert.Single(await SnapshotsAsync(sourceId));
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
