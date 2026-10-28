using System.Net;
using IPNetwork = System.Net.IPNetwork;
using Microsoft.AspNetCore.HttpOverrides;

namespace Radar.Api.Networking
{
    /// <summary>
    /// The proxies between a user and our container on Render: Cloudflare's edge, then Render's
    /// internal load balancers (private addresses). We trust X-Forwarded-For entries added by these,
    /// and nothing else, so the first untrusted address from the right is the real client.
    /// </summary>
    public static class TrustedProxies
    {
        // Published at https://www.cloudflare.com/ips/ - they change rarely; re-check them now and then.
        private static readonly string[] Cloudflare =
        [
            "173.245.48.0/20", "103.21.244.0/22", "103.22.200.0/22", "103.31.4.0/22", "141.101.64.0/18",
            "108.162.192.0/18", "190.93.240.0/20", "188.114.96.0/20", "197.234.240.0/22", "198.41.128.0/17",
            "162.158.0.0/15", "104.16.0.0/13", "104.24.0.0/14", "172.64.0.0/13", "131.0.72.0/22",
            "2400:cb00::/32", "2606:4700::/32", "2803:f800::/32", "2405:b500::/32", "2405:8100::/32",
            "2a06:98c0::/29", "2c0f:f248::/32",
        ];

        // Loopback and private ranges: Render's internal network, Docker, and local development.
        private static readonly string[] Private =
        [
            "127.0.0.0/8", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "100.64.0.0/10",
            "::1/128", "fc00::/7",
        ];

        public static IReadOnlyList<IPNetwork> Networks { get; } =
            Cloudflare.Concat(Private).Select(IPNetwork.Parse).ToArray();

        public static IServiceCollection AddTrustedForwardedHeaders(this IServiceCollection services)
        {
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

                // Walk back through EVERY trusted hop (Cloudflare + Render), not just the last one.
                options.ForwardLimit = null;

                // Cloudflare and Render don't always send the same number of For and Proto values;
                // by default ASP.NET Core would then ignore both headers completely.
                options.RequireHeaderSymmetry = false;

                options.KnownProxies.Clear();
                options.KnownIPNetworks.Clear();
                foreach (var network in Networks)
                {
                    options.KnownIPNetworks.Add(network);
                }
            });

            return services;
        }
    }
}