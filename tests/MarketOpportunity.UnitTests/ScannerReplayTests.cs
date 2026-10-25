using MarketOpportunity.Analytics;
using MarketOpportunity.Domain;

namespace MarketOpportunity.UnitTests;

public sealed class ScannerReplayTests
{
    private static readonly DateTimeOffset AsOfUtc = new(2026, 10, 23, 14, 30, 42, TimeSpan.Zero);
    private const string Symbol = "GOLD";
    private const string SessionId = "replay-session";
    private readonly FeatureCalculator _calculator = new();
    private readonly MomentumContinuationDetector _detector = new();

    [Fact]
    public void Golden_replay_is_stable_for_canonical_events_and_event_time_ordering()
    {
        var input = GoldenEvents();
        var first = Replay(input, new ScannerConfiguration());
        var second = Replay(input.Reverse().ToArray(), new ScannerConfiguration());

        Assert.Equal(first.Features.Id, second.Features.Id);
        Assert.Equal(first.Opportunity.Id, second.Opportunity.Id);
        Assert.Equal(102m, first.Features.SessionVwap);
        Assert.Equal(VwapSlopeDirection.Rising, first.Features.SessionVwapSlopeDirection);
        Assert.True(first.Features.SessionVwapSlopePerMinute > 0);
        Assert.Equal(103m, first.Features.ReferencePrice);
        Assert.Equal(3m, first.Features.ShortHorizonReturnPercent);
        Assert.Equal(3m, first.Features.SecondaryHorizonReturnPercent);
        Assert.Equal(100, first.Features.ShortHorizonVolume);
        Assert.Equal(OpportunityClassification.ProbableLong, first.Opportunity.Classification);
        Assert.Equal(100, first.Opportunity.QualityScore);
        Assert.Equal(first.Features, second.Features);
        Assert.Equal(first.Opportunity.Id, second.Opportunity.Id);
        Assert.Equal(first.Opportunity.Classification, second.Opportunity.Classification);
        Assert.Equal(first.Opportunity.Bias, second.Opportunity.Bias);
        Assert.Equal(first.Opportunity.QualityScore, second.Opportunity.QualityScore);
        Assert.Equal(first.Opportunity.SupportingEvidence, second.Opportunity.SupportingEvidence);
        Assert.Equal(first.Opportunity.ContradictoryEvidence, second.Opportunity.ContradictoryEvidence);
        Assert.Equal(first.Opportunity.ActiveVetoes, second.Opportunity.ActiveVetoes);
    }

    [Fact]
    public void Duplicate_identity_does_not_change_golden_replay()
    {
        var input = GoldenEvents();
        var baseline = Replay(input, new ScannerConfiguration());
        var duplicated = Replay(
            [.. input, input[0] with { Price = 500m, Size = 50_000 }],
            new ScannerConfiguration());

        Assert.Equal(baseline.Features.Id, duplicated.Features.Id);
        Assert.Equal(baseline.Opportunity.Id, duplicated.Opportunity.Id);
        Assert.Equal(baseline.Features.SessionVwap, duplicated.Features.SessionVwap);
        Assert.Equal(baseline.Features.ReferencePrice, duplicated.Features.ReferencePrice);
    }

    [Fact]
    public void A_future_event_cannot_change_an_earlier_snapshot_or_opportunity()
    {
        var input = GoldenEvents();
        var baseline = Replay(input, new ScannerConfiguration());
        var withFuture = Replay(
            [.. input, new TradeEvent(
                Symbol,
                SessionId,
                AsOfUtc.AddSeconds(30),
                AsOfUtc.AddSeconds(31),
                "RecordedFixture",
                "future",
                900m,
                90_000)],
            new ScannerConfiguration());

        Assert.Equal(baseline.Features, withFuture.Features);
        Assert.Equal(baseline.Opportunity.Id, withFuture.Opportunity.Id);
        Assert.Equal(baseline.Opportunity.AsOfUtc, withFuture.Opportunity.AsOfUtc);
        Assert.Equal(baseline.Opportunity.ValidUntilUtc, withFuture.Opportunity.ValidUntilUtc);
        Assert.Equal(baseline.Opportunity.Classification, withFuture.Opportunity.Classification);
        Assert.Equal(baseline.Opportunity.Bias, withFuture.Opportunity.Bias);
        Assert.Equal(baseline.Opportunity.QualityScore, withFuture.Opportunity.QualityScore);
        Assert.Equal(baseline.Opportunity.SupportingEvidence, withFuture.Opportunity.SupportingEvidence);
        Assert.Equal(baseline.Opportunity.ContradictoryEvidence, withFuture.Opportunity.ContradictoryEvidence);
        Assert.Equal(baseline.Opportunity.ActiveVetoes, withFuture.Opportunity.ActiveVetoes);
    }

