using Microsoft.AspNetCore.Http.HttpResults;
using Radar.Application.Accounts;

namespace Radar.Api.Endpoints
{
    public static class AuthEndpoints
    {
        public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/auth").WithTags("Auth");

            group.MapPost("/signup", SignUp).WithName("SignUp");

            return app;
        }

        private static async Task<Results<Created<SignUpResponse>, ValidationProblem>> SignUp(
            SignUpRequest request,
            IAccountService accounts,
            CancellationToken cancellationToken)
        {
            var result = await accounts.SignUpAsync(request, cancellationToken);

            if (!result.Succeeded)
            {
                var errors = result.Errors
                    .GroupBy(e => e.Code)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

                return TypedResults.ValidationProblem(errors);
            }

            return TypedResults.Created(
                $"/organizations/{result.OrganizationId}",
                new SignUpResponse(result.UserId, result.OrganizationId));
        }
    }

    public sealed record SignUpResponse(Guid UserId, Guid OrganizationId);
}
