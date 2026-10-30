using Microsoft.EntityFrameworkCore;
using Radar.Application.Catalog;
using Radar.Infrastructure.Persistence;

namespace Radar.Infrastructure.Catalog
{
    internal sealed class CatalogService(AppDbContext db) : ICatalogService
    {
        public async Task<IReadOnlyList<CatalogProviderView>> GetCatalogAsync(CancellationToken cancellationToken = default)
        {
            // The catalog is global (not tenant-owned), so no query filter applies and anyone may read it.
            return await db.ApiProviders
                .AsNoTracking()
                .OrderBy(p => p.Name)
                .Select(p => new CatalogProviderView(
                    p.Id,
                    p.Slug,
                    p.Name,
                    p.WebsiteUrl,
                    p.Sources
                        .Where(s => s.IsActive)
                        .OrderBy(s => s.Name)
                        .Select(s => new CatalogSourceView(s.Id, s.Slug, s.Name, s.Format.ToString(), s.SpecUrl))
                        .ToList()))
                .ToListAsync(cancellationToken);
        }
    }
}