    [Fact]
    public void Same_symbol_in_different_sessions_has_independent_event_state_and_vwap()
    {
        var configuration = new ScannerConfiguration();
        var firstSession = CreateSession("session-one", 100m);
        var secondSession = CreateSession("session-two", 200m);
        var first = _calculator.Calculate(firstSession, AsOfUtc, configuration);
        var second = _calculator.Calculate(secondSession, AsOfUtc, configuration);

        Assert.Equal("session-one", first.SessionId);
        Assert.Equal("session-two", second.SessionId);
        Assert.Equal(102m, first.SessionVwap);
        Assert.Equal(202m, second.SessionVwap);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void Session_vwap_slope_classifies_rising_falling_flat_and_insufficient_inputs()
    {
        var rising = CalculateSlope("GOLD", "rising", [100m, 101m, 102m]);
        var falling = CalculateSlope("GOLD", "falling", [102m, 101m, 100m]);
        var flat = CalculateSlope("GOLD", "flat", [100m, 100m, 100m]);
        var insufficient = CalculateSlope("GOLD", "insufficient", [100m]);

        Assert.Equal(VwapSlopeDirection.Rising, rising.SessionVwapSlopeDirection);
        Assert.Equal(1.5m, rising.SessionVwapSlopePerMinute);
        Assert.Equal(VwapSlopeDirection.Falling, falling.SessionVwapSlopeDirection);
        Assert.Equal(-1.5m, falling.SessionVwapSlopePerMinute);
        Assert.Equal(VwapSlopeDirection.Flat, flat.SessionVwapSlopeDirection);
        Assert.Equal(0m, flat.SessionVwapSlopePerMinute);
        Assert.Equal(VwapSlopeDirection.Insufficient, insufficient.SessionVwapSlopeDirection);
        Assert.Null(insufficient.SessionVwapSlopePerMinute);
    }

    [Fact]
    public void Session_vwap_slope_is_isolated_by_symbol_and_session()
    {
        var rising = CalculateSlope("GOLD", "session-one", [100m, 101m, 102m]);
        var falling = CalculateSlope("GOLD", "session-two", [102m, 101m, 100m]);
        var otherSymbol = CalculateSlope("SILVER", "session-one", [102m, 101m, 100m]);

        Assert.Equal(VwapSlopeDirection.Rising, rising.SessionVwapSlopeDirection);
        Assert.Equal(VwapSlopeDirection.Falling, falling.SessionVwapSlopeDirection);
        Assert.Equal(VwapSlopeDirection.Falling, otherSymbol.SessionVwapSlopeDirection);
        Assert.NotEqual(rising.Id, falling.Id);
        Assert.NotEqual(rising.Id, otherSymbol.Id);
    }

    [Fact]
    public void Session_vwap_slope_uses_event_time_and_ignores_future_events()
    {
        var baseline = CalculateSlope("GOLD", "future-check", [100m, 101m, 102m]);
        var state = CreateSlopeState("GOLD", "future-check", [100m, 101m, 102m]);
        state.Add(new TradeEvent(
            "GOLD",
            "future-check",
            AsOfUtc.AddSeconds(30),
            AsOfUtc.AddSeconds(31),
            "RecordedFixture",
            "future-slope-event",
            1m,
            10_000));

        var withFuture = _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration());

        Assert.Equal(baseline.SessionVwapSlopePerMinute, withFuture.SessionVwapSlopePerMinute);
        Assert.Equal(baseline.SessionVwapSlopeDirection, withFuture.SessionVwapSlopeDirection);
        Assert.Equal(baseline.Id, withFuture.Id);
    }

