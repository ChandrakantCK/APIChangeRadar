using Radar.Domain.Catalog;

namespace Radar.Infrastructure.Persistence.Seed
{
    /// <summary>
    /// The starting catalog, inserted by a migration (EF Core "HasData"), so every environment gets the same rows.
    /// Ids are fixed: changing or removing a row here produces an UPDATE/DELETE in the next migration.
    /// </summary>
    internal static class CatalogSeed
    {
        public static readonly Guid StripeId = new("528c303d-c68d-4834-80a6-cdc860f74b43");
        public static readonly Guid GitHubId = new("d5b42dc0-59b2-4ec4-8dff-bbe4446a7c54");
        public static readonly Guid TwilioId = new("8cd22e47-268b-4e0d-891b-f9cfd327af2d");

        public static readonly Guid StripeApiId = new("42c4b303-6db2-48d1-bf97-304feab0ad8d");
        public static readonly Guid GitHubRestApiId = new("5cba65a3-c7c2-494d-ae65-93ccfa284438");
        public static readonly Guid TwilioApiV2010Id = new("cdec6a40-8dbb-4bdc-98a5-a755c9490064");

        // Anonymous objects: HasData sets the private setters directly, without running the constructors.
        public static object[] Providers =>
        [
            new { Id = StripeId, Slug = "stripe", Name = "Stripe", WebsiteUrl = "https://stripe.com" },
            new { Id = GitHubId, Slug = "github", Name = "GitHub", WebsiteUrl = "https://github.com" },
            new { Id = TwilioId, Slug = "twilio", Name = "Twilio", WebsiteUrl = "https://www.twilio.com" },
        ];

        public static object[] Sources =>
        [
            Source(StripeApiId, StripeId, "api", "Stripe API",
                "stripe/openapi", "master", "openapi/spec3.json"),
            Source(GitHubRestApiId, GitHubId, "rest-api", "GitHub REST API",
                "github/rest-api-description", "main", "descriptions/api.github.com/api.github.com.json"),
            Source(TwilioApiV2010Id, TwilioId, "api-v2010", "Twilio API (2010-04-01)",
                "twilio/twilio-oai", "main", "spec/json/twilio_api_v2010.json"),
        ];

        // The download URL is the raw file on the tracked branch.
        private static object Source(Guid id, Guid providerId, string slug, string name, string repository, string branch, string filePath) =>
            new
            {
                Id = id,
                ProviderId = providerId,
                Slug = slug,
                Name = name,
                Format = SpecFormat.OpenApi,
                SpecUrl = $"https://raw.githubusercontent.com/{repository}/{branch}/{filePath}",
                Repository = repository,
                Branch = branch,
                FilePath = filePath,
                IsActive = true,
            };
    }
}
