using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radar.Application.Crawling;

namespace Radar.Infrastructure.Crawling
{
    /// <summary>
    /// Wakes up every few minutes and crawls the sources that are due.
    /// Runs inside the API process for now (Render's free tier has no background workers); Phase 3 moves it out.
    /// </summary>
    internal sealed class CrawlerHostedService(
        IServiceScopeFactory scopes,
        IOptions<CrawlerOptions> options,
        TimeProvider time,
        ILogger<CrawlerHostedService> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.Value.PollMinutes), time);

            do
            {
                try
                {
                    // A hosted service lives for the whole app; the DbContext must not. One scope per pass.
                    await using var scope = scopes.CreateAsyncScope();
                    var summary = await scope.ServiceProvider.GetRequiredService<ICrawler>().CrawlDueSourcesAsync(stoppingToken);
                    if (summary.Sources > 0)
                    {
                        logger.LogInformation("Crawl pass finished: {Summary}", summary);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // Never let one bad pass (database blip, bug) stop the crawler for good.
                    logger.LogError(ex, "Crawl pass failed; trying again at the next tick.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
    }
}
