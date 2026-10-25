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
    MarketDataCapabilities? Capabilities = null);

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
        DateTimeOffset nowUtc);
}

public sealed class OpportunityPublicationGate : IOpportunityPublicationGate
{
    public PublicationDecision Evaluate(
        Opportunity opportunity,
        MarketDataHealthStatus health,
        DateTimeOffset nowUtc)
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
                evaluatedAtUtc)
        }).ToArray();
}

public sealed class ScannerPipeline(
    IMarketDataSource source,
    IScannerStore store,
    ScannerState state,
    ScannerConfiguration configuration,
    FeatureCalculator featureCalculator,
    MomentumContinuationDetector detector,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan LiveAnalysisInterval = TimeSpan.FromSeconds(5);

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
        var channel = Channel.CreateBounded<MarketEvent>(new BoundedChannelOptions(5_000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });
        var producer = PumpLiveEventsAsync(channel.Writer, cancellationToken);
        var states = new Dictionary<(string Symbol, string SessionId), SymbolEventState>();
        var pendingEvents = new Dictionary<(string Symbol, string SessionId), List<MarketEvent>>();
        var lastAnalysisAt = new Dictionary<(string Symbol, string SessionId), DateTimeOffset>();
        var results = new Dictionary<(string Symbol, string SessionId), ScannerAnalysisResult>();
        var eventCount = 0;
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

        try
        {
            using var healthTimer = new PeriodicTimer(TimeSpan.FromSeconds(1), timeProvider);
            var waitToReadTask = channel.Reader.WaitToReadAsync(cancellationToken).AsTask();
            var healthTickTask = healthTimer.WaitForNextTickAsync(cancellationToken).AsTask();
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
                    healthTickTask = healthTimer.WaitForNextTickAsync(cancellationToken).AsTask();
                    continue;
                }

                if (!await waitToReadTask)
                {
                    break;
                }

                while (channel.Reader.TryRead(out var marketEvent))
                {
                    ValidateEvent(marketEvent);
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
                        continue;
                    }

                    pendingEvents[key].Add(marketEvent);
                    eventCount++;
                    latestEventTime = latestEventTime is null || marketEvent.EventTimeUtc > latestEventTime
                        ? marketEvent.EventTimeUtc
                        : latestEventTime;
                }

                var now = timeProvider.GetUtcNow();
                foreach (var (key, symbolState) in states)
                {
                    if (pendingEvents[key].Count == 0
                        || lastAnalysisAt.TryGetValue(key, out var last)
                            && now - last < LiveAnalysisInterval)
                    {
                        continue;
                    }

                    var asOfUtc = symbolState.GetEventsAsOf(DateTimeOffset.MaxValue)
                        .Max(item => item.EventTimeUtc)
                        .ToUniversalTime();
                    var snapshot = featureCalculator.Calculate(
                        symbolState,
                        asOfUtc,
                        configuration,
                        source.Capabilities);
                    var opportunity = detector.Evaluate(snapshot, configuration);
                    await store.PersistRunAsync(
                        configuration,
                        pendingEvents[key].ToArray(),
                        snapshot,
                        opportunity,
                        cancellationToken);
                    pendingEvents[key].Clear();
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

                state.SetResults(
                    CreateLiveStatus(null, now, latestReceivedTime, null),
                    results.Values
                        .OrderBy(item => item.Features.SessionId, StringComparer.Ordinal)
                        .ThenBy(item => item.Features.Symbol, StringComparer.Ordinal)
                        .ToArray());
                waitToReadTask = channel.Reader.WaitToReadAsync(cancellationToken).AsTask();
            }

            state.SetStatus(CreateLiveStatus(
                MarketDataHealthStatus.Offline,
                timeProvider.GetUtcNow(),
                latestReceivedTime,
                "The market-data stream disconnected."));
            await producer;
            return;
        }
        catch (ChannelClosedException)
        {
            await producer;
            throw new InvalidOperationException("The live market-data source ended unexpectedly.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            state.SetStatus(CreateLiveStatus(
                MarketDataHealthStatus.Offline,
                timeProvider.GetUtcNow(),
                latestReceivedTime,
                "The market-data stream stopped."));
            try
            {
                await producer;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }

            throw;
        }
        catch (Exception)
        {
            state.SetStatus(CreateLiveStatus(
                MarketDataHealthStatus.Error,
                timeProvider.GetUtcNow(),
                latestReceivedTime,
                "The market-data stream failed; publication is suspended."));
            throw;
        }
        finally
        {
            channel.Writer.TryComplete();
        }

        void SetLiveStatus(DateTimeOffset? latestReceivedAt)
        {
            var now = timeProvider.GetUtcNow();
            state.SetResults(
                CreateLiveStatus(null, now, latestReceivedAt, null),
                results.Values
                    .OrderBy(item => item.Features.SessionId, StringComparer.Ordinal)
                    .ThenBy(item => item.Features.Symbol, StringComparer.Ordinal)
                    .ToArray());
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
            var feedStatus = forcedStatus ?? (receiveAge >= TimeSpan.FromSeconds(
                    configuration.MaximumLiveFeedStalenessSeconds)
                ? MarketDataHealthStatus.Stale
                : latestReceivedAt is null
                    ? MarketDataHealthStatus.Connecting
                    : MarketDataHealthStatus.Live);

            return new ScannerStatus(
                feedStatus,
                source.Capabilities.Provider,
                eventCount,
                latestEventTime,
                message,
                latestReceivedAt,
                receiveAge,
                source.Capabilities);
        }
    }

    private async Task PumpLiveEventsAsync(
        ChannelWriter<MarketEvent> writer,
        CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            await foreach (var marketEvent in source.ReadEventsAsync(cancellationToken)
                               .WithCancellation(cancellationToken))
            {
                await writer.WriteAsync(marketEvent, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
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
