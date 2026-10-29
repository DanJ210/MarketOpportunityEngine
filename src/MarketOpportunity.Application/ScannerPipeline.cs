using System.Threading.Channels;
using MarketOpportunity.Analytics;
using MarketOpportunity.Domain;

namespace MarketOpportunity.Application;

public interface IMarketDataSource
{
    MarketDataCapabilities Capabilities => MarketDataCapabilities.Unknown;

    IAsyncEnumerable<MarketEvent> ReadEventsAsync(CancellationToken cancellationToken);
}

public interface ILiveMarketDataSource : IMarketDataSource
{
}

public interface IReconnectableMarketDataSource : ILiveMarketDataSource
{
}

public sealed record LiveMarketDataRecoveryOptions(int ReconnectDelayMilliseconds = 5_000)
{
    public void Validate()
    {
        if (ReconnectDelayMilliseconds is < 10 or > 300_000)
        {
            throw new InvalidOperationException(
                "MarketData:Recovery:ReconnectDelayMilliseconds must be between 10 and 300000.");
        }
    }

    public TimeSpan ReconnectDelay => TimeSpan.FromMilliseconds(ReconnectDelayMilliseconds);
}

public interface IScannerStore
{
    Task PersistRunAsync(
        ScannerConfiguration configuration,
        IReadOnlyCollection<MarketEvent> marketEvents,
        FeatureSnapshot featureSnapshot,
        Opportunity opportunity,
        CancellationToken cancellationToken);
}

public sealed record ScannerStatus(
    MarketDataHealthStatus FeedStatus,
    string Source,
    int EventsProcessed,
    DateTimeOffset? LatestEventTimeUtc,
    string? Message,
    DateTimeOffset? LatestReceivedTimeUtc = null,
    TimeSpan? ReceiveAge = null,
    MarketDataCapabilities? Capabilities = null,
    bool HasUnresolvedDataGap = false);

public sealed record ScannerAnalysisResult(
    FeatureSnapshot Features,
    Opportunity Opportunity,
    PublicationDecision Publication);

public sealed record OpportunityPublicationReadModel(
    Opportunity Opportunity,
    PublicationDecision Publication);

public sealed record ScannerReadModel(
    ScannerStatus Status,
    IReadOnlyList<ScannerAnalysisResult> Results);

public interface IOpportunityPublicationGate
{
    PublicationDecision Evaluate(
        Opportunity opportunity,
        MarketDataHealthStatus health,
        DateTimeOffset nowUtc,
        bool hasUnresolvedDataGap = false);
}

public sealed class OpportunityPublicationGate : IOpportunityPublicationGate
{
    public PublicationDecision Evaluate(
        Opportunity opportunity,
        MarketDataHealthStatus health,
        DateTimeOffset nowUtc,
        bool hasUnresolvedDataGap = false)
    {
        ArgumentNullException.ThrowIfNull(opportunity);
        var evaluatedAtUtc = nowUtc.ToUniversalTime();
        if (evaluatedAtUtc >= opportunity.ValidUntilUtc)
        {
            return new PublicationDecision(
                OpportunityLifecycleState.Expired,
                "OpportunityExpired",
                "The opportunity validity period has elapsed.",
                evaluatedAtUtc);
        }

        if (hasUnresolvedDataGap)
        {
            return new PublicationDecision(
                OpportunityLifecycleState.Suspended,
                "MarketDataGapUnresolved",
                "A market-data gap remains unresolved; publication is suspended.",
                evaluatedAtUtc);
        }

        var healthVeto = health switch
        {
            MarketDataHealthStatus.Stale => ("MarketDataStale", "Market data is stale; publication is suspended."),
            MarketDataHealthStatus.Offline => ("MarketDataOffline", "The market-data source is offline; publication is suspended."),
            MarketDataHealthStatus.Error => ("MarketDataError", "The market-data source reported an error; publication is suspended."),
            MarketDataHealthStatus.Connecting => ("MarketDataConnecting", "The market-data source is connecting; publication is suspended."),
            _ => ((string Code, string Reason)?)null
        };
        if (healthVeto is { } veto)
        {
            return new PublicationDecision(
                OpportunityLifecycleState.Suspended,
                veto.Code,
                veto.Reason,
                evaluatedAtUtc);
        }

        if (opportunity.Classification == OpportunityClassification.AvoidNoTrade)
        {
            return new PublicationDecision(
                OpportunityLifecycleState.NotQualified,
                "SetupNotQualified",
                "The setup detector classified this candidate as Avoid / No Trade.",
                evaluatedAtUtc);
        }

        return new PublicationDecision(
            OpportunityLifecycleState.Active,
            "PublicationAllowed",
            "Market data is healthy and the candidate remains within its validity period.",
            evaluatedAtUtc);
    }
}

