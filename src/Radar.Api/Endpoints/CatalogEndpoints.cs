using Microsoft.AspNetCore.Http.HttpResults;
using Radar.Application.Catalog;

namespace Radar.Api.Endpoints
{
    public static class CatalogEndpoints
    {
        public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
        {
            // Public on purpose: the list of APIs we track is not secret, and the landing page can show it.
            var group = app.MapGroup("/catalog")
                .WithTags("Catalog")
                .AllowAnonymous();

            group.MapGet("", GetCatalog).WithName("GetCatalog");

            return app;
        }

        private static async Task<Ok<IReadOnlyList<CatalogProviderView>>> GetCatalog(
            ICatalogService catalog, CancellationToken cancellationToken) =>
            TypedResults.Ok(await catalog.GetCatalogAsync(cancellationToken));
    }
}
