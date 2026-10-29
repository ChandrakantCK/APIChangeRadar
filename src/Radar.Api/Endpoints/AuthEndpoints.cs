using Microsoft.AspNetCore.Http.HttpResults;
using Radar.Application.Accounts;
using Radar.Api.Authentication;
using Radar.Api.RateLimiting;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Radar.Infrastructure;

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

            group.MapGet("/github/start", GitHubStart).WithName("GitHubStart");

            group.MapGet("/github/complete", GitHubComplete).WithName("GitHubComplete");

            return app;
        }

        // Step 1: send the browser to GitHub. The handler adds client_id, redirect_uri and a random "state".
        private static async Task<IResult> GitHubStart(HttpContext http, IAuthenticationSchemeProvider schemes)
        {
            if (await schemes.GetSchemeAsync(GitHubLogin.Scheme) is null)
            {
                return TypedResults.NotFound(); // GitHub sign-in isn't configured here
            }

            // PathBase keeps the browser on the proxy path ("/api/auth/...") when we're behind the web app.
            var properties = new AuthenticationProperties { RedirectUri = $"{http.Request.PathBase}/auth/github/complete" };
            return TypedResults.Challenge(properties, [GitHubLogin.Scheme]);
        }

        // Step 3 (after GitHub's handler finished step 2 at /auth/github/callback): turn the GitHub identity
        // into our user + session, then send the browser back to the web app.
        private static async Task<IResult> GitHubComplete(HttpContext http, IAccountService accounts, IOptions<AppOptions> appOptions, CancellationToken cancellationToken)
        {
            var web = appOptions.Value.WebBaseUrl.TrimEnd('/');

            var external = await http.AuthenticateAsync(GitHubLogin.ExternalCookieScheme);

            // The temporary external cookie has done its job, whatever happens next.
            await http.SignOutAsync(GitHubLogin.ExternalCookieScheme);

            var providerKey = external.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!external.Succeeded || providerKey is null)
            {
                return TypedResults.Redirect($"{web}/login?error=github");
            }

            var result = await accounts.ExternalLoginAsync(
                new ExternalLoginRequest(GitHubLogin.Scheme, providerKey, external.Principal!.FindFirstValue(ClaimTypes.Email)),
                cancellationToken);

            if (result.Status != LoginStatus.Succeeded)
            {
                var error = result.Status switch
                {
                    LoginStatus.EmailNotConfirmed => "github-no-verified-email",
                    LoginStatus.LockedOut => "locked",
                    _ => "github",
                };
                return TypedResults.Redirect($"{web}/login?error={error}");
            }

            // Same session as a password login: the refresh cookie. The web app's /auth/callback page
            // then calls POST /auth/refresh to get its first access token (Step 11).
            RefreshTokenCookie.Set(http.Response, result.RefreshToken!);
            return TypedResults.Redirect($"{web}/auth/callback");
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
