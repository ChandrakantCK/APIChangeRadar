using Radar.Application.Accounts;

namespace Radar.Api.Authentication
{
    /// <summary>
    /// The refresh token travels only in this cookie: JavaScript can't read it (HttpOnly),
    /// it's only sent over HTTPS (Secure), never by other sites (SameSite=Strict),
    /// and only to the auth endpoints (Path: /auth, or /api/auth when called through the web app's proxy).
    /// </summary>
    public static class RefreshTokenCookie
    {
        public const string Name = "radar_refresh";

        private static CookieOptions Options(HttpRequest request, DateTimeOffset? expires) => new()
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = $"{request.PathBase}/auth",
            Expires = expires,
        };

        public static void Set(HttpResponse response, IssuedRefreshToken token) =>
            response.Cookies.Append(Name, token.Token, Options(response.HttpContext.Request, token.ExpiresAt));

        public static string? Read(HttpRequest request) =>
            request.Cookies.TryGetValue(Name, out var value) && !string.IsNullOrEmpty(value) ? value : null;

        public static void Delete(HttpResponse response) =>
            response.Cookies.Delete(Name, Options(response.HttpContext.Request, expires: null));
    }
}