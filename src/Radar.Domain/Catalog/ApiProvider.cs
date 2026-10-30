namespace Radar.Domain.Catalog
{
    /// <summary>
    /// A company or product whose API we track, for example "Stripe".
    /// The catalog is shared by every organization, so this is not tenant-owned.
    /// </summary>
    public sealed class ApiProvider
    {
        public const int SlugMaxLength = 50;
        public const int NameMaxLength = 100;
        public const int UrlMaxLength = 500;

        // Used by EF Core when loading rows from the database.
        private ApiProvider()
        {
        }

        public ApiProvider(string slug, string name, string websiteUrl)
        {
            Id = Guid.CreateVersion7();
            Slug = CatalogRules.ValidateSlug(slug, SlugMaxLength);
            Name = CatalogRules.ValidateRequired(name, NameMaxLength, nameof(name));
            WebsiteUrl = CatalogRules.ValidateHttpsUrl(websiteUrl, UrlMaxLength, nameof(websiteUrl));
        }

        public Guid Id { get; private set; }

        /// <summary>URL-friendly, unique name, for example "stripe".</summary>
        public string Slug { get; private set; } = null!;

        public string Name { get; private set; } = null!;

        public string WebsiteUrl { get; private set; } = null!;

        public List<SpecSource> Sources { get; private set; } = [];
    }
}
