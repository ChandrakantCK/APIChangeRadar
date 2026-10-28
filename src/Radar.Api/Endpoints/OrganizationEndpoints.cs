using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.IdentityModel.JsonWebTokens;
using Radar.Api.Tenancy;
using Radar.Application.Organizations;

namespace Radar.Api.Endpoints
{
    public static class OrganizationEndpoints
    {
        public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder app)
        {
            // "current" = the organization chosen by OrganizationScopeFilter for this request.
            var group = app.MapGroup("/organizations/current")
                .WithTags("Organizations")
                .RequireAuthorization()
                .AddEndpointFilter<OrganizationScopeFilter>();

            group.MapGet("", GetCurrent).WithName("GetCurrentOrganization");
            group.MapPatch("", Rename).WithName("RenameCurrentOrganization");
            group.MapGet("/audit-log", GetAuditLog).WithName("GetAuditLog");

            return app;
        }

        private static async Task<Results<Ok<OrganizationDetails>, NotFound>> GetCurrent(
            IOrganizationService organizations, CancellationToken cancellationToken)
        {
            var details = await organizations.GetCurrentAsync(cancellationToken);
            return details is null ? TypedResults.NotFound() : TypedResults.Ok(details);
        }

        private static async Task<Results<Ok<OrganizationDetails>, ProblemHttpResult, ValidationProblem>> Rename(
            RenameOrganizationRequest request,
            ClaimsPrincipal principal,
            HttpContext http,
            IOrganizationService organizations,
            CancellationToken cancellationToken)
        {
            var actorUserId = Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
            var ipAddress = http.Connection.RemoteIpAddress?.ToString();

            var result = await organizations.RenameCurrentAsync(request.Name, actorUserId, ipAddress, cancellationToken);

            return result.Status switch
            {
                RenameStatus.Renamed => TypedResults.Ok(result.Organization!),
                RenameStatus.Invalid => TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Name"] = [result.Error!],
                }),
                _ => TypedResults.Problem(title: "Forbidden", detail: result.Error, statusCode: StatusCodes.Status403Forbidden),
            };
        }

        private static async Task<Ok<IReadOnlyList<AuditEntryView>>> GetAuditLog(
            IOrganizationService organizations, CancellationToken cancellationToken) =>
            TypedResults.Ok(await organizations.GetAuditLogAsync(cancellationToken));
    }

    public sealed record RenameOrganizationRequest(string? Name);
}