using MarketOpportunity.Domain;
using MarketOpportunity.Analytics;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MarketOpportunity.Application;

public sealed record MarketSessionCalendarEntry(
    MarketSessionDefinition CurrentSession,
    MarketSessionDefinition? PreviousSession);

public sealed record MarketSessionCalendarResolution(
    MarketSessionDefinition? CurrentSession,
    MarketSessionDefinition? PreviousSession,
    string CalendarId,
    string? Message,
    MarketSessionCalendarStatus Status = MarketSessionCalendarStatus.Success);

public interface IMarketSessionCalendar
{
    Task<MarketSessionCalendarResolution> ResolvePreviousSessionAsync(
        string symbol,
        string currentSessionId,
        DateTimeOffset currentEventTimeUtc,
        CancellationToken cancellationToken);
}

public sealed class ExplicitMarketSessionCalendar : IMarketSessionCalendar
{
    private readonly string _calendarId;
    private readonly IReadOnlyDictionary<string, MarketSessionCalendarEntry> _entries;

    public ExplicitMarketSessionCalendar(
        string calendarId,
        IEnumerable<MarketSessionCalendarEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(calendarId);
        ArgumentNullException.ThrowIfNull(entries);
        var entryArray = entries.ToArray();
        foreach (var entry in entryArray)
        {
            ArgumentNullException.ThrowIfNull(entry.CurrentSession);
            ValidateSession(entry.CurrentSession);
            if (entry.PreviousSession is { } previous)
            {
                ValidateSession(previous);
                if (previous.SessionId == entry.CurrentSession.SessionId
                    || previous.CloseAtUtc > entry.CurrentSession.OpenAtUtc)
                {
                    throw new ArgumentException(
                        "A configured previous session must precede and differ from its current session.",
                        nameof(entries));
                }
            }
        }
        _calendarId = calendarId;
        _entries = entryArray.ToDictionary(
            entry => entry.CurrentSession.SessionId,
            StringComparer.Ordinal);
    }

    public Task<MarketSessionCalendarResolution> ResolvePreviousSessionAsync(
        string symbol,
        string currentSessionId,
        DateTimeOffset currentEventTimeUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentSessionId);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_entries.TryGetValue(currentSessionId, out var entry))
        {
            return Task.FromResult(new MarketSessionCalendarResolution(
                null,
                null,
                _calendarId,
                $"The explicit calendar has no entry for session '{currentSessionId}'.",
                MarketSessionCalendarStatus.InvalidReference));
        }

        if (currentEventTimeUtc < entry.CurrentSession.OpenAtUtc
            || currentEventTimeUtc > entry.CurrentSession.CloseAtUtc)
        {
            return Task.FromResult(new MarketSessionCalendarResolution(
                entry.CurrentSession,
                null,
                _calendarId,
                "The event time is outside the configured exchange-local session boundaries.",
                MarketSessionCalendarStatus.InvalidReference));
        }

        return Task.FromResult(new MarketSessionCalendarResolution(
            entry.CurrentSession,
            entry.PreviousSession,
            _calendarId,
            entry.PreviousSession is null
                ? "No immediately preceding eligible session is configured."
                : null,
            entry.PreviousSession is null
                ? MarketSessionCalendarStatus.NoPreviousSession
                : MarketSessionCalendarStatus.Success));
    }

    private static void ValidateSession(MarketSessionDefinition session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(session.SessionId);
        if (session.OpenAtUtc >= session.CloseAtUtc)
        {
            throw new ArgumentException("Session boundaries must have a positive UTC interval.");
        }
    }
}

public interface IHistoricalMarketDataSource
{
    Task<HistoricalMarketSessionData?> ReadSessionAsync(
        string symbol,
        MarketSession session,
        CancellationToken cancellationToken);
}

public enum PriorSessionWarmupStatus
{
    Pending,
    CompleteUsable,
    Partial,
    Unavailable,
    Invalid,
    Cancelled,
    AuthenticationFailure,
    EntitlementFailure,
    LimitExceeded,
    TimedOut,
    ProviderFailure,
    ReconstructionFailure
}

