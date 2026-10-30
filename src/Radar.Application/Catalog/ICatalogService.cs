namespace Radar.Application.Catalog
{
    public sealed record CatalogSourceView(Guid Id, string Slug, string Name, string Format, string SpecUrl);

    public sealed record CatalogProviderView(Guid Id, string Slug, string Name, string WebsiteUrl, IReadOnlyList<CatalogSourceView> Sources);

    public interface ICatalogService
    {
        /// <summary>Every provider with its active sources, ordered by name.</summary>
        Task<IReadOnlyList<CatalogProviderView>> GetCatalogAsync(CancellationToken cancellationToken = default);
    }
}
