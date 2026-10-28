using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Radar.Api.Authentication
{
    /// <summary>
    /// "Continue with GitHub" (OAuth 2.0 authorization code flow).
    /// GitHub's handler does the protocol work - redirect, state/correlation check, code-for-token exchange -
    /// and parks the result in a short-lived "External" cookie that /auth/github/complete reads once.
    /// </summary>
    public static class GitHubLogin
    {
        public const string Scheme = "GitHub";
        public const string ExternalCookieScheme = "External";
        public const string CallbackPath = "/auth/github/callback";

        public static IServiceCollection AddGitHubLogin(this IServiceCollection services, IConfiguration configuration)
        {
            var authentication = services.AddAuthentication()
                .AddCookie(ExternalCookieScheme, options =>
                {
                    options.Cookie.Name = "radar_external";
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;

                    // Lax, not Strict: the browser must send it on the redirect back from github.com.
                    options.Cookie.SameSite = SameSiteMode.Lax;
                    options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
                });

            var clientId = configuration["GitHub:ClientId"];
            var clientSecret = configuration["GitHub:ClientSecret"];
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                // Not configured (e.g. tests, or production until Step 11): the feature is simply off.
                return services;
            }

            authentication.AddGitHub(Scheme, options =>
            {
                options.ClientId = clientId;
                options.ClientSecret = clientSecret;
                options.CallbackPath = CallbackPath;
                options.SignInScheme = ExternalCookieScheme;

                // Read-only access to the user's email addresses; nothing else.
                options.Scope.Add("user:email");

                options.Events.OnCreatingTicket = async context =>
                {
                    // Ask GitHub which addresses are VERIFIED, and keep only the primary verified one.
                    using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user/emails");
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

                    using var response = await context.Backchannel.SendAsync(request, context.HttpContext.RequestAborted);
                    response.EnsureSuccessStatusCode();
                    var emails = await response.Content.ReadFromJsonAsync<GitHubEmail[]>(context.HttpContext.RequestAborted) ?? [];

                    var verified = emails.FirstOrDefault(e => e.Primary && e.Verified) ?? emails.FirstOrDefault(e => e.Verified);

                    foreach (var claim in context.Identity!.FindAll(ClaimTypes.Email).ToList())
                    {
                        context.Identity.RemoveClaim(claim);
                    }

                    if (verified is not null)
                    {
                        context.Identity.AddClaim(new Claim(ClaimTypes.Email, verified.Email));
                    }
                };
            });

            return services;
        }

        private sealed record GitHubEmail(string Email, bool Primary, bool Verified);
    }
}