public sealed record PriorSessionWarmupScope(
    string Symbol,
    string CurrentSessionId,
    string LiveProvider,
    string LiveFeed,
    string? PriorSessionId,
    string HistoricalProvider,
    string HistoricalFeed,
    string HistoricalSourceKind,
    string PriorContextDefinitionVersion,
    string FeatureSetVersion,
    string ScannerConfigurationVersion);

public sealed record PriorSessionWarmupOutcome(
    PriorSessionWarmupScope Scope,
    PriorSessionWarmupStatus Status,
    string Reason,
    DateOnly? PriorSessionTradingDate = null,
    DateTimeOffset? PriorSessionOpenAtUtc = null,
    DateTimeOffset? PriorSessionCloseAtUtc = null,
    string? CalendarId = null,
    MarketSessionCalendarStatus? CalendarStatus = null,
    HistoricalRetrievalStatus? RetrievalStatus = null,
    bool? PaginationExhausted = null,
    int? PagesRetrieved = null,
    int? DuplicateRecordsIgnored = null,
    PriorSessionContextStatus? ContextCompleteness = null);

public sealed record PriorSessionWarmupResult(
    PriorSessionContext Context,
    PriorSessionWarmupOutcome Outcome);

public sealed record PriorSessionWarmupOptions(
    int TimeoutSeconds = 120,
    int MaximumConcurrentWarmups = 4)
{
    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);

    public void Validate()
    {
        if (TimeoutSeconds is < 1 or > 300)
        {
            throw new InvalidOperationException(
                "Operations:PriorSessionWarmup:TimeoutSeconds must be between 1 and 300.");
        }

        if (MaximumConcurrentWarmups is < 1 or > 32)
        {
            throw new InvalidOperationException(
                "Operations:PriorSessionWarmup:MaximumConcurrentWarmups must be between 1 and 32.");
        }
    }
}

public interface IPriorSessionContextProvider
{
    Task<PriorSessionContextView> GetContextAsync(
        string symbol,
        string currentSessionId,
        DateTimeOffset currentEventTimeUtc,
        decimal? currentReferencePrice,
        CancellationToken cancellationToken);

    async Task<PriorSessionWarmupResult> WarmupAsync(
        string symbol,
        string currentSessionId,
        DateTimeOffset currentEventTimeUtc,
        MarketDataCapabilities currentMarketData,
        CancellationToken cancellationToken)
    {
        var context = await GetContextAsync(
            symbol,
            currentSessionId,
            currentEventTimeUtc,
            currentReferencePrice: null,
            cancellationToken);
        var status = context.Context.Completeness switch
        {
            PriorSessionContextStatus.Available => PriorSessionWarmupStatus.CompleteUsable,
            PriorSessionContextStatus.Partial => PriorSessionWarmupStatus.Partial,
            PriorSessionContextStatus.Invalid => PriorSessionWarmupStatus.Invalid,
            _ => PriorSessionWarmupStatus.Unavailable
        };
        var scope = PriorSessionWarmupScopeFactory.Create(
            symbol,
            currentSessionId,
            currentMarketData,
            context.Context.PriorSessionId,
            context.Context.ContextDefinitionVersion,
            featureSetVersion: null,
            scannerConfigurationVersion: null);
        return new PriorSessionWarmupResult(
            context.Context,
            new PriorSessionWarmupOutcome(
                scope,
                status,
                context.Context.CompletenessReason,
                PriorSessionCloseAtUtc: context.Context.PriorSessionEndUtc,
                ContextCompleteness: context.Context.Completeness));
    }
}

