using Microsoft.AspNetCore.Http.HttpResults;
using Radar.Application.Accounts;
using Radar.Api.Authentication;
using Radar.Api.RateLimiting;

namespace Radar.Api.Endpoints
{
    public static class AuthEndpoints
    {
        public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
        {
            // Every /auth endpoint shares the per-IP rate limit (brute force, sign-up spam, email bombing).
            var group = app.MapGroup("/auth")
                .WithTags("Auth")
                .RequireRateLimiting(RateLimitingExtensions.AuthPolicy);

            group.MapPost("/signup", SignUp).WithName("SignUp");

            group.MapPost("/verify-email", VerifyEmail).WithName("VerifyEmail");

            group.MapPost("/resend-verification", ResendVerification).WithName("ResendVerification");

            group.MapPost("/login", Login).WithName("Login");

            group.MapPost("/refresh", Refresh).WithName("Refresh");

            group.MapPost("/logout", Logout).WithName("Logout");

            return app;
        }

        private static async Task<NoContent> Logout(HttpContext http, IAccountService accounts, CancellationToken cancellationToken)
        {
            await accounts.LogoutAsync(RefreshTokenCookie.Read(http.Request), cancellationToken);
            RefreshTokenCookie.Delete(http.Response);

            // Always 204: logging out twice, or without a session, is not an error.
            return TypedResults.NoContent();
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

        private static async Task<Results<Ok<LoginResponse>, ProblemHttpResult>> Login(LoginRequest request, IAccountService accounts, HttpResponse response, CancellationToken cancellationToken)
        {
            var result = await accounts.LoginAsync(request, cancellationToken);

            if (result.Status == LoginStatus.Succeeded)
            {
                RefreshTokenCookie.Set(response, result.RefreshToken!);
            }

            return result.Status switch
            {
                LoginStatus.Succeeded => TypedResults.Ok(
                    new LoginResponse(result.AccessToken!.Token, "Bearer", result.AccessToken.ExpiresAt)),

                LoginStatus.EmailNotConfirmed => TypedResults.Problem(
                    title: "Email not confirmed",
                    detail: "Confirm your email address before signing in.",
                    statusCode: StatusCodes.Status403Forbidden),

                LoginStatus.LockedOut => TypedResults.Problem(
                    title: "Too many failed attempts",
                    detail: "Try again in 15 minutes.",
                    statusCode: StatusCodes.Status429TooManyRequests),

                _ => TypedResults.Problem(
                    title: "Invalid credentials",
                    detail: "Invalid email or password.",
                    statusCode: StatusCodes.Status401Unauthorized),
            };
        }

        private static async Task<Results<Ok<LoginResponse>, ProblemHttpResult>> Refresh(HttpContext http, IAccountService accounts, CancellationToken cancellationToken)
        {
            var refreshToken = RefreshTokenCookie.Read(http.Request);
            var result = refreshToken is null
                ? RefreshResult.Failure(RefreshStatus.Invalid)
                : await accounts.RefreshAsync(refreshToken, cancellationToken);

            if (result.Status != RefreshStatus.Succeeded)
            {
                // Whatever went wrong, this browser's session is over: clear the cookie.
                RefreshTokenCookie.Delete(http.Response);
                return TypedResults.Problem(
                    title: "Session expired",
                    detail: "Please sign in again.",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            RefreshTokenCookie.Set(http.Response, result.RefreshToken!);
            return TypedResults.Ok(new LoginResponse(result.AccessToken!.Token, "Bearer", result.AccessToken.ExpiresAt));
        }
    }

    public sealed record SignUpResponse(Guid UserId, Guid OrganizationId);

    public sealed record LoginResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);
}
