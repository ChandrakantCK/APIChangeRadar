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

            group.MapPost("/verify-email", VerifyEmail).WithName("VerifyEmail");

            group.MapPost("/resend-verification", ResendVerification).WithName("ResendVerification");

            return app;
        }

        private static async Task<Results<NoContent, ValidationProblem>> VerifyEmail(VerifyEmailRequest request, IAccountService accounts, CancellationToken cancellationToken)
        {
            if (await accounts.VerifyEmailAsync(request, cancellationToken))
            {
                return TypedResults.NoContent();
            }

            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["InvalidToken"] = ["This verification link is invalid or has expired. Request a new one."],
            });
        }

        private static async Task<Accepted> ResendVerification(
            ResendVerificationEmailRequest request, IAccountService accounts, CancellationToken cancellationToken)
        {
            await accounts.ResendVerificationEmailAsync(request, cancellationToken);

            // Always the same answer, whether or not the email has an account.
            return TypedResults.Accepted((string?)null);
        }

        private static async Task<Results<Created<SignUpResponse>, ValidationProblem>> SignUp(
            SignUpRequest request, IAccountService accounts, CancellationToken cancellationToken)
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