public sealed class PriorSessionContextProvider(
    IMarketSessionCalendar? calendar,
    IHistoricalMarketDataSource? historicalSource,
    PriorSessionContextBuilder builder,
    ScannerConfiguration configuration,
    PriorSessionHistoricalReconstructor? reconstructor = null,
    PriorSessionWarmupOptions? warmupOptions = null,
    TimeProvider? timeProvider = null,
    ILogger<PriorSessionContextProvider>? logger = null) : IPriorSessionContextProvider
{
    private readonly PriorSessionWarmupOptions _warmupOptions =
        warmupOptions ?? new PriorSessionWarmupOptions();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<WarmupRequestKey, Lazy<Task<PriorSessionWarmupResult>>> _warmups = new();
    private readonly ConcurrentDictionary<WarmupRequestKey, CancellationTokenSource> _scopeCancellations = new();
    private readonly ConcurrentDictionary<string, string> _activeSessions = new(
        StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _concurrency =
        new(
            (warmupOptions ?? new PriorSessionWarmupOptions()).MaximumConcurrentWarmups,
            (warmupOptions ?? new PriorSessionWarmupOptions()).MaximumConcurrentWarmups);

    public async Task<PriorSessionContextView> GetContextAsync(
        string symbol,
        string currentSessionId,
        DateTimeOffset currentEventTimeUtc,
        decimal? currentReferencePrice,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentSessionId);
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);
        var result = await WarmupAsync(
            symbol,
            currentSessionId,
            currentEventTimeUtc,
            MarketDataCapabilities.Unknown,
            cancellationToken);
        return PriorSessionContextBuilder.CreateView(result.Context, currentReferencePrice);
    }

    public Task<PriorSessionWarmupResult> WarmupAsync(
        string symbol,
        string currentSessionId,
        DateTimeOffset currentEventTimeUtc,
        MarketDataCapabilities currentMarketData,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentSessionId);
        ArgumentNullException.ThrowIfNull(currentMarketData);
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);
        _warmupOptions.Validate();
        symbol = symbol.Trim().ToUpperInvariant();
        var key = new WarmupRequestKey(
            symbol,
            currentSessionId,
            currentMarketData.Provider,
            currentMarketData.Feed,
            PriorSessionContextBuilder.DefinitionVersion,
            configuration.FeatureSetVersion,
            configuration.Version);
        _activeSessions[key.Symbol] = key.CurrentSessionId;
        CancelObsoleteWarmupScopes(key);
        var candidateScopeCancellation = new CancellationTokenSource();
        var scopeCancellation = _scopeCancellations.GetOrAdd(key, candidateScopeCancellation);
        if (!ReferenceEquals(scopeCancellation, candidateScopeCancellation))
        {
            candidateScopeCancellation.Dispose();
        }

        PruneCompletedObsoleteWarmups(key.Symbol);
        var entry = _warmups.GetOrAdd(
            key,
            request => new Lazy<Task<PriorSessionWarmupResult>>(() =>
                {
                    var task = ExecuteWarmupAsync(
                        request,
                        currentEventTimeUtc,
                        currentMarketData,
                        scopeCancellation.Token);
                    _ = task.ContinueWith(
                        _ => PruneCompletedObsoleteWarmups(request.Symbol),
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                    return task;
                },
                LazyThreadSafetyMode.ExecutionAndPublication));
        return AwaitWarmupAsync(
            entry.Value,
            key,
            currentMarketData,
            cancellationToken);
    }

    private async Task<PriorSessionWarmupResult> AwaitWarmupAsync(
        Task<PriorSessionWarmupResult> warmup,
        WarmupRequestKey request,
        MarketDataCapabilities currentMarketData,
        CancellationToken cancellationToken)
    {
        try
        {
            return await warmup.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var scope = PriorSessionWarmupScopeFactory.Create(
                request.Symbol,
                request.CurrentSessionId,
                currentMarketData,
                priorSessionId: null,
                request.PriorContextDefinitionVersion,
                request.FeatureSetVersion,
                request.ScannerConfigurationVersion);
            return TerminalUnavailable(
                scope,
                PriorSessionWarmupStatus.Cancelled,
                "Prior-session warm-up was cancelled for this waiter.");
        }
    }

    private async Task<PriorSessionWarmupResult> ExecuteWarmupAsync(
        WarmupRequestKey request,
        DateTimeOffset currentEventTimeUtc,
        MarketDataCapabilities currentMarketData,
        CancellationToken cancellationToken)
    {
        var pendingScope = PriorSessionWarmupScopeFactory.Create(
            request.Symbol,
            request.CurrentSessionId,
            currentMarketData,
            priorSessionId: null,
            request.PriorContextDefinitionVersion,
            request.FeatureSetVersion,
            request.ScannerConfigurationVersion);
        logger?.LogInformation(
            "Prior-session warm-up started for {Symbol}/{SessionId} from {LiveProvider}/{LiveFeed}.",
            request.Symbol,
            request.CurrentSessionId,
            currentMarketData.Provider,
            currentMarketData.Feed);
        using var timeout = new CancellationTokenSource(_warmupOptions.Timeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeout.Token);
        try
        {
            await _concurrency.WaitAsync(linked.Token);
            try
            {
                return await ResolveWarmupAsync(
                    request,
                    currentEventTimeUtc,
                    currentMarketData,
                    pendingScope,
                    linked.Token);
            }
            finally
            {
                _concurrency.Release();
            }
        }
        catch (OperationCanceledException)
        {
            var cancelledByScope =
                cancellationToken.IsCancellationRequested
                && !timeout.IsCancellationRequested;
            var result = TerminalUnavailable(
                pendingScope,
                cancelledByScope
                    ? PriorSessionWarmupStatus.Cancelled
                    : PriorSessionWarmupStatus.TimedOut,
                cancelledByScope
                    ? "Prior-session warm-up was cancelled."
                    : $"Prior-session warm-up timed out after {_warmupOptions.TimeoutSeconds} seconds.");
            LogTerminal(result);
            return result;
        }
    }

    private async Task<PriorSessionWarmupResult> ResolveWarmupAsync(
        WarmupRequestKey request,
        DateTimeOffset currentEventTimeUtc,
        MarketDataCapabilities currentMarketData,
        PriorSessionWarmupScope pendingScope,
        CancellationToken cancellationToken)
    {
        if (calendar is null || historicalSource is null)
        {
            var unavailable = TerminalUnavailable(
                pendingScope,
                PriorSessionWarmupStatus.Unavailable,
                "Historical prior-session data is unavailable because no approved market-data source and session calendar are configured for this live provider.");
            LogTerminal(unavailable);
            return unavailable;
        }

        MarketSessionCalendarResolution resolution;
        try
        {
            resolution = await calendar.ResolvePreviousSessionAsync(
                request.Symbol,
                request.CurrentSessionId,
                currentEventTimeUtc,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or HttpRequestException)
        {
            var failure = TerminalUnavailable(
                pendingScope,
                PriorSessionWarmupStatus.ProviderFailure,
                $"Session-calendar resolution failed ({exception.GetType().Name}); current-session scanning continues.",
                calendarId: null);
            logger?.LogWarning(
                exception,
                "Prior-session calendar resolution failed for {Symbol}/{SessionId}.",
                request.Symbol,
                request.CurrentSessionId);
            LogTerminal(failure);
            return failure;
        }

        if (resolution.Status != MarketSessionCalendarStatus.Success)
        {
            var result = TerminalUnavailable(
                pendingScope with { PriorSessionId = resolution.PreviousSession?.SessionId },
                MapCalendarStatus(resolution.Status),
                resolution.Message ?? "The previous eligible session could not be resolved.",
                calendarStatus: resolution.Status,
                calendarId: resolution.CalendarId,
                prior: resolution.PreviousSession);
            LogTerminal(result);
            return result;
        }

        if (resolution.CurrentSession is null || resolution.PreviousSession is null)
        {
            var invalid = TerminalUnavailable(
                pendingScope,
                PriorSessionWarmupStatus.Invalid,
                "The calendar reported success without an explicit current and prior regular session.",
                calendarStatus: resolution.Status,
                calendarId: resolution.CalendarId);
            LogTerminal(invalid);
            return invalid;
        }

        var current = resolution.CurrentSession;
        var previous = resolution.PreviousSession;
        if (!current.SessionId.Equals(request.CurrentSessionId, StringComparison.Ordinal)
            || current.Validity != MarketSessionValidity.Valid
            || current.SessionType != MarketSessionType.Regular
            || currentEventTimeUtc < current.OpenAtUtc
            || currentEventTimeUtc > current.CloseAtUtc
            || previous.SessionId.Equals(current.SessionId, StringComparison.Ordinal)
            || previous.EndUtc > current.StartUtc
            || previous.Validity != MarketSessionValidity.Valid
            || previous.SessionType != MarketSessionType.Regular)
        {
            var invalid = TerminalUnavailable(
                pendingScope with { PriorSessionId = previous.SessionId },
                PriorSessionWarmupStatus.Invalid,
                "The session calendar returned an invalid current/prior regular-session relationship.",
                calendarStatus: resolution.Status,
                calendarId: resolution.CalendarId,
                prior: previous);
            LogTerminal(invalid);
            return invalid;
        }

        var resolvedScope = pendingScope with
        {
            PriorSessionId = previous.SessionId,
            HistoricalProvider = previous.Provider,
            HistoricalFeed = currentMarketData.Provider.Equals("Alpaca", StringComparison.OrdinalIgnoreCase)
                ? "sip"
                : pendingScope.HistoricalFeed
        };
        logger?.LogInformation(
            "Resolved prior session {PriorSessionId} ({TradingDate}) for {Symbol}/{SessionId}; calendar={CalendarId}.",
            previous.SessionId,
            previous.TradingDate,
            request.Symbol,
            request.CurrentSessionId,
            resolution.CalendarId);

        HistoricalMarketSessionData? historicalData;
        try
        {
            historicalData = await historicalSource.ReadSessionAsync(
                request.Symbol,
                previous.ToMarketSession(),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or HttpRequestException)
        {
            var failure = TerminalUnavailable(
                resolvedScope,
                PriorSessionWarmupStatus.ProviderFailure,
                $"Historical source read failed ({exception.GetType().Name}); current-session scanning continues.",
                calendarStatus: resolution.Status,
                calendarId: resolution.CalendarId,
                prior: previous);
            logger?.LogWarning(
                exception,
                "Historical warm-up retrieval failed for {Symbol}/{SessionId}.",
                request.Symbol,
                request.CurrentSessionId);
            LogTerminal(failure);
            return failure;
        }

        if (historicalData is null)
        {
            var unavailable = TerminalUnavailable(
                resolvedScope,
                PriorSessionWarmupStatus.Unavailable,
                $"No historical data is available for prior session '{previous.SessionId}'.",
                calendarStatus: resolution.Status,
                calendarId: resolution.CalendarId,
                prior: previous);
            LogTerminal(unavailable);
            return unavailable;
        }

        var actualScope = resolvedScope with
        {
            HistoricalProvider = historicalData.Capabilities.Provider,
            HistoricalFeed = historicalData.Capabilities.Feed,
            HistoricalSourceKind = historicalData.Source
        };
        logger?.LogInformation(
            "Historical request completed for {Symbol}/{PriorSessionId}: status={RetrievalStatus}, pages={Pages}, records={Records}, duplicates={Duplicates}, complete={Complete}.",
            request.Symbol,
            previous.SessionId,
            historicalData.RetrievalStatus,
            historicalData.Provenance?.PagesRetrieved,
            historicalData.Events.Count,
            historicalData.Provenance?.DuplicateRecordsIgnored,
            historicalData.DeclaresCompleteCoverage);

        PriorSessionContext context;
        try
        {
            if (reconstructor is not null
                && historicalData.Capabilities.Provider.Equals(
                    "Alpaca", StringComparison.OrdinalIgnoreCase))
            {
                context = reconstructor.Reconstruct(
                    request.Symbol,
                    request.CurrentSessionId,
                    previous,
                    historicalData,
                    new HistoricalReconstructionRequirements(
                        "Alpaca",
                        "sip",
                        "historical-rest"),
                    configuration);
            }
            else
            {
                context = builder.Build(
                    request.Symbol,
                    request.CurrentSessionId,
                    previous.ToMarketSession(),
                    historicalData,
                    configuration);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger?.LogError(
                exception,
                "Prior-session reconstruction failed for {Symbol}/{SessionId}.",
                request.Symbol,
                request.CurrentSessionId);
            var reconstructionFailure = TerminalUnavailable(
                actualScope,
                PriorSessionWarmupStatus.ReconstructionFailure,
                $"Prior-session reconstruction failed ({exception.GetType().Name}); current-session scanning continues.",
                calendarStatus: resolution.Status,
                calendarId: resolution.CalendarId,
                historicalData: historicalData,
                prior: previous);
            LogTerminal(reconstructionFailure);
            return reconstructionFailure;
        }

        var status = MapWarmupStatus(historicalData, context.Completeness);
        var terminal = new PriorSessionWarmupResult(
            context,
            new PriorSessionWarmupOutcome(
                actualScope,
                status,
                context.CompletenessReason,
                previous.TradingDate,
                previous.OpenAtUtc,
                previous.CloseAtUtc,
                resolution.CalendarId,
                resolution.Status,
                historicalData.RetrievalStatus,
                historicalData.PaginationExhausted,
                historicalData.Provenance?.PagesRetrieved,
                historicalData.Provenance?.DuplicateRecordsIgnored,
                context.Completeness));
        LogTerminal(terminal);
        return terminal;
    }

    private PriorSessionWarmupResult TerminalUnavailable(
        PriorSessionWarmupScope scope,
        PriorSessionWarmupStatus status,
        string reason,
        MarketSessionCalendarStatus? calendarStatus = null,
        string? calendarId = null,
        HistoricalMarketSessionData? historicalData = null,
        MarketSessionDefinition? prior = null) =>
        new(
            builder.BuildUnavailable(
                scope.Symbol,
                scope.CurrentSessionId,
                reason,
                calendarId),
            new PriorSessionWarmupOutcome(
                scope,
                status,
                reason,
                prior?.TradingDate,
                prior?.OpenAtUtc,
                prior?.CloseAtUtc,
                calendarId,
                calendarStatus,
                historicalData?.RetrievalStatus,
                historicalData?.PaginationExhausted,
                historicalData?.Provenance?.PagesRetrieved,
                historicalData?.Provenance?.DuplicateRecordsIgnored,
                PriorSessionContextStatus.Unavailable));

    private void LogTerminal(PriorSessionWarmupResult result)
    {
        var outcome = result.Outcome;
        logger?.LogInformation(
            "Prior-session warm-up completed for {Symbol}/{SessionId}: status={WarmupStatus}, prior={PriorSessionId}, completeness={Completeness}, mode={Mode}.",
            outcome.Scope.Symbol,
            outcome.Scope.CurrentSessionId,
            outcome.Status,
            outcome.Scope.PriorSessionId,
            outcome.ContextCompleteness,
            outcome.Status == PriorSessionWarmupStatus.CompleteUsable
                ? "PriorSessionEnhanced"
                : "CurrentSessionOnly");
    }

    private static PriorSessionWarmupStatus MapCalendarStatus(
        MarketSessionCalendarStatus status) =>
        status switch
        {
            MarketSessionCalendarStatus.NoPreviousSession => PriorSessionWarmupStatus.Unavailable,
            MarketSessionCalendarStatus.Cancelled => PriorSessionWarmupStatus.Cancelled,
            MarketSessionCalendarStatus.AuthenticationFailure => PriorSessionWarmupStatus.AuthenticationFailure,
            MarketSessionCalendarStatus.EntitlementFailure => PriorSessionWarmupStatus.EntitlementFailure,
            MarketSessionCalendarStatus.LimitExceeded => PriorSessionWarmupStatus.LimitExceeded,
            MarketSessionCalendarStatus.MalformedResponse or
                MarketSessionCalendarStatus.InvalidReference => PriorSessionWarmupStatus.Invalid,
            _ => PriorSessionWarmupStatus.ProviderFailure
        };

    private static PriorSessionWarmupStatus MapWarmupStatus(
        HistoricalMarketSessionData historicalData,
        PriorSessionContextStatus completeness) =>
        historicalData.RetrievalStatus switch
        {
            HistoricalRetrievalStatus.Cancelled => PriorSessionWarmupStatus.Cancelled,
            HistoricalRetrievalStatus.AuthenticationFailure => PriorSessionWarmupStatus.AuthenticationFailure,
            HistoricalRetrievalStatus.EntitlementFailure => PriorSessionWarmupStatus.EntitlementFailure,
            HistoricalRetrievalStatus.LimitExceeded => PriorSessionWarmupStatus.LimitExceeded,
            HistoricalRetrievalStatus.MalformedResponse => PriorSessionWarmupStatus.Invalid,
            HistoricalRetrievalStatus.ProviderFailure => PriorSessionWarmupStatus.ProviderFailure,
            _ => completeness switch
            {
                PriorSessionContextStatus.Available => PriorSessionWarmupStatus.CompleteUsable,
                PriorSessionContextStatus.Partial => PriorSessionWarmupStatus.Partial,
                PriorSessionContextStatus.Invalid => PriorSessionWarmupStatus.Invalid,
                _ => PriorSessionWarmupStatus.Unavailable
            }
        };

    private void PruneCompletedObsoleteWarmups(string symbol)
    {
        if (!_activeSessions.TryGetValue(symbol, out var activeSession))
        {
            return;
        }

        foreach (var entry in _warmups
                     .Where(item => item.Key.Symbol.Equals(
                         symbol, StringComparison.OrdinalIgnoreCase)
                         && !item.Key.CurrentSessionId.Equals(activeSession, StringComparison.Ordinal)
                         && item.Value.IsValueCreated
                         && item.Value.Value.IsCompleted))
        {
            _warmups.TryRemove(entry.Key, out _);
            if (_scopeCancellations.TryRemove(entry.Key, out var cancellation))
            {
                cancellation.Dispose();
            }
        }
    }

    private void CancelObsoleteWarmupScopes(WarmupRequestKey active)
    {
        foreach (var entry in _scopeCancellations.Where(item =>
                     item.Key.Symbol.Equals(active.Symbol, StringComparison.OrdinalIgnoreCase)
                     && !item.Key.CurrentSessionId.Equals(
                         active.CurrentSessionId, StringComparison.Ordinal)))
        {
            try
            {
                entry.Value.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private sealed record WarmupRequestKey(
        string Symbol,
        string CurrentSessionId,
        string LiveProvider,
        string LiveFeed,
        string PriorContextDefinitionVersion,
        string FeatureSetVersion,
        string ScannerConfigurationVersion);
}

public static class PriorSessionWarmupScopeFactory
{
    public static PriorSessionWarmupOutcome Pending(
        string symbol,
        string currentSessionId,
        MarketDataCapabilities liveMarketData,
        ScannerConfiguration configuration) =>
        new(
            Create(
                symbol,
                currentSessionId,
                liveMarketData,
                priorSessionId: null,
                PriorSessionContextBuilder.DefinitionVersion,
                configuration.FeatureSetVersion,
                configuration.Version),
            PriorSessionWarmupStatus.Pending,
            "Prior-session warm-up is in progress before the first eligible analysis.");

    public static PriorSessionWarmupScope Create(
        string symbol,
        string currentSessionId,
        MarketDataCapabilities liveMarketData,
        string? priorSessionId,
        string priorContextDefinitionVersion,
        string? featureSetVersion,
        string? scannerConfigurationVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentSessionId);
        ArgumentNullException.ThrowIfNull(liveMarketData);
        var isAlpaca = liveMarketData.Provider.Equals("Alpaca", StringComparison.OrdinalIgnoreCase);
        var isFixture = liveMarketData.Provider.Equals(
            "RecordedFixture", StringComparison.OrdinalIgnoreCase);
        return new PriorSessionWarmupScope(
            symbol.Trim().ToUpperInvariant(),
            currentSessionId,
            liveMarketData.Provider,
            liveMarketData.Feed,
            priorSessionId,
            isAlpaca ? "Alpaca" : isFixture ? "RecordedFixture" : "Unavailable",
            isAlpaca ? "sip" : isFixture ? liveMarketData.Feed : "Unavailable",
            isAlpaca ? "historical-rest" : isFixture ? "recorded-fixture-v1" : "Unavailable",
            priorContextDefinitionVersion,
            featureSetVersion ?? "",
            scannerConfigurationVersion ?? "");
    }
}
