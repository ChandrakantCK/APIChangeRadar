namespace Radar.Domain.Catalog
{
    /// <summary>
    /// One machine-readable API description we crawl, for example Stripe's OpenAPI file.
    /// A provider can publish several (Twilio has one per product).
    /// </summary>
    public sealed class SpecSource
    {
        public const int SlugMaxLength = 80;
        public const int NameMaxLength = 150;
        public const int UrlMaxLength = 500;
        public const int RepositoryMaxLength = 200;
        public const int BranchMaxLength = 100;
        public const int FilePathMaxLength = 300;

        // Used by EF Core when loading rows from the database.
        private SpecSource()
        {
        }

        public SpecSource(Guid providerId, string slug, string name, string specUrl, GitLocation? git = null)
        {
            Id = Guid.CreateVersion7();
            ProviderId = providerId;
            Slug = CatalogRules.ValidateSlug(slug, SlugMaxLength);
            Name = CatalogRules.ValidateRequired(name, NameMaxLength, nameof(name));
            SpecUrl = CatalogRules.ValidateHttpsUrl(specUrl, UrlMaxLength, nameof(specUrl));
            Format = SpecFormat.OpenApi;
            IsActive = true;

            if (git is not null)
            {
                Repository = CatalogRules.ValidateRequired(git.Repository, RepositoryMaxLength, nameof(git));
                Branch = CatalogRules.ValidateRequired(git.Branch, BranchMaxLength, nameof(git));
                FilePath = CatalogRules.ValidateRequired(git.FilePath, FilePathMaxLength, nameof(git));
            }
        }

        public Guid Id { get; private set; }

        public Guid ProviderId { get; private set; }

        /// <summary>Unique within the provider, for example "api".</summary>
        public string Slug { get; private set; } = null!;

        public string Name { get; private set; } = null!;

        public SpecFormat Format { get; private set; }

        /// <summary>Where the crawler downloads the latest version.</summary>
        public string SpecUrl { get; private set; } = null!;

        /// <summary>
        /// Optional: the GitHub repository ("owner/name"), branch and file the spec lives in.
        /// Lets us replay its commit history (backfill) instead of only seeing future changes.
        /// </summary>
        public string? Repository { get; private set; }

        public string? Branch { get; private set; }

        public string? FilePath { get; private set; }

        /// <summary>Inactive sources stay in the catalog (old changes keep pointing at them) but are not crawled.</summary>
        public bool IsActive { get; private set; }
    }

    public sealed record GitLocation(string Repository, string Branch, string FilePath);
}
