using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.IdentityModel.JsonWebTokens;
using Radar.Application.Accounts;

namespace Radar.Api.Endpoints
{
    public static class MeEndpoints
    {
        public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
        {
            app.MapGet("/me", GetMe)
                .RequireAuthorization()
                .WithTags("Me")
                .WithName("GetMe");

            return app;
        }

        private static async Task<Results<Ok<UserProfile>, UnauthorizedHttpResult>> GetMe(
            ClaimsPrincipal principal,
            IAccountService accounts,
            CancellationToken cancellationToken)
        {
            // "sub" is the user id we put into the token at login.
            if (!Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId))
            {
                return TypedResults.Unauthorized();
            }

            var profile = await accounts.GetProfileAsync(userId, cancellationToken);

            // A valid token for a user who no longer exists.
            return profile is null ? TypedResults.Unauthorized() : TypedResults.Ok(profile);
        }
    }
}