public sealed class ScannerState(
    IOpportunityPublicationGate? publicationGate = null,
    TimeProvider? timeProvider = null)
{
    private readonly object _sync = new();
    private readonly IOpportunityPublicationGate _publicationGate =
        publicationGate ?? new OpportunityPublicationGate();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private IReadOnlyList<ScannerAnalysisResult> _results = [];
    private ScannerStatus _status = new(
        MarketDataHealthStatus.Connecting,
        "Unknown",
        0,
        null,
        null,
        Capabilities: MarketDataCapabilities.Unknown);

    public event Action? Changed;

    public ScannerReadModel Read()
    {
        lock (_sync)
        {
            return new ScannerReadModel(_status, _results);
        }
    }

    public void SetResults(
        ScannerStatus status,
        IReadOnlyList<ScannerAnalysisResult> results,
        DateTimeOffset? evaluationTimeUtc = null)
    {
        var evaluatedAt = evaluationTimeUtc
            ?? (status.FeedStatus == MarketDataHealthStatus.Recorded
                ? results.Select(result => result.Opportunity.AsOfUtc).DefaultIfEmpty(DateTimeOffset.UnixEpoch).Max()
                : _timeProvider.GetUtcNow());
        lock (_sync)
        {
            _status = status;
            _results = ApplyPublication(status, results, evaluatedAt);
        }

        Changed?.Invoke();
    }

    public void SetStatus(ScannerStatus status, DateTimeOffset? evaluationTimeUtc = null)
    {
        lock (_sync)
        {
            _status = status;
            _results = ApplyPublication(
                status,
                _results,
                evaluationTimeUtc ?? _timeProvider.GetUtcNow());
        }

        Changed?.Invoke();
    }

    private IReadOnlyList<ScannerAnalysisResult> ApplyPublication(
        ScannerStatus status,
        IReadOnlyList<ScannerAnalysisResult> results,
        DateTimeOffset evaluatedAtUtc) =>
        results.Select(result => result with
        {
            Publication = _publicationGate.Evaluate(
                result.Opportunity,
                status.FeedStatus,
                evaluatedAtUtc,
                status.HasUnresolvedDataGap)
        }).ToArray();
}

