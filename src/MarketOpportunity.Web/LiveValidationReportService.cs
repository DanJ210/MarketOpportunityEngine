using System.Text.Json;
using MarketOpportunity.Application;

namespace MarketOpportunity.Web;

public sealed class LiveValidationReportService(
    LiveValidationDiagnostics diagnostics,
    IConfiguration configuration,
    TimeProvider clock,
    IHostApplicationLifetime lifetime,
    ILogger<LiveValidationReportService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var duration = configuration.GetValue<int>("MarketData:Diagnostics:DurationSeconds");
        var started = clock.GetTimestamp();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await SaveAsync(stoppingToken);
                if (duration > 0 && clock.GetElapsedTime(started).TotalSeconds >= duration)
                {
                    logger.LogInformation("Bounded live validation reached its duration; requesting normal host shutdown.");
                    lifetime.StopApplication();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await SaveAsync(cancellationToken);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var snapshot = diagnostics.Snapshot();
        logger.LogInformation(
            "Live diagnostic accounting: produced={Produced}, accepted={Accepted}, dequeued={Dequeued}, duplicates={Duplicates}, analyzed={Analyzed}, processingFailures={ProcessingFailures}, persistenceAcknowledged={Persisted}, persistenceFailures={PersistenceFailures}, persistenceFailedEvents={PersistenceFailedEvents}, cancelledWrites={CancelledWrites}, channelDepth={Depth}, remaining={Remaining}, drainStatus={DrainStatus}, drainFailure={DrainFailure}, maxDepth={MaxDepth}, longestWaitMs={WaitMs}",
            snapshot.CanonicalEventsProduced, snapshot.AcceptedEvents, snapshot.DequeuedEvents,
            snapshot.DuplicateEvents, snapshot.EventsAnalyzed, snapshot.ProcessingFailures,
            snapshot.PersistenceAcknowledgedEvents, snapshot.PersistenceFailures,
            snapshot.PersistenceFailedEvents, snapshot.CancelledWrites, snapshot.ChannelDepth,
            snapshot.RemainingEvents, snapshot.ShutdownDrainStatus, snapshot.ShutdownDrainFailure,
            snapshot.MaximumSampledChannelDepth, snapshot.LongestChannelWaitMs);
        var directory = configuration["MarketData:Diagnostics:Directory"];
        if (string.IsNullOrWhiteSpace(directory)) { return; }
        var session = configuration["MarketData:Alpaca:SessionId"]
            ?? throw new InvalidOperationException("Diagnostic export requires a session ID.");
        if (session.Length > 64 || session.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-' && character != '_'))
        {
            throw new InvalidOperationException("Diagnostic export requires a filename-safe session ID.");
        }
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, session + ".diagnostics.json");
        var temporary = target + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(snapshot,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }), cancellationToken);
        File.Move(temporary, target, overwrite: true);
    }
}
