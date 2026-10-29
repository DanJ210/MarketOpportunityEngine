using System.Runtime.InteropServices;
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
    private readonly List<IDisposable> _registrations = [];

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _registrations.Add(lifetime.ApplicationStopping.Register(diagnostics.ShutdownRequested));
        foreach (var signal in new[] { PosixSignal.SIGINT, PosixSignal.SIGQUIT })
        {
            try
            {
                _registrations.Add(PosixSignalRegistration.Create(signal, context => OnSignal(diagnostics, context.Signal)));
            }
            catch (PlatformNotSupportedException)
            {
            }
        }
        return base.StartAsync(cancellationToken);
    }

    // Console Ctrl+C maps to SIGINT and Ctrl+Break to SIGQUIT; the host's console lifetime still performs the stop.
    public static void OnSignal(LiveValidationDiagnostics diagnostics, PosixSignal signal)
    {
        if (signal is PosixSignal.SIGINT or PosixSignal.SIGQUIT)
        {
            diagnostics.RecordShutdownTrigger(ShutdownTrigger.OperatorCancellation);
        }
    }

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
                    diagnostics.RecordShutdownTrigger(ShutdownTrigger.DurationElapsed);
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

    public override void Dispose()
    {
        foreach (var registration in _registrations) { registration.Dispose(); }
        _registrations.Clear();
        base.Dispose();
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
        if (snapshot.ShutdownRequestedUtc is not null)
        {
            logger.LogInformation(
                "Live validation shutdown: trigger={ShutdownTrigger}, requestedUtc={ShutdownRequestedUtc:O}, configuredDurationSeconds={ConfiguredDurationSeconds}",
                snapshot.ShutdownTrigger, snapshot.ShutdownRequestedUtc, snapshot.ConfiguredDurationSeconds);
        }
        var directory = configuration["MarketData:Diagnostics:Directory"];
        if (string.IsNullOrWhiteSpace(directory)) { return; }
        var session = LiveValidationSession.ValidateSessionId(configuration["MarketData:Alpaca:SessionId"]);
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, session + ".diagnostics.json");
        var temporary = target + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(snapshot,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }), cancellationToken);
        File.Move(temporary, target, overwrite: true);
    }
}
