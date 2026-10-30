using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radar.Api.Endpoints;
using Radar.Application.Accounts;
using Radar.Application.Organizations;
using Radar.Application.Watches;
using Radar.Domain.Catalog;
using Radar.Infrastructure.Identity;
using Radar.Infrastructure.Persistence;
using Radar.Infrastructure.Persistence.Seed;
using Radar.Integration.Tests.Infrastructure;

namespace Radar.Integration.Tests.Watches
{
    [Collection(ApiCollection.Name)]
    public class WatchTests(RadarApiFactory factory)
    {
        private const string ValidPassword = "correct horse battery staple";
        private const string WatchesPath = "/organizations/current/watches";

        // Signs up a verified user with their own organization and returns a client that carries their token.
        private async Task<HttpClient> CreateSignedInClientAsync()
        {
            var client = factory.CreateClient();
            var email = $"watcher-{Guid.NewGuid():N}@example.com";
            (await client.PostAsJsonAsync("/auth/signup", new { email, password = ValidPassword, organizationName = "Watchers" })).EnsureSuccessStatusCode();

            using (var scope = factory.Services.CreateScope())
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = await userManager.FindByEmailAsync(email);
                await userManager.ConfirmEmailAsync(user!, await userManager.GenerateEmailConfirmationTokenAsync(user!));
            }

            var login = await client.PostAsJsonAsync("/auth/login", new { email, password = ValidPassword });
            var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        // The seeded catalog has only 3 sources, the same as the free limit, so limit tests add their own.
        private async Task<List<Guid>> CreateSourcesAsync(int count)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var provider = new ApiProvider($"test-{Guid.NewGuid():N}", "Test provider", "https://example.com");
            var sources = Enumerable.Range(1, count)
                .Select(i => new SpecSource(provider.Id, $"api-{i}", $"Test API {i}", $"https://example.com/openapi-{i}.json"))
                .ToList();

            db.ApiProviders.Add(provider);
            db.SpecSources.AddRange(sources);
            await db.SaveChangesAsync();
            return sources.Select(s => s.Id).ToList();
        }

        private static Task<HttpResponseMessage> AddAsync(HttpClient client, Guid sourceId) =>
            client.PostAsJsonAsync(WatchesPath, new { sourceId });

        [Fact]
        public async Task A_member_can_add_list_and_remove_a_watch()
        {
            var client = await CreateSignedInClientAsync();

            var added = await AddAsync(client, CatalogSeed.StripeApiId);
            Assert.Equal(HttpStatusCode.Created, added.StatusCode);
            var watch = (await added.Content.ReadFromJsonAsync<WatchView>())!;
            Assert.Equal("Stripe", watch.ProviderName);
            Assert.Equal("Stripe API", watch.SourceName);

            var list = (await client.GetFromJsonAsync<WatchList>(WatchesPath))!;
            Assert.Equal(1, list.Used);
            Assert.Equal(3, list.Limit);
            Assert.Equal(watch.Id, list.Items.Single().Id);

            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{WatchesPath}/{watch.Id}")).StatusCode);
            Assert.Empty((await client.GetFromJsonAsync<WatchList>(WatchesPath))!.Items);

            var audit = (await client.GetFromJsonAsync<List<AuditEntryView>>("/organizations/current/audit-log"))!;
            Assert.Contains(audit, e => e.Action == "watch.added" && e.Target == "Stripe / Stripe API");
            Assert.Contains(audit, e => e.Action == "watch.removed" && e.Target == "Stripe / Stripe API");
        }

        [Fact]
        public async Task The_free_plan_can_watch_at_most_three_apis()
        {
            var client = await CreateSignedInClientAsync();
            var sources = await CreateSourcesAsync(4);

            foreach (var sourceId in sources.Take(3))
            {
                Assert.Equal(HttpStatusCode.Created, (await AddAsync(client, sourceId)).StatusCode);
            }

            var fourth = await AddAsync(client, sources[3]);

            Assert.Equal(HttpStatusCode.Forbidden, fourth.StatusCode);
            Assert.Contains("up to 3", await fourth.Content.ReadAsStringAsync());
            Assert.Equal(3, (await client.GetFromJsonAsync<WatchList>(WatchesPath))!.Used);
        }

        [Fact]
        public async Task Simultaneous_adds_cannot_exceed_the_limit()
        {
            var client = await CreateSignedInClientAsync();
            var sources = await CreateSourcesAsync(6);

            // Six requests at once: without the row lock several would count "0, 1 or 2 used" and all insert.
            var responses = await Task.WhenAll(sources.Select(id => AddAsync(client, id)));

            Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
            Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Forbidden));
            Assert.Equal(3, (await client.GetFromJsonAsync<WatchList>(WatchesPath))!.Used);
        }

        [Fact]
        public async Task Watching_the_same_api_twice_is_a_conflict()
        {
            var client = await CreateSignedInClientAsync();

            Assert.Equal(HttpStatusCode.Created, (await AddAsync(client, CatalogSeed.TwilioApiV2010Id)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await AddAsync(client, CatalogSeed.TwilioApiV2010Id)).StatusCode);
        }

        [Fact]
        public async Task Unknown_or_missing_sources_are_rejected()
        {
            var client = await CreateSignedInClientAsync();

            Assert.Equal(HttpStatusCode.NotFound, (await AddAsync(client, Guid.NewGuid())).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(WatchesPath, new { })).StatusCode);
        }

        [Fact]
        public async Task Another_organization_cannot_see_or_remove_my_watch()
        {
            var owner = await CreateSignedInClientAsync();
            var attacker = await CreateSignedInClientAsync();
            var watch = (await (await AddAsync(owner, CatalogSeed.GitHubRestApiId)).Content.ReadFromJsonAsync<WatchView>())!;

            Assert.Empty((await attacker.GetFromJsonAsync<WatchList>(WatchesPath))!.Items);

            // 404, not 403: the attacker learns nothing about whether the id exists.
            Assert.Equal(HttpStatusCode.NotFound, (await attacker.DeleteAsync($"{WatchesPath}/{watch.Id}")).StatusCode);
            Assert.Single((await owner.GetFromJsonAsync<WatchList>(WatchesPath))!.Items);
        }

        [Fact]
        public async Task Watches_require_sign_in()
        {
            var anonymous = factory.CreateClient();

            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(WatchesPath)).StatusCode);
        }

        [Fact]
        public async Task A_watched_source_cannot_be_deleted_from_the_catalog()
        {
            var client = await CreateSignedInClientAsync();
            var sourceId = (await CreateSourcesAsync(1)).Single();
            Assert.Equal(HttpStatusCode.Created, (await AddAsync(client, sourceId)).StatusCode);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.SpecSources.Remove(await db.SpecSources.SingleAsync(s => s.Id == sourceId));

            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }
}
