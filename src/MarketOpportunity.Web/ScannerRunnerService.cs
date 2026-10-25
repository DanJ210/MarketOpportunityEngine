using MarketOpportunity.Application;
using MarketOpportunity.Domain;

namespace MarketOpportunity.Web;

public sealed class ScannerRunnerService(
    IServiceScopeFactory scopeFactory,
    ILogger<ScannerRunnerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<ScannerPipeline>();
        try
        {
            await pipeline.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var scannerState = scope.ServiceProvider.GetRequiredService<ScannerState>();
            var currentStatus = scannerState.Read().Status;
            scannerState.SetStatus(currentStatus with
            {
                FeedStatus = MarketDataHealthStatus.Error,
                Message = "Market-data processing stopped unexpectedly."
            });
            logger.LogError(exception, "Market-data processing stopped unexpectedly.");
            throw;
        }

        var result = scope.ServiceProvider.GetRequiredService<ScannerState>().Read();
        logger.LogInformation(
            "Processed {EventCount} {Source} events and persisted {AnalysisCount} symbol/session analyses; latest AsOfUtc={AsOfUtc}",
            result.Status.EventsProcessed,
            result.Status.Source,
            result.Results.Count,
            result.Status.LatestEventTimeUtc);
    }
}
