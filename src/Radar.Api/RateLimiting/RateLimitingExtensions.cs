using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace Radar.Api.RateLimiting
{
    public sealed class RateLimitingOptions
    {
        public const string SectionName = "RateLimiting";

        /// <summary>Requests allowed per client IP per window on /auth endpoints.</summary>
        public int AuthPermitLimit { get; set; } = 10;

        public int AuthWindowSeconds { get; set; } = 60;
    }

    public static class RateLimitingExtensions
    {
        public const string AuthPolicy = "auth";

        public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<RateLimitingOptions>(configuration.GetSection(RateLimitingOptions.SectionName));

            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                // Tell well-behaved clients when they may try again.
                options.OnRejected = (context, _) =>
                {
                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    {
                        context.HttpContext.Response.Headers.RetryAfter =
                            ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                    }

                    return ValueTask.CompletedTask;
                };

                // One bucket per client IP: each address gets its own allowance per window.
                options.AddPolicy(AuthPolicy, http =>
                {
                    var settings = http.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
                    var clientIp = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                    return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.AuthPermitLimit,
                        Window = TimeSpan.FromSeconds(settings.AuthWindowSeconds),
                        QueueLimit = 0,
                    });
                });
            });

            return services;
        }
    }
}