    [Fact]
    public void Changed_configuration_version_creates_distinct_feature_and_opportunity_identities()
    {
        var firstConfiguration = new ScannerConfiguration();
        var changedConfiguration = firstConfiguration with { Version = "scanner-config-v6" };
        var first = Replay(GoldenEvents(), firstConfiguration);
        var changed = Replay(GoldenEvents(), changedConfiguration);

        Assert.NotEqual(first.Features.Id, changed.Features.Id);
        Assert.NotEqual(first.Opportunity.Id, changed.Opportunity.Id);
    }

    [Theory]
    [InlineData(100, 125, 25)]
    [InlineData(200, 150, -25)]
    [InlineData(100, 100, 0)]
    public void Secondary_roc_uses_first_and_last_trades_in_its_inclusive_window(
        int firstPrice, int lastPrice, int expectedPercent)
    {
        var configuration = new ScannerConfiguration();
        var state = new SymbolEventState(Symbol, SessionId, 20);
        state.Add(new TradeEvent(
            Symbol, SessionId, AsOfUtc.AddSeconds(-31), AsOfUtc, "Fixture", "outside", 50m, 10));
        state.Add(new TradeEvent(
            Symbol, SessionId, AsOfUtc.AddSeconds(-30), AsOfUtc.AddDays(1), "Fixture", "first", firstPrice, 10));
        state.Add(new TradeEvent(
            Symbol, SessionId, AsOfUtc.AddSeconds(-10), AsOfUtc.AddDays(-1), "Fixture", "middle", 110m, 100));
        state.Add(new TradeEvent(
            Symbol, SessionId, AsOfUtc, AsOfUtc.AddDays(2), "Fixture", "last", lastPrice, 10));
        state.Add(new QuoteEvent(
            Symbol, SessionId, AsOfUtc, AsOfUtc, "Fixture", "quote", 999m, 1000m, 10, 10));
        state.Add(new TradeEvent(
            Symbol, SessionId, AsOfUtc.AddTicks(1), AsOfUtc, "Fixture", "future", 900m, 10));

        var features = _calculator.Calculate(state, AsOfUtc, configuration);

        Assert.Equal((decimal)expectedPercent, features.SecondaryHorizonReturnPercent);
        Assert.Equal((lastPrice - 50m) / 50m * 100m, features.ShortHorizonReturnPercent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Secondary_roc_is_unavailable_with_fewer_than_two_in_window_observations(int count)
    {
        var state = new SymbolEventState(Symbol, SessionId, 20);
        state.Add(new TradeEvent(
            Symbol, SessionId, AsOfUtc.AddSeconds(-31), AsOfUtc, "Fixture", "outside", 100m, 10));
        if (count == 1)
        {
            state.Add(new TradeEvent(
                Symbol, SessionId, AsOfUtc, AsOfUtc, "Fixture", "inside", 110m, 10));
        }

        var features = _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration());

        Assert.Null(features.SecondaryHorizonReturnPercent);
    }

    [Theory]
    [InlineData(0, 100, 110)]
    [InlineData(-100, 100, 110)]
    [InlineData(100, 0, 110)]
    [InlineData(100, -1, 110)]
    [InlineData(100, 110, 0)]
    [InlineData(100, 110, -1)]
    public void Secondary_roc_is_unavailable_for_non_positive_window_prices(int first, int middle, int last)
    {
        var state = CreateSlopeState(Symbol, SessionId, [first, middle, last]);
        state.Add(new TradeEvent(
            Symbol, SessionId, AsOfUtc.AddSeconds(-30), AsOfUtc, "Fixture", "secondary-first", first, 10));
        var features = _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration());

        Assert.Null(features.SecondaryHorizonReturnPercent);
    }

    [Fact]
    public void Secondary_roc_resolves_equal_timestamps_by_canonical_order_not_arrival_or_receive_time()
    {
        var input = new TradeEvent[]
        {
            new(Symbol, SessionId, AsOfUtc, AsOfUtc.AddDays(1), "A", "a", 100m, 10),
            new(Symbol, SessionId, AsOfUtc, AsOfUtc.AddDays(-1), "A", "b", 110m, 10),
            new(Symbol, SessionId, AsOfUtc, AsOfUtc, "B", "a", 125m, 10)
        };
        var first = Replay(input, new ScannerConfiguration());
        var reordered = Replay(input.Reverse().ToArray(), new ScannerConfiguration());
        var changedReceiveTimes = Replay(
            input.Select(trade => trade with { ReceivedTimeUtc = AsOfUtc.AddYears(1) }).ToArray(),
            new ScannerConfiguration());

        Assert.Equal(25m, first.Features.SecondaryHorizonReturnPercent);
        Assert.Equal(first.Features, reordered.Features);
        Assert.Equal(first.Features, changedReceiveTimes.Features);
        Assert.Null(first.Features.SessionVwapSlopePerMinute);
        Assert.Equal(VwapSlopeDirection.Insufficient, first.Features.SessionVwapSlopeDirection);
    }