public sealed class ScannerPipeline(
    IMarketDataSource source,
    IScannerStore store,
    ScannerState state,
    ScannerConfiguration configuration,
    FeatureCalculator featureCalculator,
    MomentumContinuationDetector detector,
    TimeProvider timeProvider,
    LiveMarketDataRecoveryOptions? recoveryOptions = null,
    Action<string, string>? logReconnectFailure = null,
    LiveValidationDiagnostics? diagnostics = null,
    LivePipelineOptions? livePipelineOptions = null)
{
    private static readonly TimeSpan LiveAnalysisInterval = TimeSpan.FromSeconds(5);
    private readonly LiveMarketDataRecoveryOptions _recoveryOptions =
        recoveryOptions ?? new LiveMarketDataRecoveryOptions();
    private readonly LivePipelineOptions _livePipelineOptions =
        livePipelineOptions ?? new LivePipelineOptions();

    public Task RunAsync(CancellationToken cancellationToken) =>
        source is ILiveMarketDataSource
            ? RunLiveAsync(cancellationToken)
            : RunFixtureAsync(cancellationToken);

    public async Task RunFixtureAsync(CancellationToken cancellationToken)
    {
        var states = new Dictionary<(string Symbol, string SessionId), SymbolEventState>();
        var eventsByState = new Dictionary<(string Symbol, string SessionId), List<MarketEvent>>();

        await foreach (var marketEvent in source.ReadEventsAsync(cancellationToken))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(marketEvent.Symbol);
            ArgumentException.ThrowIfNullOrWhiteSpace(marketEvent.SessionId);
            ArgumentException.ThrowIfNullOrWhiteSpace(marketEvent.Provider);
            var key = (marketEvent.Symbol, marketEvent.SessionId);
            if (!states.TryGetValue(key, out var symbolState))
            {
                symbolState = new SymbolEventState(
                    marketEvent.Symbol,
                    marketEvent.SessionId,
                    configuration.MaximumStateEvents);
                states.Add(key, symbolState);
                eventsByState.Add(key, []);
            }

            if (symbolState.Add(marketEvent))
            {
                eventsByState[key].Add(marketEvent);
            }
        }

        if (states.Count == 0)
        {
            throw new InvalidOperationException("The recorded market-data fixture contains no events.");
        }

        var results = new List<ScannerAnalysisResult>(states.Count);
        var eventCount = 0;
        foreach (var (key, symbolState) in states
                     .OrderBy(item => item.Key.SessionId, StringComparer.Ordinal)
                     .ThenBy(item => item.Key.Symbol, StringComparer.Ordinal))
        {
            var stateEvents = eventsByState[key];
            eventCount += stateEvents.Count;
            var asOfUtc = stateEvents.Max(marketEvent => marketEvent.EventTimeUtc).ToUniversalTime();
            var snapshot = featureCalculator.Calculate(
                symbolState,
                asOfUtc,
                configuration,
                source.Capabilities);
            var opportunity = detector.Evaluate(snapshot, configuration);
            await store.PersistRunAsync(configuration, stateEvents, snapshot, opportunity, cancellationToken);
            results.Add(new ScannerAnalysisResult(
                snapshot,
                opportunity,
                new PublicationDecision(
                    OpportunityLifecycleState.NotQualified,
                    "PendingPublication",
                    "Publication status has not been evaluated.",
                    asOfUtc)));
        }

        state.SetResults(
            new ScannerStatus(
                MarketDataHealthStatus.Recorded,
                source.Capabilities.Provider,
                eventCount,
                results.Max(result => result.Features.AsOfUtc).ToUniversalTime(),
                null,
                Capabilities: source.Capabilities),
            results);
    }

    private async Task RunLiveAsync(CancellationToken cancellationToken)
    {
        if (source is IReconnectableMarketDataSource)
        {
            _recoveryOptions.Validate();
        }
        _livePipelineOptions.Validate();
        var channel = Channel.CreateBounded<MarketEvent>(new BoundedChannelOptions(5_000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });
        var statusSync = new object();
        var connectionState = (int)MarketDataHealthStatus.Connecting;
        var unresolvedDataGap = 0;
        var states = new Dictionary<(string Symbol, string SessionId), SymbolEventState>();
        var pendingEvents = new Dictionary<(string Symbol, string SessionId), List<MarketEvent>>();
        var lastAnalysisAt = new Dictionary<(string Symbol, string SessionId), DateTimeOffset>();
        var results = new Dictionary<(string Symbol, string SessionId), ScannerAnalysisResult>();
        var eventCount = 0;
        long producedEvents = 0;
        long acceptedEvents = 0;
        long dequeuedEvents = 0;
        long duplicateEvents = 0;
        long analyzedEvents = 0;
        long processingFailures = 0;
        long persistenceAcknowledgedEvents = 0;
        long persistenceFailures = 0;
        long cancelledWrites = 0;
        var latestEventTime = (DateTimeOffset?)null;
        var latestReceivedTime = (DateTimeOffset?)null;
        var connectedAt = timeProvider.GetUtcNow();
        state.SetResults(
            new ScannerStatus(
                MarketDataHealthStatus.Connecting,
                source.Capabilities.Provider,
                0,
                null,
                null,
                Capabilities: source.Capabilities),
            []);
        using var consumerCancellation = new CancellationTokenSource();
        using var persistenceCancellation = new CancellationTokenSource();
        using var producerCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var producer = PumpLiveEventsAsync(
            channel.Writer,
            () => channel.Reader.Count,
            status =>
            {
                lock (statusSync)
                {
                    Volatile.Write(ref connectionState, (int)status);
                    var current = state.Read().Status;
                    state.SetStatus(current with
                    {
                        FeedStatus = status,
                        Message = status switch
                        {
                            MarketDataHealthStatus.Offline =>
                                Volatile.Read(ref unresolvedDataGap) != 0
                                    ? "The market-data stream disconnected; reconnecting. The data gap is unresolved."
                                    : "The market-data connection failed; retrying.",
                            MarketDataHealthStatus.Connecting when Volatile.Read(ref unresolvedDataGap) != 0 =>
                                "Reconnecting; waiting for valid market data. The data gap remains unresolved.",
                            _ when Volatile.Read(ref unresolvedDataGap) != 0 =>
                                "Market data resumed, but the disconnect gap remains unresolved; no backfill was performed.",
                            _ => null
                        },
                        HasUnresolvedDataGap = Volatile.Read(ref unresolvedDataGap) != 0
                    });
                }
            },
            () => Interlocked.Exchange(ref unresolvedDataGap, 1),
            producerCancellation.Token,
            () => Interlocked.Increment(ref producedEvents),
            depth => Interlocked.Increment(ref acceptedEvents),
            () => Interlocked.Increment(ref cancelledWrites));
        var shutdownRequested = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var shutdownRegistration = cancellationToken.Register(
            () => shutdownRequested.TrySetResult());
        var consumer = ConsumeChannelAsync();
        try
        {
            var completed = await Task.WhenAny(consumer, shutdownRequested.Task);
            var draining = completed == shutdownRequested.Task || cancellationToken.IsCancellationRequested;
            if (draining)
            {
                diagnostics?.ShutdownDrainStarted();
                try
                {
                    await consumer.WaitAsync(
                        _livePipelineOptions.GracefulShutdownTimeout,
                        timeProvider);
                }
                catch (TimeoutException)
                {
                    if (consumer.IsCompleted)
                    {
                        await consumer;
                    }

                    diagnostics?.ShutdownDrainTimedOut();
                    producerCancellation.Cancel();
                    consumerCancellation.Cancel();
                    persistenceCancellation.Cancel();
                    _ = consumer.ContinueWith(
                        task => _ = task.Exception,
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                    var message = CreateDrainAccountingMessage(
                        "Graceful scanner shutdown timed out");
                    state.SetStatus(CreateLiveStatus(
                        MarketDataHealthStatus.Error,
                        timeProvider.GetUtcNow(),
                        latestReceivedTime,
                        message));
                    throw new TimeoutException(message);
                }

                var diagnosticsDrainCompleted = diagnostics?.ShutdownDrainCompleted() ?? true;
                if (acceptedEvents != dequeuedEvents
                    || acceptedEvents != duplicateEvents + persistenceAcknowledgedEvents
                    || persistenceFailures != 0
                    || processingFailures != 0
                    || channel.Reader.Count != 0
                    || !diagnosticsDrainCompleted)
                {
                    var message = CreateDrainAccountingMessage(
                        "Graceful scanner shutdown could not reconcile accepted work");
                    diagnostics?.ShutdownDrainFailed(message);
                    state.SetStatus(CreateLiveStatus(
                        MarketDataHealthStatus.Error,
                        timeProvider.GetUtcNow(),
                        latestReceivedTime,
                        message));
                    throw new InvalidOperationException(message);
                }
            }

            await consumer;
            await producer;
            if (!draining)
            {
                state.SetStatus(CreateLiveStatus(
                    MarketDataHealthStatus.Offline,
                    timeProvider.GetUtcNow(),
                    latestReceivedTime,
                    "The market-data stream disconnected."));
            }
        }
        catch (Exception exception)
        {
            producerCancellation.Cancel();
            consumerCancellation.Cancel();
            if (shutdownRequested.Task.IsCompleted
                && exception is not TimeoutException)
            {
                var message = CreateDrainAccountingMessage(
                    $"Graceful scanner shutdown failed ({exception.GetType().Name})");
                diagnostics?.ShutdownDrainFailed(message);
                state.SetStatus(CreateLiveStatus(
                    MarketDataHealthStatus.Error,
                    timeProvider.GetUtcNow(),
                    latestReceivedTime,
                    message));
                throw new InvalidOperationException(message, exception);
            }

            if (exception is not TimeoutException)
            {
                state.SetStatus(CreateLiveStatus(
                    MarketDataHealthStatus.Error,
                    timeProvider.GetUtcNow(),
                    latestReceivedTime,
                    "The market-data stream failed; publication is suspended."));
            }

            channel.Writer.TryComplete(exception);
            throw;
        }
        finally
        {
            consumerCancellation.Cancel();
            channel.Writer.TryComplete();
        }

        async Task ConsumeChannelAsync()
        {
            using var healthTimer = new PeriodicTimer(TimeSpan.FromSeconds(1), timeProvider);
            var waitToReadTask = channel.Reader.WaitToReadAsync(consumerCancellation.Token).AsTask();
            var healthTickTask = healthTimer.WaitForNextTickAsync(consumerCancellation.Token).AsTask();
            while (true)
            {
                var completedTask = await Task.WhenAny(waitToReadTask, healthTickTask);
                if (completedTask == healthTickTask)
                {
                    if (!await healthTickTask)
                    {
                        break;
                    }

                    SetLiveStatus(latestReceivedTime);
                    healthTickTask = healthTimer.WaitForNextTickAsync(consumerCancellation.Token).AsTask();
                    continue;
                }

                bool hasData;
                try
                {
                    hasData = await waitToReadTask;
                }
                catch (ChannelClosedException)
                {
                    break;
                }

                if (!hasData)
                {
                    break;
                }

                while (channel.Reader.TryRead(out var marketEvent))
                {
                    ValidateEvent(marketEvent);
                    Interlocked.Increment(ref dequeuedEvents);
                    latestReceivedTime = latestReceivedTime is null
                        || marketEvent.ReceivedTimeUtc > latestReceivedTime
                            ? marketEvent.ReceivedTimeUtc.ToUniversalTime()
                            : latestReceivedTime;
                    var key = (marketEvent.Symbol, marketEvent.SessionId);
                    if (!states.TryGetValue(key, out var symbolState))
                    {
                        symbolState = new SymbolEventState(
                            marketEvent.Symbol,
                            marketEvent.SessionId,
                            configuration.MaximumStateEvents);
                        states.Add(key, symbolState);
                        pendingEvents.Add(key, []);
                    }

                    if (!symbolState.Add(marketEvent))
                    {
                        Interlocked.Increment(ref duplicateEvents);
                        diagnostics?.Dequeued(channel.Reader.Count, duplicate: true);
                        continue;
                    }

                    diagnostics?.Dequeued(channel.Reader.Count, duplicate: false);
                    pendingEvents[key].Add(marketEvent);
                    eventCount++;
                    latestEventTime = latestEventTime is null || marketEvent.EventTimeUtc > latestEventTime
                        ? marketEvent.EventTimeUtc
                        : latestEventTime;
                }

                await AnalyzePendingAsync(force: false);
                waitToReadTask = channel.Reader.WaitToReadAsync(consumerCancellation.Token).AsTask();
            }

            await AnalyzePendingAsync(force: true);
            state.SetStatus(CreateLiveStatus(
                MarketDataHealthStatus.Offline,
                timeProvider.GetUtcNow(),
                latestReceivedTime,
                cancellationToken.IsCancellationRequested
                    ? "The market-data stream stopped."
                    : "The market-data stream disconnected."));
        }

        async Task AnalyzePendingAsync(bool force)
        {
            var now = timeProvider.GetUtcNow();
            foreach (var (key, symbolState) in states)
            {
                var pending = pendingEvents[key];
                if (pending.Count == 0
                    || !force
                        && lastAnalysisAt.TryGetValue(key, out var last)
                        && now - last < LiveAnalysisInterval)
                {
                    continue;
                }

                var batch = pending.ToArray();
                Interlocked.Add(ref analyzedEvents, batch.Length);
                var asOfUtc = symbolState.GetEventsAsOf(DateTimeOffset.MaxValue)
                    .Max(item => item.EventTimeUtc)
                    .ToUniversalTime();
                var analysisStart = diagnostics?.AnalysisStarted(batch) ?? 0;
                FeatureSnapshot snapshot;
                Opportunity opportunity;
                try
                {
                    snapshot = featureCalculator.Calculate(
                        symbolState,
                        asOfUtc,
                        configuration,
                        source.Capabilities);
                    opportunity = detector.Evaluate(snapshot, configuration);
                }
                catch
                {
                    Interlocked.Increment(ref processingFailures);
                    diagnostics?.ProcessingFailed();
                    throw;
                }

                var persistenceStart = timeProvider.GetTimestamp();
                try
                {
                    await store.PersistRunAsync(
                        configuration,
                        batch,
                        snapshot,
                        opportunity,
                        persistenceCancellation.Token);
                }
                catch (OperationCanceledException) when (persistenceCancellation.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    Interlocked.Increment(ref persistenceFailures);
                    diagnostics?.PersistenceFailed(batch.Length);
                    throw;
                }

                if (persistenceCancellation.IsCancellationRequested)
                {
                    throw new OperationCanceledException(persistenceCancellation.Token);
                }

                Interlocked.Add(ref persistenceAcknowledgedEvents, batch.Length);
                diagnostics?.PersistenceCompleted(analysisStart, persistenceStart, batch.Length);
                pending.Clear();
                lastAnalysisAt[key] = now;
                results[key] = new ScannerAnalysisResult(
                    snapshot,
                    opportunity,
                    new PublicationDecision(
                        OpportunityLifecycleState.NotQualified,
                        "PendingPublication",
                        "Publication status has not been evaluated.",
                        asOfUtc));
            }

            lock (statusSync)
            {
                state.SetResults(
                    CreateLiveStatus(null, now, latestReceivedTime, null),
                    results.Values
                        .OrderBy(item => item.Features.SessionId, StringComparer.Ordinal)
                        .ThenBy(item => item.Features.Symbol, StringComparer.Ordinal)
                        .ToArray());
            }
        }

        string CreateDrainAccountingMessage(string prefix)
        {
            var remaining = Math.Max(0, acceptedEvents - duplicateEvents - persistenceAcknowledgedEvents);
            return $"{prefix}. Produced={producedEvents}; Accepted={acceptedEvents}; " +
                $"Dequeued={dequeuedEvents}; Duplicate={duplicateEvents}; " +
                $"Analyzed={analyzedEvents}; ProcessingFailures={processingFailures}; " +
                $"PersistenceAcknowledged={persistenceAcknowledgedEvents}; " +
                $"PersistenceFailures={persistenceFailures}; CancelledWrites={cancelledWrites}; " +
                $"ChannelDepth={channel.Reader.Count}; Remaining={remaining}.";
        }

        void SetLiveStatus(DateTimeOffset? latestReceivedAt)
        {
            var now = timeProvider.GetUtcNow();
            lock (statusSync)
            {
                state.SetResults(
                    CreateLiveStatus(null, now, latestReceivedAt, null),
                    results.Values
                        .OrderBy(item => item.Features.SessionId, StringComparer.Ordinal)
                        .ThenBy(item => item.Features.Symbol, StringComparer.Ordinal)
                        .ToArray());
            }
        }

        ScannerStatus CreateLiveStatus(
            MarketDataHealthStatus? forcedStatus,
            DateTimeOffset now,
            DateTimeOffset? latestReceivedAt,
            string? message)
        {
            TimeSpan? receiveAge = latestReceivedAt is null
                ? now - connectedAt
                : TimeSpan.FromTicks(Math.Max(0, (now - latestReceivedAt.Value).Ticks));
            var currentConnectionStatus = (MarketDataHealthStatus)Volatile.Read(ref connectionState);
            var feedStatus = forcedStatus ?? (currentConnectionStatus == MarketDataHealthStatus.Live
                && receiveAge >= TimeSpan.FromSeconds(configuration.MaximumLiveFeedStalenessSeconds)
                    ? MarketDataHealthStatus.Stale
                    : currentConnectionStatus);
            var statusMessage = message ?? (Volatile.Read(ref unresolvedDataGap) == 0
                ? null
                : "The disconnect gap remains unresolved; no backfill was performed.");

            return new ScannerStatus(
                feedStatus,
                source.Capabilities.Provider,
                eventCount,
                latestEventTime,
                statusMessage,
                latestReceivedAt,
                receiveAge,
                source.Capabilities,
                Volatile.Read(ref unresolvedDataGap) != 0);
        }
    }

    private async Task PumpLiveEventsAsync(
        ChannelWriter<MarketEvent> writer,
        Func<int> channelDepth,
        Action<MarketDataHealthStatus> setConnectionStatus,
        Action markDataGapUnresolved,
        CancellationToken cancellationToken,
        Action recordProduced,
        Action<int> recordAccepted,
        Action recordCancelledWrite)
    {
        Exception? failure = null;
        var hasReceivedMarketData = false;
        try
        {
            do
            {
                try
                {
                    await foreach (var marketEvent in source.ReadEventsAsync(cancellationToken)
                                       .WithCancellation(cancellationToken))
                    {
                        recordProduced();
                        cancellationToken.ThrowIfCancellationRequested();
                        ValidateEvent(marketEvent);
                        hasReceivedMarketData = true;
                        setConnectionStatus(MarketDataHealthStatus.Live);
                        var writeStarted = timeProvider.GetTimestamp();
                        var write = writer.WriteAsync(marketEvent, cancellationToken);
                        var waited = !write.IsCompletedSuccessfully;
                        try
                        {
                            await write;
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            diagnostics?.WriteCompleted(writeStarted, waited, cancelled: true);
                            recordCancelledWrite();
                            throw;
                        }
                        var depth = channelDepth();
                        recordAccepted(depth);
                        diagnostics?.Accepted(depth);
                        diagnostics?.WriteCompleted(writeStarted, waited, cancelled: false);
                    }

                    if (source is not IReconnectableMarketDataSource)
                    {
                        break;
                    }

                    throw new IOException("The live market-data stream ended unexpectedly.");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (source is IReconnectableMarketDataSource)
                {
                    failure = exception;
                    if (hasReceivedMarketData)
                    {
                        markDataGapUnresolved();
                    }
                    setConnectionStatus(MarketDataHealthStatus.Offline);
                    logReconnectFailure?.Invoke(
                        source.Capabilities.Provider,
                        exception.GetType().Name);
                    await Task.Delay(_recoveryOptions.ReconnectDelay, cancellationToken);
                    setConnectionStatus(MarketDataHealthStatus.Connecting);
                    failure = null;
                }
            }
            while (source is IReconnectableMarketDataSource);
        }
        catch (Exception exception)
        {
            if (exception is OperationCanceledException
                && cancellationToken.IsCancellationRequested)
            {
                failure = null;
            }
            else
            {
                failure = exception;
                throw;
            }
        }
        finally
        {
            writer.TryComplete(failure);
        }
    }

    private static void ValidateEvent(MarketEvent marketEvent)
    {
        ArgumentNullException.ThrowIfNull(marketEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(marketEvent.Symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(marketEvent.SessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(marketEvent.Provider);
    }
}
