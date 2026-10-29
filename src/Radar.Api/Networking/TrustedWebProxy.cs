using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Radar.Api.Networking
{
    public sealed class WebProxyOptions
    {
        public const string SectionName = "WebProxy";

        /// <summary>Shared with the web app's proxy. Empty = no proxy is trusted.</summary>
        public string? Secret { get; set; }
    }

    /// <summary>
    /// The web app (Vercel) forwards browser calls from /api/* to this API, so cookies stay first-party.
    /// Vercel's servers have no fixed IP addresses, so instead of trusting IPs we trust a shared secret:
    /// only a request carrying it may tell us the real client IP, host, scheme and path prefix.
    /// </summary>
    public sealed class TrustedWebProxyMiddleware(RequestDelegate next, IOptions<WebProxyOptions> options)
    {
        public const string SecretHeader = "X-Radar-Proxy-Secret";
        public const string ClientIpHeader = "X-Radar-Client-Ip";
        public const string HostHeader = "X-Radar-Host";
        public const string ProtoHeader = "X-Radar-Proto";
        public const string PathBaseHeader = "X-Radar-Path-Base";

        private static readonly string[] AllHeaders = [SecretHeader, ClientIpHeader, HostHeader, ProtoHeader, PathBaseHeader];

        public Task InvokeAsync(HttpContext context)
        {
            var request = context.Request;
            var secret = options.Value.Secret;

            if (!string.IsNullOrEmpty(secret)
                && request.Headers.TryGetValue(SecretHeader, out var provided)
                && SecretsMatch(provided.ToString(), secret))
            {
                if (IPAddress.TryParse(request.Headers[ClientIpHeader].ToString(), out var clientIp))
                {
                    context.Connection.RemoteIpAddress = clientIp;
                }

                var host = request.Headers[HostHeader].ToString();
                if (!string.IsNullOrEmpty(host))
                {
                    request.Host = new HostString(host);
                }

                var proto = request.Headers[ProtoHeader].ToString();
                if (proto is "http" or "https")
                {
                    request.Scheme = proto;
                }

                // e.g. "/api": the browser called /api/auth/login, the proxy forwarded /auth/login.
                var pathBase = request.Headers[PathBaseHeader].ToString();
                if (pathBase.StartsWith('/'))
                {
                    request.PathBase = new PathString(pathBase.TrimEnd('/'));
                }
            }

            // Trusted or not, these headers never reach the rest of the app.
            foreach (var header in AllHeaders)
            {
                request.Headers.Remove(header);
            }

            return next(context);
        }

        // Constant-time comparison: how long it takes doesn't reveal how many characters were right.
        private static bool SecretsMatch(string provided, string expected) =>
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected));
    }

    public static class TrustedWebProxyExtensions
    {
        public static IServiceCollection AddTrustedWebProxy(this IServiceCollection services, IConfiguration configuration) =>
            services.Configure<WebProxyOptions>(configuration.GetSection(WebProxyOptions.SectionName));

        public static IApplicationBuilder UseTrustedWebProxy(this IApplicationBuilder app) =>
            app.UseMiddleware<TrustedWebProxyMiddleware>();
    }
}
