using System.Text.Json;
using MarketOpportunity.Analytics;
using MarketOpportunity.Application;
using MarketOpportunity.Domain;

namespace MarketOpportunity.UnitTests;

public sealed class PriorSessionContextTests
{
    private static readonly DateTimeOffset SessionStart =
        new(2026, 10, 22, 13, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SessionEnd =
        new(2026, 10, 22, 20, 0, 0, TimeSpan.Zero);
    private static readonly MarketSession PriorSession =
        new("prior-session", SessionStart, SessionEnd);
    private static readonly MarketSession CurrentSession =
        new(
            "current-session",
            new DateTimeOffset(2026, 10, 23, 13, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 23, 20, 0, 0, TimeSpan.Zero));
    private static readonly MarketDataCapabilities Capabilities = new(
        "RecordedFixture",
        "Synthetic",
        MarketDataCoverage.Synthetic,
        false,
        true,
        true);
    private readonly PriorSessionContextBuilder _builder = new(new FeatureCalculator());

    [Fact]
    public void Explicit_calendar_uses_recorded_prior_session_across_weekend_and_holiday()
    {
        var friday = new MarketSession(
            "friday",
            new DateTimeOffset(2026, 10, 23, 13, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 23, 20, 0, 0, TimeSpan.Zero));
        var monday = new MarketSession(
            "monday",
            new DateTimeOffset(2026, 10, 26, 13, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 26, 20, 0, 0, TimeSpan.Zero));
        var preHolidayFriday = new MarketSession(
            "pre-holiday-friday",
            new DateTimeOffset(2026, 10, 09, 13, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 09, 20, 0, 0, TimeSpan.Zero));
        var postHolidayTuesday = new MarketSession(
            "post-holiday-tuesday",
            new DateTimeOffset(2026, 10, 13, 13, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 13, 20, 0, 0, TimeSpan.Zero));
        var calendar = new ExplicitMarketSessionCalendar(
            "test-calendar",
            [
                new MarketSessionCalendarEntry(friday, PriorSession),
                new MarketSessionCalendarEntry(monday, friday),
                new MarketSessionCalendarEntry(postHolidayTuesday, preHolidayFriday)
            ]);

        var weekend = calendar.ResolvePreviousSession("MOE", monday.SessionId, monday.StartUtc);
        var holiday = calendar.ResolvePreviousSession(
            "MOE",
            postHolidayTuesday.SessionId,
            postHolidayTuesday.StartUtc);
        var missing = calendar.ResolvePreviousSession(
            "MOE",
            "unmapped-session",
            postHolidayTuesday.StartUtc);

        Assert.Equal("friday", weekend.PreviousSession?.SessionId);
        Assert.Equal("pre-holiday-friday", holiday.PreviousSession?.SessionId);
        Assert.Null(missing.CurrentSession);
        Assert.NotNull(missing.Message);
    }

    [Fact]
    public void Complete_context_uses_event_time_trade_close_and_reuses_versioned_swings()
    {
        var events = CreateTrades();
        var context = _builder.Build(
            "MOE",
            CurrentSession.SessionId,
            PriorSession,
            Historical(events, complete: true),
            new ScannerConfiguration());

        Assert.Equal(PriorSessionContextStatus.Available, context.Completeness);
        Assert.Equal(7m, context.PriorSessionHigh);
        Assert.Equal(1m, context.PriorSessionLow);
        Assert.Equal(5m, context.PriorSessionClose);
        Assert.Equal(SessionStart.AddSeconds(12), context.PriorSessionCloseEventTimeUtc);
        Assert.Equal(
            PriorSessionCloseSemantics.LastEligibleTradeAtOrBeforeSessionEnd,
            context.CloseSemantics);
        Assert.NotNull(context.LatestConfirmedSwingHigh);
        Assert.NotNull(context.LatestConfirmedSwingLow);
        Assert.Equal("session-swing-fractal-v1", context.LatestConfirmedSwingHigh!.AlgorithmVersion);
        Assert.Equal("session-swing-fractal-v1", context.LatestConfirmedSwingLow!.AlgorithmVersion);
        Assert.Equal(5m, context.PreviousConfirmedSwingHigh?.Price);
        Assert.Equal(2m, context.PreviousConfirmedSwingLow?.Price);
        Assert.Equal(MarketStructureState.Bullish, context.StructureState);
        Assert.All(context.SupportCandidates, candidate =>
        {
            Assert.Equal(StructuralLevelType.Support, candidate.Type);
            Assert.NotEqual(Guid.Empty, candidate.SourceSwingId);
            Assert.Equal(candidate.SourceSwingId, candidate.Id);
        });
        Assert.All(context.ResistanceCandidates, candidate =>
            Assert.Equal(StructuralLevelType.Resistance, candidate.Type));
    }

    [Fact]
    public void Partial_coverage_reports_observed_values_but_not_prior_session_levels_or_close()
    {
        var context = _builder.Build(
            "MOE",
            CurrentSession.SessionId,
            PriorSession,
            Historical(CreateTrades(), complete: false),
            new ScannerConfiguration());

        Assert.Equal(PriorSessionContextStatus.Partial, context.Completeness);
        Assert.Null(context.PriorSessionHigh);
        Assert.Null(context.PriorSessionLow);
        Assert.Null(context.PriorSessionClose);
        Assert.Equal(7m, context.ObservedHigh);
        Assert.Equal(1m, context.ObservedLow);
        Assert.Equal(5m, context.LastObservedTrade);
        Assert.Equal(PriorSessionCloseSemantics.Unavailable, context.CloseSemantics);
        Assert.False(context.IsSessionStartCovered);
        Assert.False(context.IsSessionEndCovered);
        Assert.Contains("not session high, low, or close", context.CompletenessReason);
        Assert.Equal("RecordedFixture", context.MarketData?.Provider);
        Assert.Equal("recorded-fixture-v1", context.HistoricalSource);
    }

    [Fact]
    public void Context_is_deterministic_and_current_price_changes_only_distance_projection()
    {
        var data = Historical(CreateTrades(), complete: true);
        var first = _builder.Build(
            "MOE", CurrentSession.SessionId, PriorSession, data, new ScannerConfiguration());
        var replay = _builder.Build(
            "MOE", CurrentSession.SessionId, PriorSession, data, new ScannerConfiguration());
        var changedPrice = _builder.Build(
            "MOE",
            CurrentSession.SessionId,
            PriorSession,
            Historical(CreateTrades(changedTradeIndex: 2), complete: true),
            new ScannerConfiguration());
        var firstView = PriorSessionContextBuilder.CreateView(first, 2m);
        var secondView = PriorSessionContextBuilder.CreateView(first, 5m);

        Assert.Equal(first.ContextId, replay.ContextId);
        Assert.NotEqual(first.ContextId, changedPrice.ContextId);
        Assert.Equal(first.ContextId, firstView.Context.ContextId);
        Assert.NotEqual(
            firstView.DistanceToLatestSwingHigh?.AbsolutePriceDistance,
            secondView.DistanceToLatestSwingHigh?.AbsolutePriceDistance);
        Assert.Equal(first.ContextId, secondView.Context.ContextId);
        Assert.Equal(1m, firstView.DistanceToPriorLow?.LevelPrice);
        Assert.Equal(1m, firstView.DistanceToPriorLow?.AbsolutePriceDistance);
        Assert.Equal(50m, firstView.DistanceToPriorLow?.PercentDistance);
    }

    [Fact]
    public void Later_and_current_session_events_cannot_leak_into_context()
    {
        var baseEvents = CreateTrades();
        var leakedEvents = baseEvents.Concat(
        [
            new TradeEvent(
                "QNT",
                PriorSession.SessionId,
                SessionStart.AddSeconds(1),
                SessionStart.AddSeconds(1),
                "RecordedFixture",
                "other-symbol",
                999m,
                10),
            new TradeEvent(
                "MOE",
                CurrentSession.SessionId,
                CurrentSession.StartUtc.AddSeconds(1),
                CurrentSession.StartUtc.AddSeconds(1),
                "RecordedFixture",
                "current-session",
                997m,
                10),
            new TradeEvent(
                "MOE",
                CurrentSession.SessionId,
                SessionEnd.AddDays(1),
                SessionEnd.AddDays(1),
                "RecordedFixture",
                "future-session",
                999m,
                10),
            new TradeEvent(
                "MOE",
                PriorSession.SessionId,
                SessionEnd.AddSeconds(1),
                SessionEnd.AddSeconds(2),
                "RecordedFixture",
                "after-prior-session",
                998m,
                10)
        ]).ToArray();
        var baseContext = _builder.Build(
            "MOE", CurrentSession.SessionId, PriorSession,
            Historical(baseEvents, complete: true), new ScannerConfiguration());
        var leakedContext = _builder.Build(
            "MOE", CurrentSession.SessionId, PriorSession,
            Historical(leakedEvents, complete: true), new ScannerConfiguration());

        Assert.Equal(baseContext.ContextId, leakedContext.ContextId);
        Assert.Equal(baseContext.ObservedHigh, leakedContext.ObservedHigh);
        Assert.Equal(baseContext.LatestConfirmedSwingHigh, leakedContext.LatestConfirmedSwingHigh);
    }

    [Fact]
    public void Invalid_trade_price_marks_context_invalid_and_suppresses_session_levels()
    {
        var invalidEvents = CreateTrades().Append(
            new TradeEvent(
                "MOE",
                PriorSession.SessionId,
                SessionStart.AddMinutes(2),
                SessionStart.AddMinutes(2),
                "RecordedFixture",
                "invalid-price",
                0m,
                10)).ToArray();
        var context = _builder.Build(
            "MOE",
            CurrentSession.SessionId,
            PriorSession,
            Historical(invalidEvents, complete: true),
            new ScannerConfiguration());

        Assert.Equal(PriorSessionContextStatus.Invalid, context.Completeness);
        Assert.Null(context.PriorSessionHigh);
        Assert.Null(context.PriorSessionLow);
        Assert.Null(context.PriorSessionClose);
    }

    [Fact]
    public void Prior_structure_reports_bullish_bearish_mixed_range_and_insufficient()
    {
        var scenarios = new[]
        {
            (new decimal[] { 1.5m, 1m, 5m, 3m, 2m, 4m, 3m, 7m, 5m, 4m, 6m, 5m },
                MarketStructureState.Bullish),
            (new decimal[] { 18.5m, 19m, 15m, 17m, 18m, 16m, 17m, 13m, 15m, 16m, 14m, 15m },
                MarketStructureState.Bearish),
            (new decimal[] { 1m, 2m, 5m, 3m, 2m, 4m, 3m, 6m, 5m, 1m, 2m, 3m },
                MarketStructureState.Mixed),
            (new decimal[] { 1m, 2m, 5m, 3m, 1m, 2m, 5m, 3m, 1m, 2m, 4m },
                MarketStructureState.Range),
            (new decimal[] { 1m, 2m, 3m, 2m },
                MarketStructureState.Insufficient)
        };

        foreach (var (prices, expected) in scenarios)
        {
            var context = _builder.Build(
                "MOE",
                CurrentSession.SessionId,
                PriorSession,
                Historical(CreateTrades(prices), complete: true),
                new ScannerConfiguration());

            Assert.Equal(expected, context.StructureState);
        }
    }

    [Fact]
    public void Unavailable_context_and_context_json_preserve_explicit_status_and_provenance()
    {
        var context = _builder.BuildUnavailable(
            "MOE", CurrentSession.SessionId, "No preceding eligible session.", "fixture-calendar");
        var json = JsonSerializer.Serialize(context);
        var replay = JsonSerializer.Deserialize<PriorSessionContext>(json);

        Assert.Equal(PriorSessionContextStatus.Unavailable, replay?.Completeness);
        Assert.Equal(context.ContextId, replay?.ContextId);
        Assert.Equal("No preceding eligible session.", replay?.CompletenessReason);
        Assert.Equal("prior-session-context-v1", replay?.ContextDefinitionVersion);
    }

    [Fact]
    public async Task Missing_live_history_or_calendar_is_explicit_and_does_not_prevent_current_analysis()
    {
        var provider = new PriorSessionContextProvider(
            calendar: null,
            historicalSource: null,
            builder: _builder,
            configuration: new ScannerConfiguration());
        var view = await provider.GetContextAsync(
            "MOE",
            CurrentSession.SessionId,
            CurrentSession.StartUtc,
            12m,
            CancellationToken.None);

        Assert.Equal(PriorSessionContextStatus.Unavailable, view.Context.Completeness);
        Assert.Equal(12m, view.CurrentReferencePrice);
        Assert.Contains("unavailable", view.Context.CompletenessReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Historical_read_failure_is_reported_as_unavailable_without_throwing_into_scanner()
    {
        var calendar = new ExplicitMarketSessionCalendar(
            "test-calendar",
            [new MarketSessionCalendarEntry(CurrentSession, PriorSession)]);
        var provider = new PriorSessionContextProvider(
            calendar,
            new FailingHistoricalSource(),
            _builder,
            new ScannerConfiguration());
        var view = await provider.GetContextAsync(
            "MOE",
            CurrentSession.SessionId,
            CurrentSession.StartUtc,
            12m,
            CancellationToken.None);

        Assert.Equal(PriorSessionContextStatus.Unavailable, view.Context.Completeness);
        Assert.Contains("IOException", view.Context.CompletenessReason);
        Assert.Contains("current-session scanning continues", view.Context.CompletenessReason);
    }

    private static HistoricalMarketSessionData Historical(
        IReadOnlyList<MarketEvent> events,
        bool complete)
    {
        var eligible = events
            .Where(item => item.SessionId == PriorSession.SessionId
                && item.EventTimeUtc >= PriorSession.StartUtc
                && item.EventTimeUtc <= PriorSession.EndUtc)
            .ToArray();
        return new HistoricalMarketSessionData(
            events,
            Capabilities,
            "recorded-fixture-v1",
            complete ? PriorSession.StartUtc : eligible.Min(item => item.EventTimeUtc),
            complete ? PriorSession.EndUtc : eligible.Max(item => item.EventTimeUtc),
            complete,
            HasKnownGaps: false);
    }

    private static TradeEvent[] CreateTrades(
        decimal[]? sourcePrices = null,
        int? changedTradeIndex = null)
    {
        decimal[] prices = sourcePrices
            ?? [1.5m, 1m, 5m, 3m, 2m, 4m, 3m, 7m, 5m, 4m, 6m, 5m];
        if (changedTradeIndex is { } index)
        {
            prices[index] += 1m;
        }

        return prices.Select((price, index) => new TradeEvent(
                "MOE",
                PriorSession.SessionId,
                SessionStart.AddSeconds(index + 1),
                SessionStart.AddSeconds(100 - index),
                "RecordedFixture",
                $"trade-{index:D3}",
                price,
                10))
            .ToArray();
    }

    private sealed class FailingHistoricalSource : IHistoricalMarketDataSource
    {
        public Task<HistoricalMarketSessionData?> ReadSessionAsync(
            string symbol,
            MarketSession session,
            CancellationToken cancellationToken) =>
            throw new IOException("Fixture read failed.");
    }
}
