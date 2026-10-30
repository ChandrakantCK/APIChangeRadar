using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.IdentityModel.JsonWebTokens;
using Radar.Api.Tenancy;
using Radar.Application.Watches;

namespace Radar.Api.Endpoints
{
    public static class WatchEndpoints
    {
        public static IEndpointRouteBuilder MapWatchEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/organizations/current/watches")
                .WithTags("Watches")
                .RequireAuthorization()
                .AddEndpointFilter<OrganizationScopeFilter>();

            group.MapGet("", List).WithName("ListWatches");
            group.MapPost("", Add).WithName("AddWatch");
            group.MapDelete("/{id:guid}", Remove).WithName("RemoveWatch");

            return app;
        }

        private static async Task<Ok<WatchList>> List(IWatchService watches, CancellationToken cancellationToken) =>
            TypedResults.Ok(await watches.ListAsync(cancellationToken));

        private static async Task<Results<Created<WatchView>, ProblemHttpResult, ValidationProblem>> Add(
            AddWatchRequest request,
            ClaimsPrincipal principal,
            HttpContext http,
            IWatchService watches,
            CancellationToken cancellationToken)
        {
            if (request.SourceId is not { } sourceId || sourceId == Guid.Empty)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["SourceId"] = ["Choose an API from the catalog."],
                });
            }

            var result = await watches.AddAsync(sourceId, ActorId(principal), http.Connection.RemoteIpAddress?.ToString(), cancellationToken);

            return result.Status switch
            {
                AddWatchStatus.Added => TypedResults.Created($"/organizations/current/watches/{result.Watch!.Id}", result.Watch),
                AddWatchStatus.SourceNotFound => TypedResults.Problem(title: "Not found", detail: result.Error, statusCode: StatusCodes.Status404NotFound),
                AddWatchStatus.AlreadyWatching => TypedResults.Problem(title: "Already watching", detail: result.Error, statusCode: StatusCodes.Status409Conflict),
                _ => TypedResults.Problem(title: "Plan limit reached", detail: result.Error, statusCode: StatusCodes.Status403Forbidden),
            };
        }

        private static async Task<Results<NoContent, NotFound>> Remove(
            Guid id,
            ClaimsPrincipal principal,
            HttpContext http,
            IWatchService watches,
            CancellationToken cancellationToken) =>
            await watches.RemoveAsync(id, ActorId(principal), http.Connection.RemoteIpAddress?.ToString(), cancellationToken)
                ? TypedResults.NoContent()
                : TypedResults.NotFound();

        private static Guid ActorId(ClaimsPrincipal principal) =>
            Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
    }

    public sealed record AddWatchRequest(Guid? SourceId);
}
