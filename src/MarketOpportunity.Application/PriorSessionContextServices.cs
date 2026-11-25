using MarketOpportunity.Domain;
using MarketOpportunity.Analytics;

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

public interface IPriorSessionContextProvider
{
    Task<PriorSessionContextView> GetContextAsync(
        string symbol,
        string currentSessionId,
        DateTimeOffset currentEventTimeUtc,
        decimal? currentReferencePrice,
        CancellationToken cancellationToken);
}

public sealed class PriorSessionContextProvider(
    IMarketSessionCalendar? calendar,
    IHistoricalMarketDataSource? historicalSource,
    PriorSessionContextBuilder builder,
    ScannerConfiguration configuration) : IPriorSessionContextProvider
{
    private readonly object _contextSync = new();
    private readonly Dictionary<(string Symbol, string SessionId), PriorSessionContext> _contexts = [];

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
        var key = (symbol.ToUpperInvariant(), currentSessionId);
        lock (_contextSync)
        {
            if (_contexts.TryGetValue(key, out var cachedContext))
            {
                return PriorSessionContextBuilder.CreateView(cachedContext, currentReferencePrice);
            }
        }

        if (calendar is null || historicalSource is null)
        {
            return View(builder.BuildUnavailable(
                symbol,
                currentSessionId,
                "Historical market data or an authoritative session calendar is unavailable for this source."));
        }

        var resolution = await calendar.ResolvePreviousSessionAsync(
            symbol,
            currentSessionId,
            currentEventTimeUtc,
            cancellationToken);
        if (resolution.CurrentSession is null || resolution.PreviousSession is null)
        {
            return View(builder.BuildUnavailable(
                symbol,
                currentSessionId,
                resolution.Message ?? "The previous eligible session could not be resolved.",
                resolution.CalendarId));
        }

        if (resolution.PreviousSession.SessionId == resolution.CurrentSession.SessionId
            || resolution.PreviousSession.EndUtc > resolution.CurrentSession.StartUtc)
        {
            throw new InvalidOperationException(
                "The session calendar returned an invalid previous-session relationship.");
        }

        var priorSession = resolution.PreviousSession.ToMarketSession();
        HistoricalMarketSessionData? historicalData;
        try
        {
            historicalData = await historicalSource.ReadSessionAsync(
                symbol,
                priorSession,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            return View(builder.BuildUnavailable(
                symbol,
                currentSessionId,
                $"Historical source read failed ({exception.GetType().Name}); current-session scanning continues.",
                resolution.CalendarId));
        }
        if (historicalData is null)
        {
            return View(builder.BuildUnavailable(
                symbol,
                currentSessionId,
                $"No historical data is available for prior session '{resolution.PreviousSession.SessionId}'.",
                resolution.CalendarId));
        }

        var context = builder.Build(
            symbol,
            currentSessionId,
            priorSession,
            historicalData,
            configuration);
        return View(context);

        PriorSessionContextView View(PriorSessionContext context)
        {
            lock (_contextSync)
            {
                _contexts.TryAdd(key, context);
                return PriorSessionContextBuilder.CreateView(
                    _contexts[key],
                    currentReferencePrice);
            }
        }
    }
}
