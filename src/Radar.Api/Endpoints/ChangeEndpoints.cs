using Microsoft.AspNetCore.Http.HttpResults;
using Radar.Api.Tenancy;
using Radar.Application.Changes;

namespace Radar.Api.Endpoints
{
    public static class ChangeEndpoints
    {
        public static IEndpointRouteBuilder MapChangeEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/organizations/current/changes")
                .WithTags("Changes")
                .RequireAuthorization()
                .AddEndpointFilter<OrganizationScopeFilter>();

            group.MapGet("", List).WithName("ListChanges");
            group.MapGet("/{id:guid}", Get).WithName("GetChangeSet");

            return app;
        }

        /// <param name="severity">"all" (default), "warning" (warning or breaking) or "breaking".</param>
        /// <param name="before">The previous page's nextBefore.</param>
        /// <param name="limit">1 to 50, default 20.</param>
        private static async Task<Results<Ok<ChangeFeed>, ValidationProblem>> List(
            IChangeFeedService feed,
            string? severity,
            DateTimeOffset? before,
            int? limit,
            CancellationToken cancellationToken)
        {
            var errors = new Dictionary<string, string[]>();
            FeedSeverity parsed = FeedSeverity.All;
            if (severity is not null && (!Enum.TryParse(severity, ignoreCase: true, out parsed) || !Enum.IsDefined(parsed)))
            {
                errors["severity"] = ["Use all, warning or breaking."];
            }

            if (limit is < 1 or > IChangeFeedService.MaxPageSize)
            {
                errors["limit"] = [$"Use a number from 1 to {IChangeFeedService.MaxPageSize}."];
            }

            if (errors.Count > 0)
            {
                return TypedResults.ValidationProblem(errors);
            }

            return TypedResults.Ok(await feed.ListAsync(parsed, before, limit ?? IChangeFeedService.DefaultPageSize, cancellationToken));
        }

        private static async Task<Results<Ok<ChangeSetDetail>, NotFound>> Get(
            Guid id, IChangeFeedService feed, CancellationToken cancellationToken) =>
            await feed.GetAsync(id, cancellationToken) is { } detail
                ? TypedResults.Ok(detail)
                : TypedResults.NotFound();
    }
}
