using MarketOpportunity.Domain;
using MarketOpportunity.Analytics;

namespace MarketOpportunity.Application;

public sealed record MarketSessionCalendarEntry(
    MarketSession CurrentSession,
    MarketSession? PreviousSession);

public sealed record MarketSessionCalendarResolution(
    MarketSession? CurrentSession,
    MarketSession? PreviousSession,
    string CalendarId,
    string? Message);

public interface IMarketSessionCalendar
{
    MarketSessionCalendarResolution ResolvePreviousSession(
        string symbol,
        string currentSessionId,
        DateTimeOffset currentEventTimeUtc);
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
                    || previous.EndUtc > entry.CurrentSession.StartUtc)
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

    public MarketSessionCalendarResolution ResolvePreviousSession(
        string symbol,
        string currentSessionId,
        DateTimeOffset currentEventTimeUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentSessionId);
        if (!_entries.TryGetValue(currentSessionId, out var entry))
        {
            return new MarketSessionCalendarResolution(
                null,
                null,
                _calendarId,
                $"The explicit calendar has no entry for session '{currentSessionId}'.");
        }

        if (currentEventTimeUtc < entry.CurrentSession.StartUtc
            || currentEventTimeUtc > entry.CurrentSession.EndUtc)
        {
            return new MarketSessionCalendarResolution(
                entry.CurrentSession,
                null,
                _calendarId,
                "The event time is outside the configured exchange-local session boundaries.");
        }

        return new MarketSessionCalendarResolution(
            entry.CurrentSession,
            entry.PreviousSession,
            _calendarId,
            entry.PreviousSession is null
                ? "No immediately preceding eligible session is configured."
                : null);
    }

    private static void ValidateSession(MarketSession session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(session.SessionId);
        if (session.StartUtc >= session.EndUtc)
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

        var resolution = calendar.ResolvePreviousSession(
            symbol,
            currentSessionId,
            currentEventTimeUtc);
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

        HistoricalMarketSessionData? historicalData;
        try
        {
            historicalData = await historicalSource.ReadSessionAsync(
                symbol,
                resolution.PreviousSession,
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
            resolution.PreviousSession,
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
