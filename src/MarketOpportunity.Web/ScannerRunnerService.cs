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
            // Restore persisted active tracked candidates before scanning so readiness is
            // recomputed for the same pinned PlanId from current trustworthy market state.
            // Prior readiness states are never restored blindly.
            var tracker = scope.ServiceProvider.GetService<PreTradeReadinessTracker>();
            var readinessStore = scope.ServiceProvider.GetService<IPreTradeReadinessStore>();
            if (tracker is not null && readinessStore is not null)
            {
                tracker.Restore(await readinessStore.RestoreActiveAsync(stoppingToken));
            }

            await pipeline.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            scope.ServiceProvider.GetService<LiveValidationDiagnostics>()
                ?.RecordShutdownTrigger(ShutdownTrigger.ApplicationFailure);
            var scannerState = scope.ServiceProvider.GetRequiredService<ScannerState>();
            var currentStatus = scannerState.Read().Status;
            scannerState.SetStatus(currentStatus with
            {
                FeedStatus = MarketDataHealthStatus.Error,
                Message = currentStatus.Message ?? "Market-data processing stopped unexpectedly."
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
