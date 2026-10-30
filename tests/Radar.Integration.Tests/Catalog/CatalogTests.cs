using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radar.Application.Catalog;
using Radar.Domain.Catalog;
using Radar.Infrastructure.Persistence;
using Radar.Integration.Tests.Infrastructure;

namespace Radar.Integration.Tests.Catalog
{
    [Collection(ApiCollection.Name)]
    public class CatalogTests(RadarApiFactory factory)
    {
        [Fact]
        public async Task Catalog_lists_the_seeded_apis_without_signing_in()
        {
            var client = factory.CreateClient();

            var response = await client.GetAsync("/catalog");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var catalog = (await response.Content.ReadFromJsonAsync<List<CatalogProviderView>>())!;

            // Ordered by name; other tests may add providers, so only check the seeded ones.
            var seeded = catalog.Where(p => p.Slug is "github" or "stripe" or "twilio").ToList();
            Assert.Equal(["GitHub", "Stripe", "Twilio"], seeded.Select(p => p.Name));

            var stripe = seeded.Single(p => p.Slug == "stripe").Sources.Single();
            Assert.Equal("api", stripe.Slug);
            Assert.Equal("OpenApi", stripe.Format);
            Assert.Equal("https://raw.githubusercontent.com/stripe/openapi/master/openapi/spec3.json", stripe.SpecUrl);
        }

        [Fact]
        public async Task Seeded_sources_know_their_git_history_location()
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var sources = await db.SpecSources.Where(s => s.Repository != null).ToListAsync();

            Assert.True(sources.Count >= 3);
            Assert.All(sources, s =>
            {
                Assert.True(s.IsActive);
                Assert.Equal($"https://raw.githubusercontent.com/{s.Repository}/{s.Branch}/{s.FilePath}", s.SpecUrl);
            });
        }

        [Fact]
        public async Task Provider_slugs_are_unique()
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.ApiProviders.Add(new ApiProvider("stripe", "Another Stripe", "https://example.com"));

            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        [Theory]
        [InlineData("http://example.com/openapi.json")]
        [InlineData("file:///etc/passwd")]
        [InlineData("not a url")]
        public void Spec_url_must_be_https(string specUrl)
        {
            Assert.Throws<ArgumentException>(() => new SpecSource(Guid.NewGuid(), "api", "Example API", specUrl));
        }

        [Theory]
        [InlineData("Stripe")]
        [InlineData("my api")]
        [InlineData("-api")]
        [InlineData("")]
        public void Slug_must_be_lowercase_letters_digits_and_dashes(string slug)
        {
            Assert.Throws<ArgumentException>(() => new ApiProvider(slug, "Example", "https://example.com"));
        }
    }
}
