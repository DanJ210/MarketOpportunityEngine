using MarketOpportunity.Application;
using MarketOpportunity.Domain;

namespace MarketOpportunity.Infrastructure;

public sealed class FixtureHistoricalMarketDataSource(string fixturePath) : IHistoricalMarketDataSource
{
    public async Task<HistoricalMarketSessionData?> ReadSessionAsync(
        string symbol,
        MarketSession session,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(session);
        var fixtureSource = new FixtureMarketDataSource(fixturePath);
        var events = new List<MarketEvent>();
        await foreach (var marketEvent in fixtureSource.ReadEventsAsync(cancellationToken))
        {
            if (marketEvent.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)
                && marketEvent.SessionId.Equals(session.SessionId, StringComparison.Ordinal)
                && marketEvent.EventTimeUtc >= session.StartUtc
                && marketEvent.EventTimeUtc <= session.EndUtc)
            {
                events.Add(marketEvent);
            }
        }

        return new HistoricalMarketSessionData(
            events,
            fixtureSource.Capabilities,
            "recorded-fixture-v1",
            events.Count == 0 ? null : events.Min(item => item.EventTimeUtc),
            events.Count == 0 ? null : events.Max(item => item.EventTimeUtc),
            DeclaresCompleteCoverage: false,
            HasKnownGaps: false);
    }
}

public sealed class FixtureMarketSessionCalendar : IMarketSessionCalendar
{
    private static readonly DateTimeOffset Session22Start =
        new(2026, 10, 22, 13, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Session22End =
        new(2026, 10, 22, 20, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Session23Start =
        new(2026, 10, 23, 13, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Session23End =
        new(2026, 10, 23, 20, 0, 0, TimeSpan.Zero);

    private readonly ExplicitMarketSessionCalendar _calendar = new(
        "fixture-explicit-us-equity-rth-v1",
        [
            new MarketSessionCalendarEntry(
                Definition("fixture-2026-10-22", new DateOnly(2026, 10, 22), Session22Start, Session22End),
                null),
            new MarketSessionCalendarEntry(
                Definition("fixture-2026-10-23", new DateOnly(2026, 10, 23), Session23Start, Session23End),
                Definition("fixture-2026-10-22", new DateOnly(2026, 10, 22), Session22Start, Session22End))
        ]);

    public Task<MarketSessionCalendarResolution> ResolvePreviousSessionAsync(
        string symbol,
        string currentSessionId,
        DateTimeOffset currentEventTimeUtc,
        CancellationToken cancellationToken) =>
        _calendar.ResolvePreviousSessionAsync(
            symbol,
            currentSessionId,
            currentEventTimeUtc,
            cancellationToken);

    private static MarketSessionDefinition Definition(
        string sessionId,
        DateOnly tradingDate,
        DateTimeOffset openAtUtc,
        DateTimeOffset closeAtUtc) =>
        new(
            sessionId,
            tradingDate,
            openAtUtc,
            closeAtUtc,
            "RecordedFixture",
            "explicit-fixture-calendar",
            "fixture-explicit-us-equity-rth-v1",
            MarketSessionType.Regular,
            MarketSessionValidity.Valid);
}
