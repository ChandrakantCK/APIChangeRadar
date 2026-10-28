using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Radar.Application.Organizations;
using Radar.Infrastructure.Tenancy;

namespace Radar.Api.Tenancy
{
    /// <summary>
    /// Runs before every organization-scoped endpoint: works out which organization the request acts on
    /// and checks the caller is a member. Without that, the endpoint never runs.
    /// </summary>
    public sealed class OrganizationScopeFilter : IEndpointFilter
    {
        /// <summary>Optional: pick one of your organizations. Without it, your first organization is used.</summary>
        public const string HeaderName = "X-Organization-Id";

        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var http = context.HttpContext;

            if (!Guid.TryParse(http.User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId))
            {
                return TypedResults.Unauthorized();
            }

            Guid? requestedOrganizationId = null;
            if (http.Request.Headers.TryGetValue(HeaderName, out var raw))
            {
                if (!Guid.TryParse(raw.ToString(), out var parsed))
                {
                    return TypedResults.Problem(
                        title: "Invalid organization",
                        detail: $"{HeaderName} must be an organization id.",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                requestedOrganizationId = parsed;
            }

            var organizations = http.RequestServices.GetRequiredService<IOrganizationService>();
            var access = await organizations.ResolveAccessAsync(userId, requestedOrganizationId, http.RequestAborted);

            if (access is null)
            {
                // Someone else's organization id (or a made-up one): stop here. This is the IDOR check.
                return TypedResults.Problem(
                    title: "Forbidden",
                    detail: "You are not a member of this organization.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            http.RequestServices.GetRequiredService<CurrentOrganizationContext>().Set(access.OrganizationId, access.Role);

            return await next(context);
        }
    }
}