using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radar.Application.Changes;
using Radar.Application.Crawling;
using Radar.Domain.Catalog;
using Radar.Domain.Changes;
using Radar.Infrastructure.Crawling;
using Radar.Infrastructure.Persistence;
using Radar.Integration.Tests.Infrastructure;

namespace Radar.Integration.Tests.Changes
{
    [Collection(ApiCollection.Name)]
    public class ChangeFeedTests
    {
        private const string ChangesPath = "/organizations/current/changes";

        private readonly StubSpecServer _server = new();
        private readonly WebApplicationFactory<Program> _app;

        public ChangeFeedTests(RadarApiFactory factory)
        {
            _app = factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Crawler:SnapshotsToKeep", "2");
                builder.ConfigureTestServices(services =>
                    services.AddHttpClient(SpecFetcher.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _server));
            });
        }

        // A pets API; each version passes the fields of its Pet schema and any extra paths.
        private static string Spec(string version, string petProperties, string extraPaths = "", string description = "A pet") =>
            "{\"openapi\":\"3.0.3\",\"info\":{\"title\":\"Pets\",\"version\":\"" + version + "\"},\"paths\":{"
            + "\"/pets\":{\"get\":{\"description\":\"" + description + "\",\"responses\":{\"200\":{\"description\":\"OK\",\"content\":{\"application/json\":{\"schema\":"
            + "{\"type\":\"object\",\"properties\":{" + petProperties + "}}}}}}}}"
            + extraPaths + "}}";

        private const string IdAndName = "\"id\":{\"type\":\"string\"},\"name\":{\"type\":\"string\"}";
        private const string IdOnly = "\"id\":{\"type\":\"string\"}";
        private const string DeletePet = ",\"/pets/{id}\":{\"delete\":{\"responses\":{\"204\":{\"description\":\"Deleted\"}}}}";

        private async Task<(Guid SourceId, string Url)> CreateSourceAsync()
        {
            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var provider = new ApiProvider($"feed-{Guid.NewGuid():N}", "Petstore", "https://example.com");
            var url = $"https://specs.example.com/{Guid.NewGuid():N}/openapi.json";
            var source = new SpecSource(provider.Id, "pets", "Pets API", url);
            db.ApiProviders.Add(provider);
            db.SpecSources.Add(source);
            await db.SaveChangesAsync();
            return (source.Id, url);
        }

        private async Task<HttpClient> WatcherAsync(Guid sourceId)
        {
            var client = await TestAccounts.CreateSignedInClientAsync(_app, "Feed readers");
            (await client.PostAsJsonAsync("/organizations/current/watches", new { sourceId })).EnsureSuccessStatusCode();
            return client;
        }

        // Each call serves the next version; the crawler sees new bytes every time.
        private async Task CrawlVersionsAsync(Guid sourceId, string url, params string[] versions)
        {
            foreach (var version in versions)
            {
                _server.Map(url, _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(version, Encoding.UTF8, "application/json"),
                });

                using var scope = _app.Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ICrawler>().CrawlSourceAsync(sourceId);
            }
        }

        private async Task<List<ChangeSet>> ChangeSetsAsync(Guid sourceId)
        {
            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.ChangeSets.Where(c => c.SpecSourceId == sourceId).OrderBy(c => c.DetectedAt).ToListAsync();
        }

        [Fact]
        public async Task A_new_version_shows_up_in_the_feed_with_its_changes_most_severe_first()
        {
            var (sourceId, url) = await CreateSourceAsync();
            var client = await WatcherAsync(sourceId);

            await CrawlVersionsAsync(sourceId, url, Spec("1.0", IdAndName), Spec("1.1", IdOnly, DeletePet));

            var feed = (await client.GetFromJsonAsync<ChangeFeed>(ChangesPath))!;
            var release = Assert.Single(feed.Items, i => i.SourceId == sourceId);
            Assert.Equal(("Petstore", "Pets API", "1.0", "1.1"), (release.ProviderName, release.SourceName, release.FromApiVersion, release.ToApiVersion));
            Assert.Equal((1, 0, 1), (release.BreakingCount, release.WarningCount, release.InfoCount));

            var detail = (await client.GetFromJsonAsync<ChangeSetDetail>($"{ChangesPath}/{release.Id}"))!;
            Assert.Equal(
                [
                    ("response-field-removed", "Breaking", "GET", "/pets", "response 200 `name`"),
                    ("endpoint-added", "Info", "DELETE", "/pets/{id}", ""),
                ],
                detail.Changes.Select(c => (c.RuleId, c.Severity, c.Method, c.Path, c.Location)));
        }

        [Fact]
        public async Task The_first_snapshot_and_documentation_only_edits_create_no_release()
        {
            var (sourceId, url) = await CreateSourceAsync();

            await CrawlVersionsAsync(sourceId, url, Spec("1.0", IdAndName), Spec("1.0", IdAndName, description: "A good pet"));

            Assert.Empty(await ChangeSetsAsync(sourceId));
        }

        [Fact]
        public async Task Organizations_only_see_releases_of_apis_they_watch()
        {
            var (sourceId, url) = await CreateSourceAsync();
            await WatcherAsync(sourceId);
            await CrawlVersionsAsync(sourceId, url, Spec("1.0", IdAndName), Spec("1.1", IdOnly));
            var changeSetId = Assert.Single(await ChangeSetsAsync(sourceId)).Id;

            var outsider = await TestAccounts.CreateSignedInClientAsync(_app, "Not watching");

            var feed = (await outsider.GetFromJsonAsync<ChangeFeed>(ChangesPath))!;
            Assert.DoesNotContain(feed.Items, i => i.Id == changeSetId);
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"{ChangesPath}/{changeSetId}")).StatusCode);
        }

        [Fact]
        public async Task The_feed_filters_by_severity_and_pages_newest_first()
        {
            var (sourceId, url) = await CreateSourceAsync();
            var client = await WatcherAsync(sourceId);

            // 1.0 â†’ 1.1 adds an endpoint (info), 1.1 â†’ 1.2 removes a field (breaking), 1.2 â†’ 1.3 adds it back (info).
            await CrawlVersionsAsync(sourceId, url,
                Spec("1.0", IdAndName), Spec("1.1", IdAndName, DeletePet), Spec("1.2", IdOnly, DeletePet), Spec("1.3", IdAndName, DeletePet));

            var all = (await client.GetFromJsonAsync<ChangeFeed>($"{ChangesPath}?limit=2"))!;
            Assert.Equal(["1.3", "1.2"], all.Items.Select(i => i.ToApiVersion));
            Assert.NotNull(all.NextBefore);

            var next = (await client.GetFromJsonAsync<ChangeFeed>($"{ChangesPath}?limit=2&before={Uri.EscapeDataString(all.NextBefore!.Value.ToString("O"))}"))!;
            Assert.Equal(["1.1"], next.Items.Select(i => i.ToApiVersion));
            Assert.Null(next.NextBefore);

            var breaking = (await client.GetFromJsonAsync<ChangeFeed>($"{ChangesPath}?severity=breaking"))!;
            Assert.Equal(["1.2"], breaking.Items.Select(i => i.ToApiVersion));
        }

        [Fact]
        public async Task Releases_outlive_the_snapshots_they_were_computed_from()
        {
            var (sourceId, url) = await CreateSourceAsync();

            // Only 2 snapshots are kept in these tests; 4 versions mean the oldest 2 are deleted.
            await CrawlVersionsAsync(sourceId, url,
                Spec("1.0", IdAndName), Spec("1.1", IdOnly), Spec("1.2", IdAndName), Spec("1.3", IdOnly));

            var sets = await ChangeSetsAsync(sourceId);
            Assert.Equal(3, sets.Count);
            Assert.Null(sets[0].FromSnapshotId);
            Assert.Null(sets[0].ToSnapshotId);
            Assert.NotNull(sets[2].FromSnapshotId);
            Assert.NotNull(sets[2].ToSnapshotId);
        }

        [Theory]
        [InlineData("?severity=urgent")]
        [InlineData("?limit=0")]
        [InlineData("?limit=51")]
        public async Task Bad_feed_parameters_are_rejected(string query)
        {
            var client = await TestAccounts.CreateSignedInClientAsync(_app, "Feed readers");
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(ChangesPath + query)).StatusCode);
        }

        [Fact]
        public async Task The_feed_needs_a_signed_in_user()
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await _app.CreateClient().GetAsync(ChangesPath)).StatusCode);
        }
    }
}