    [Fact]
    public void Secondary_roc_is_isolated_by_symbol_and_explicit_session()
    {
        var first = CalculateSlope(Symbol, "session-one", [100m, 100m, 125m]);
        var second = CalculateSlope(Symbol, "session-two", [200m, 200m, 150m]);
        var otherSymbol = CalculateSlope("SILVER", "session-one", [100m, 100m, 100m]);
        var state = CreateSlopeState(Symbol, "session-one", [100m, 100m, 125m]);

        Assert.Equal(25m, first.SecondaryHorizonReturnPercent);
        Assert.Equal(-25m, second.SecondaryHorizonReturnPercent);
        Assert.Equal(0m, otherSymbol.SecondaryHorizonReturnPercent);
        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.Id, otherSymbol.Id);
        Assert.Throws<ArgumentException>(() => state.Add(new TradeEvent(
            Symbol, "session-two", AsOfUtc, AsOfUtc, "Fixture", "wrong-session", 900m, 10)));
        Assert.Throws<ArgumentException>(() => state.Add(new TradeEvent(
            "SILVER", "session-one", AsOfUtc, AsOfUtc, "Fixture", "wrong-symbol", 900m, 10)));
        Assert.Equal(first, _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration()));
    }

    [Fact]
    public void Secondary_window_changes_feature_identity_even_when_its_value_is_unchanged()
    {
        var configuration = new ScannerConfiguration();
        var baseline = Replay(GoldenEvents(), configuration);
        var sameValue = Replay(GoldenEvents(), configuration with { SecondaryMomentumWindowSeconds = 31 });
        var shorterWindow = Replay(GoldenEvents(), configuration with { SecondaryMomentumWindowSeconds = 10 });

        Assert.Equal(baseline.Features.SecondaryHorizonReturnPercent, sameValue.Features.SecondaryHorizonReturnPercent);
        Assert.NotEqual(baseline.Features.Id, sameValue.Features.Id);
        Assert.NotEqual(baseline.Opportunity.Id, sameValue.Opportunity.Id);
        Assert.Equal(1m / 102m * 100m, shorterWindow.Features.SecondaryHorizonReturnPercent);
        Assert.Equal(baseline.Features.ShortHorizonReturnPercent, shorterWindow.Features.ShortHorizonReturnPercent);
        Assert.Equal(baseline.Features.SessionVwapSlopePerMinute, shorterWindow.Features.SessionVwapSlopePerMinute);
        Assert.Equal(baseline.Opportunity.Classification, shorterWindow.Opportunity.Classification);
        Assert.Equal(baseline.Opportunity.QualityScore, shorterWindow.Opportunity.QualityScore);
        Assert.Equal(baseline.Opportunity.ActiveVetoes, shorterWindow.Opportunity.ActiveVetoes);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(60)]
    [InlineData(61)]
    public void Secondary_window_must_be_positive_and_shorter_than_the_primary_window(int seconds)
    {
        var configuration = new ScannerConfiguration { SecondaryMomentumWindowSeconds = seconds };

        Assert.Throws<InvalidOperationException>(configuration.Validate);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(59)]
    public void Secondary_window_accepts_distinct_shorter_horizons(int seconds)
    {
        new ScannerConfiguration { SecondaryMomentumWindowSeconds = seconds }.Validate();
    }

    [Fact]
    public void Secondary_roc_is_descriptive_even_when_unavailable_or_conflicting()
    {
        var configuration = new ScannerConfiguration();
        var baseline = Replay(GoldenEvents(), configuration);
        foreach (var value in new decimal?[] { null, -100m, 100m })
        {
            var opportunity = _detector.Evaluate(
                baseline.Features with { SecondaryHorizonReturnPercent = value }, configuration);

            Assert.Equal(baseline.Opportunity.Id, opportunity.Id);
            Assert.Equal(baseline.Opportunity.Classification, opportunity.Classification);
            Assert.Equal(baseline.Opportunity.Bias, opportunity.Bias);
            Assert.Equal(baseline.Opportunity.QualityScore, opportunity.QualityScore);
            Assert.Equal(baseline.Opportunity.SupportingEvidence, opportunity.SupportingEvidence);
            Assert.Equal(baseline.Opportunity.ContradictoryEvidence, opportunity.ContradictoryEvidence);
            Assert.Equal(baseline.Opportunity.ActiveVetoes, opportunity.ActiveVetoes);
            Assert.Equal(baseline.Opportunity.InvalidationCondition, opportunity.InvalidationCondition);
        }
    }

    private (FeatureSnapshot Features, Opportunity Opportunity) Replay(
        IReadOnlyCollection<TradeEvent> input,
        ScannerConfiguration configuration)
    {
        var state = new SymbolEventState(Symbol, SessionId, configuration.MaximumStateEvents);
        foreach (var marketEvent in input)
        {
            state.Add(marketEvent);
        }

        state.Add(new QuoteEvent(
            Symbol,
            SessionId,
            AsOfUtc,
            AsOfUtc,
            "RecordedFixture",
            "quote-current",
            102.99m,
            103.01m,
            500,
            500));
        var features = _calculator.Calculate(state, AsOfUtc, configuration);
        return (features, _detector.Evaluate(features, configuration));
    }

    private static TradeEvent[] GoldenEvents() =>
    [
        new(Symbol, SessionId, AsOfUtc.AddSeconds(-30), AsOfUtc, "RecordedFixture", "trade-1", 100m, 10),
        new(Symbol, SessionId, AsOfUtc.AddSeconds(-20), AsOfUtc, "RecordedFixture", "trade-2", 101m, 20),
        new(Symbol, SessionId, AsOfUtc.AddSeconds(-10), AsOfUtc, "RecordedFixture", "trade-3", 102m, 30),
        new(Symbol, SessionId, AsOfUtc.AddSeconds(-1), AsOfUtc, "RecordedFixture", "trade-4", 103m, 40)
    ];

    private FeatureSnapshot CalculateSlope(string symbol, string sessionId, decimal[] prices)
    {
        var state = CreateSlopeState(symbol, sessionId, prices);
        return _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration());
    }

    private static SymbolEventState CreateSlopeState(string symbol, string sessionId, decimal[] prices)
    {
        var state = new SymbolEventState(symbol, sessionId, 20);
        var eventTimes = prices.Length == 3
            ? new[]
            {
                AsOfUtc.AddSeconds(-40),
                AsOfUtc.AddSeconds(-10),
                AsOfUtc
            }
            : new[] { AsOfUtc.AddSeconds(-30) };
        for (var index = 0; index < prices.Length; index++)
        {
            var eventTime = eventTimes[index];
            state.Add(new TradeEvent(
                symbol,
                sessionId,
                eventTime,
                eventTime,
                "RecordedFixture",
                $"{symbol}-{sessionId}-{index}",
                prices[index],
                10));
        }

        return state;
    }

    private static SymbolEventState CreateSession(string sessionId, decimal startPrice)
    {
        var state = new SymbolEventState(Symbol, sessionId, 20);
        state.Add(new TradeEvent(
            Symbol, sessionId, AsOfUtc.AddSeconds(-30), AsOfUtc, "RecordedFixture", $"{sessionId}-1", startPrice, 10));
        state.Add(new TradeEvent(
            Symbol, sessionId, AsOfUtc.AddSeconds(-20), AsOfUtc, "RecordedFixture", $"{sessionId}-2", startPrice + 1m, 20));
        state.Add(new TradeEvent(
            Symbol, sessionId, AsOfUtc.AddSeconds(-10), AsOfUtc, "RecordedFixture", $"{sessionId}-3", startPrice + 2m, 30));
        state.Add(new TradeEvent(
            Symbol, sessionId, AsOfUtc.AddSeconds(-1), AsOfUtc, "RecordedFixture", $"{sessionId}-4", startPrice + 3m, 40));
        state.Add(new QuoteEvent(
            Symbol, sessionId, AsOfUtc, AsOfUtc, "RecordedFixture", $"{sessionId}-quote", startPrice + 2.99m, startPrice + 3.01m, 500, 500));
        return state;
    }
}
