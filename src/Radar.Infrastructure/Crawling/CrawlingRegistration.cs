using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Radar.Application.Crawling;

namespace Radar.Infrastructure.Crawling
{
    internal static class CrawlingRegistration
    {
        public static IServiceCollection AddCrawling(this IServiceCollection services, IConfiguration configuration)
        {
            var section = configuration.GetSection(CrawlerOptions.SectionName);
            services.Configure<CrawlerOptions>(section);

            services.AddHttpClient(SpecFetcher.HttpClientName, client =>
                {
                    // SpecFetcher applies its own (configurable) timeout per request.
                    client.Timeout = Timeout.InfiniteTimeSpan;
                    // Polite crawlers say who they are and where to find out more.
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("APIChangeRadar/1.0 (+https://api-change-radar.vercel.app)");
                })
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    AutomaticDecompression = DecompressionMethods.All,
                    MaxAutomaticRedirections = 5,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                });

            services.AddScoped<SpecFetcher>();
            services.AddScoped<ICrawler, Crawler>();

            if (section.GetValue<bool>(nameof(CrawlerOptions.Enabled)))
            {
                services.AddHostedService<CrawlerHostedService>();
            }

            return services;
        }
    }
}
