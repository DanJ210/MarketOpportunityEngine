using MarketOpportunity.Analytics;
using MarketOpportunity.Domain;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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
        Assert.Equal(10_200m, first.Features.ShortHorizonDollarVolumeUsd);
        Assert.Equal(ShortHorizonDollarVolumeStatus.Available, first.Features.ShortHorizonDollarVolumeStatus);
        Assert.Equal(100, first.Features.SessionCumulativeVolume);
        Assert.Equal(103m, first.Features.SessionHighPrice);
        Assert.Equal(100m, first.Features.SessionLowPrice);
        Assert.Equal(OpportunityClassification.AvoidNoTrade, first.Opportunity.Classification);
        Assert.Contains("ExcessiveRealizedVolatility", first.Opportunity.ActiveVetoes);
        Assert.Contains("InsufficientDollarVolume", first.Opportunity.ActiveVetoes);
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
    public void Trend_direction_context_resolves_agreement_neutrality_and_conflicts()
    {
        var bullish = CalculateTrendDirection(
            fastEma: 101m,
            mediumEma: 100m,
            spread: 1m,
            fastSlope: 1m,
            mediumSlope: 2m,
            vwapSlope: 1m,
            vwapDirection: VwapSlopeDirection.Rising,
            structure: PriceStructure.HigherHighHigherLow,
            adx: 0m,
            adxStatus: AdxStatus.Available);
        var bearish = CalculateTrendDirection(
            fastEma: 99m,
            mediumEma: 100m,
            spread: -1m,
            fastSlope: -1m,
            mediumSlope: -2m,
            vwapSlope: -1m,
            vwapDirection: VwapSlopeDirection.Falling,
            structure: PriceStructure.LowerHighLowerLow,
            adx: 100m,
            adxStatus: AdxStatus.Available);
        var neutral = CalculateTrendDirection(
            fastEma: 100m,
            mediumEma: 100m,
            spread: 0m,
            fastSlope: 0m,
            mediumSlope: 0m,
            vwapSlope: 0m,
            vwapDirection: VwapSlopeDirection.Flat,
            structure: PriceStructure.Flat,
            adx: 50m,
            adxStatus: AdxStatus.Available);

        Assert.Equal(TrendDirectionContext.Bullish, bullish);
        Assert.Equal(TrendDirectionContext.Bearish, bearish);
        Assert.Equal(TrendDirectionContext.Neutral, neutral);
        Assert.Equal(TrendDirectionContext.Conflicting, CalculateTrendDirection(
            fastEma: 99m,
            mediumEma: 100m,
            spread: -1m,
            fastSlope: 1m,
            mediumSlope: 1m,
            vwapSlope: 1m,
            vwapDirection: VwapSlopeDirection.Rising,
            structure: PriceStructure.HigherHighHigherLow));
        Assert.Equal(TrendDirectionContext.Conflicting, CalculateTrendDirection(
            fastEma: 101m,
            mediumEma: 100m,
            spread: 1m,
            fastSlope: 1m,
            mediumSlope: 1m,
            vwapSlope: -1m,
            vwapDirection: VwapSlopeDirection.Falling,
            structure: PriceStructure.HigherHighHigherLow));
        Assert.Equal(TrendDirectionContext.Conflicting, CalculateTrendDirection(
            fastEma: 101m,
            mediumEma: 100m,
            spread: 1m,
            fastSlope: 1m,
            mediumSlope: 1m,
            vwapSlope: 1m,
            vwapDirection: VwapSlopeDirection.Rising,
            structure: PriceStructure.LowerHighLowerLow));
        Assert.Equal(TrendDirectionContext.Conflicting, CalculateTrendDirection(
            vwapSlope: 1m,
            vwapDirection: VwapSlopeDirection.Rising,
            structure: PriceStructure.Mixed));
    }

    [Fact]
    public void Trend_direction_context_distinguishes_missing_neutral_partial_and_invalid_inputs()
    {
        Assert.Equal(TrendDirectionContext.Insufficient, CalculateTrendDirection());
        Assert.Equal(TrendDirectionContext.Neutral, CalculateTrendDirection(
            vwapSlope: 0m,
            vwapDirection: VwapSlopeDirection.Flat));
        Assert.Equal(TrendDirectionContext.Bullish, CalculateTrendDirection(
            fastEma: 100m,
            fastSlope: 1m));
        Assert.Equal(TrendDirectionContext.Invalid, CalculateTrendDirection(
            fastEma: 100m,
            spread: 1m));
        Assert.Equal(TrendDirectionContext.Invalid, CalculateTrendDirection(
            vwapSlope: 1m,
            vwapDirection: (VwapSlopeDirection)999));
        Assert.Equal(TrendDirectionContext.Invalid, CalculateTrendDirection(
            structure: (PriceStructure)999));
        Assert.Equal(TrendDirectionContext.Invalid, CalculateTrendDirection(
            adxStatus: AdxStatus.InvalidPriceData));
        Assert.Equal(TrendDirectionContext.Invalid, CalculateTrendDirection(
            adx: 101m,
            adxStatus: AdxStatus.Available));

        var risingEvents = CreateSlopeState(Symbol, SessionId, [100m, 101m, 102m]).GetEventsAsOf(AsOfUtc);
        var completeState = new SymbolEventState(Symbol, SessionId, 3);
        var partialState = new SymbolEventState(Symbol, SessionId, 2);
        foreach (var marketEvent in risingEvents)
        {
            completeState.Add(marketEvent);
            partialState.Add(marketEvent);
        }

        var complete = _calculator.Calculate(completeState, AsOfUtc, new ScannerConfiguration());
        var partial = _calculator.Calculate(partialState, AsOfUtc, new ScannerConfiguration());
        Assert.True(complete.HasCompleteSessionData);
        Assert.False(partial.HasCompleteSessionData);
        Assert.Equal(TrendDirectionContext.Bullish, complete.SessionTrendDirection);
        Assert.Equal(complete.SessionTrendDirection, partial.SessionTrendDirection);
    }

    [Fact]
    public void Trend_direction_context_ignores_adx_magnitude_and_is_descriptive_only()
    {
        var configuration = new ScannerConfiguration();
        var result = Replay(GoldenEvents(), configuration);
        var baseline = JsonSerializer.Serialize(result.Opportunity);
        foreach (var adx in new decimal?[] { null, 0m, 25m, 100m })
        {
            var status = adx.HasValue ? AdxStatus.Available : AdxStatus.Insufficient;
            var direction = CalculateTrendDirection(
                fastEma: 101m,
                mediumEma: 100m,
                spread: 1m,
                fastSlope: 1m,
                mediumSlope: 1m,
                vwapSlope: 1m,
                vwapDirection: VwapSlopeDirection.Rising,
                structure: PriceStructure.HigherHighHigherLow,
                adx: adx,
                adxStatus: status);
            Assert.Equal(TrendDirectionContext.Bullish, direction);
        }

        foreach (var context in Enum.GetValues<TrendDirectionContext>())
        {
            var changed = result.Features with { SessionTrendDirection = context };
            Assert.Equal(baseline, JsonSerializer.Serialize(_detector.Evaluate(changed, configuration)));
        }

        var changedSources = result.Features with
        {
            FastEma = 101m,
            MediumEma = 100m,
            FastMediumEmaSpreadPercent = 1m,
            FastEmaSlopePerMinute = 1m,
            MediumEmaSlopePerMinute = 1m,
            SessionVwapSlopePerMinute = -1m,
            SessionVwapSlopeDirection = VwapSlopeDirection.Falling,
            SessionPriceStructure = PriceStructure.Mixed,
            SessionAdx = 100m,
            SessionAdxStatus = AdxStatus.Available,
            SessionTrendDirection = TrendDirectionContext.Conflicting
        };
        Assert.Equal(baseline, JsonSerializer.Serialize(_detector.Evaluate(changedSources, configuration)));
    }

    [Fact]
    public void Trend_direction_context_serializes_and_round_trips_each_explicit_state()
    {
        var features = Replay(GoldenEvents(), new ScannerConfiguration()).Features;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };
        foreach (var context in Enum.GetValues<TrendDirectionContext>())
        {
            var json = JsonSerializer.Serialize(
                features with { SessionTrendDirection = context },
                options);
            using var document = JsonDocument.Parse(json);
            Assert.Equal(context.ToString(),
                document.RootElement.GetProperty("sessionTrendDirection").GetString());
            Assert.Equal(
                features with { SessionTrendDirection = context },
                JsonSerializer.Deserialize<FeatureSnapshot>(json, options));
        }
    }

    private static TrendDirectionContext CalculateTrendDirection(
        decimal? fastEma = null,
        decimal? mediumEma = null,
        decimal? spread = null,
        decimal? fastSlope = null,
        decimal? mediumSlope = null,
        decimal? vwapSlope = null,
        VwapSlopeDirection vwapDirection = VwapSlopeDirection.Insufficient,
        PriceStructure? structure = null,
        decimal? adx = null,
        AdxStatus adxStatus = AdxStatus.Insufficient) =>
        TrendDirectionContextCalculator.Calculate(
            fastEma,
            mediumEma,
            spread,
            fastSlope,
            mediumSlope,
            vwapSlope,
            vwapDirection,
            structure,
            adx,
            adxStatus);

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
        Assert.Equal(100, duplicated.Features.SessionCumulativeVolume);
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
    public void Ema_paths_report_exact_fast_medium_relationship_and_event_time_slopes()
    {
        var configuration = new ScannerConfiguration
        {
            EmaFastPeriod = 2,
            EmaMediumPeriod = 3
        };
        var rising = CalculateEmaFeatures([100m, 102m, 104m, 106m], configuration);
        var falling = CalculateEmaFeatures([106m, 104m, 102m, 100m], configuration);
        var flat = CalculateEmaFeatures([100m, 100m, 100m, 100m], configuration);

        Assert.Equal(105m, rising.FastEma);
        Assert.Equal(104m, rising.MediumEma);
        Assert.Equal(1m / 104m * 100m, rising.FastMediumEmaSpreadPercent);
        Assert.Equal(12m, rising.FastEmaSlopePerMinute);
        Assert.Equal(12m, rising.MediumEmaSlopePerMinute);
        Assert.Equal(101m, falling.FastEma);
        Assert.Equal(102m, falling.MediumEma);
        Assert.Equal(-1m / 102m * 100m, falling.FastMediumEmaSpreadPercent);
        Assert.Equal(-12m, falling.FastEmaSlopePerMinute);
        Assert.Equal(-12m, falling.MediumEmaSlopePerMinute);
        Assert.Equal(100m, flat.FastEma);
        Assert.Equal(100m, flat.MediumEma);
        Assert.Equal(0m, flat.FastMediumEmaSpreadPercent);
        Assert.Equal(0m, flat.FastEmaSlopePerMinute);
        Assert.Equal(0m, flat.MediumEmaSlopePerMinute);
    }

    [Fact]
    public void Ema_uses_sma_seed_and_returns_null_until_each_period_is_warmed_up()
    {
        var configuration = new ScannerConfiguration
        {
            EmaFastPeriod = 2,
            EmaMediumPeriod = 4
        };
        var oneTrade = CalculateEmaFeatures([100m], configuration);
        var twoTrades = CalculateEmaFeatures([100m, 102m], configuration);
        var threeTrades = CalculateEmaFeatures([100m, 102m, 104m], configuration);
        var fourTrades = CalculateEmaFeatures([100m, 102m, 104m, 106m], configuration);

        Assert.Null(oneTrade.FastEma);
        Assert.Null(oneTrade.MediumEma);
        Assert.Null(oneTrade.FastMediumEmaSpreadPercent);
        Assert.Null(twoTrades.FastEmaSlopePerMinute);
        Assert.Equal(101m, twoTrades.FastEma);
        Assert.Null(twoTrades.MediumEma);
        Assert.Equal(103m, threeTrades.FastEma);
        Assert.Null(threeTrades.MediumEma);
        Assert.Null(threeTrades.FastMediumEmaSpreadPercent);
        Assert.Equal(103m, fourTrades.MediumEma);
        Assert.Equal(105m, fourTrades.FastEma);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(5, 0)]
    [InlineData(5, 5)]
    [InlineData(20, 5)]
    public void Ema_periods_must_be_positive_and_fast_must_be_less_than_medium(
        int fastPeriod,
        int mediumPeriod)
    {
        var configuration = new ScannerConfiguration
        {
            EmaFastPeriod = fastPeriod,
            EmaMediumPeriod = mediumPeriod
        };

        Assert.Throws<InvalidOperationException>(configuration.Validate);
    }

    [Fact]
    public void Ema_slope_uses_inclusive_event_time_bounds_and_last_canonical_sample_at_equal_times()
    {
        var configuration = new ScannerConfiguration
        {
            EmaFastPeriod = 1,
            EmaMediumPeriod = 2
        };
        var state = new SymbolEventState(Symbol, SessionId, 20);
        state.Add(new TradeEvent(Symbol, SessionId, AsOfUtc.AddSeconds(-60).AddTicks(-1), AsOfUtc,
            "Fixture", "before-window", 1m, 1));
        state.Add(new TradeEvent(Symbol, SessionId, AsOfUtc.AddSeconds(-60), AsOfUtc,
            "Fixture", "a-first-at-boundary", 100m, 1));
        state.Add(new TradeEvent(Symbol, SessionId, AsOfUtc.AddSeconds(-60), AsOfUtc,
            "Fixture", "z-last-at-boundary", 102m, 1));
        state.Add(new TradeEvent(Symbol, SessionId, AsOfUtc, AsOfUtc,
            "Fixture", "at-as-of", 108m, 1));
        state.Add(new TradeEvent(Symbol, SessionId, AsOfUtc.AddTicks(1), AsOfUtc.AddDays(-1),
            "Fixture", "after-as-of", 1000m, 1));

        var features = _calculator.Calculate(state, AsOfUtc, configuration);
        var reorderedState = new SymbolEventState(Symbol, SessionId, 20);
        foreach (var marketEvent in state.GetEventsAsOf(AsOfUtc).Reverse())
        {
            reorderedState.Add(marketEvent with { ReceivedTimeUtc = AsOfUtc.AddYears(1) });
        }
        var reordered = _calculator.Calculate(reorderedState, AsOfUtc, configuration);

        Assert.Equal(108m, features.FastEma);
        Assert.Equal(6m, features.FastEmaSlopePerMinute);
        Assert.Equal(100.27777777777777777777777778m, features.MediumEma);
        Assert.Equal(15.44444444444444444444444444m, features.MediumEmaSlopePerMinute);
        Assert.Equal(features, reordered);
    }

    [Fact]
    public void Ema_slope_is_null_for_equal_timestamp_samples_even_when_the_ema_is_warm()
    {
        var configuration = new ScannerConfiguration
        {
            EmaFastPeriod = 1,
            EmaMediumPeriod = 2
        };
        var trades = new[]
        {
            new TradeEvent(Symbol, SessionId, AsOfUtc, AsOfUtc, "Fixture", "a", 100m, 1),
            new TradeEvent(Symbol, SessionId, AsOfUtc, AsOfUtc, "Fixture", "b", 101m, 1),
            new TradeEvent(Symbol, SessionId, AsOfUtc, AsOfUtc, "Fixture", "c", 102m, 1)
        };

        var features = CalculateEmaFeatures(trades, configuration);

        Assert.NotNull(features.FastEma);
        Assert.NotNull(features.MediumEma);
        Assert.Null(features.FastEmaSlopePerMinute);
        Assert.Null(features.MediumEmaSlopePerMinute);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Ema_is_unavailable_for_non_positive_eligible_prices_but_ignores_size(long ignored)
    {
        var configuration = new ScannerConfiguration
        {
            EmaFastPeriod = 2,
            EmaMediumPeriod = 3
        };
        var validPricesWithInvalidSizes = GoldenEvents()
            .Select((trade, index) => trade with { Size = index == 1 ? ignored : trade.Size })
            .ToArray();
        var sizedFeatures = CalculateEmaFeatures(validPricesWithInvalidSizes, configuration);
        Assert.NotNull(sizedFeatures.FastEma);
        Assert.NotNull(sizedFeatures.MediumEma);
        foreach (var invalidPrice in new[] { 0m, -1m })
        {
            var invalidPriceFeatures = CalculateEmaFeatures(
                [.. validPricesWithInvalidSizes, validPricesWithInvalidSizes[0] with
                {
                    SequenceId = $"invalid-session-price-{invalidPrice}",
                    Price = invalidPrice,
                    EventTimeUtc = AsOfUtc
                }],
                configuration);

            Assert.Null(invalidPriceFeatures.FastEma);
            Assert.Null(invalidPriceFeatures.MediumEma);
            Assert.Null(invalidPriceFeatures.FastMediumEmaSpreadPercent);
            Assert.Null(invalidPriceFeatures.FastEmaSlopePerMinute);
            Assert.Null(invalidPriceFeatures.MediumEmaSlopePerMinute);
        }
    }

    [Fact]
    public void Ema_uses_trades_only_and_isolated_symbol_and_session_state()
    {
        var configuration = new ScannerConfiguration { EmaFastPeriod = 2, EmaMediumPeriod = 3 };
        var trades = new[] { 100m, 102m, 104m, 106m };
        var primary = CalculateEmaFeatures(trades, configuration, Symbol, SessionId);
        var otherSession = CalculateEmaFeatures(trades.Reverse().ToArray(), configuration, Symbol, "other");
        var otherSymbol = CalculateEmaFeatures(trades.Reverse().ToArray(), configuration, "SILVER", SessionId);

        var withQuote = CalculateEmaFeatures(
            [.. CreateEmaTrades(trades, Symbol, SessionId), new QuoteEvent(Symbol, SessionId, AsOfUtc, AsOfUtc,
                "Fixture", "extreme-quote", -1000m, 1000m, 1, 1)],
            configuration);

        Assert.Equal(105m, primary.FastEma);
        Assert.Equal(101m, otherSession.FastEma);
        Assert.Equal(otherSession.FastEma, otherSymbol.FastEma);
        Assert.Equal(primary.FastEma, withQuote.FastEma);
        Assert.Equal(primary.MediumEma, withQuote.MediumEma);
        Assert.Equal(primary.FastEmaSlopePerMinute, withQuote.FastEmaSlopePerMinute);
    }

    [Fact]
    public void Ema_replay_is_stable_for_reordering_duplicates_and_receive_time_changes()
    {
        var configuration = new ScannerConfiguration { EmaFastPeriod = 2, EmaMediumPeriod = 3 };
        var input = CreateEmaTrades([100m, 102m, 104m, 106m]);
        var baseline = CalculateEmaFeatures(input, configuration);
        var reordered = CalculateEmaFeatures(input.Reverse().ToArray(), configuration);
        var duplicate = CalculateEmaFeatures(
            [.. input, input[0] with { Price = 999m, Size = 999 }],
            configuration);
        var receivedLater = CalculateEmaFeatures(
            input.Select(item => item with { ReceivedTimeUtc = AsOfUtc.AddYears(1) }).ToArray(),
            configuration);

        Assert.Equal(baseline, reordered);
        Assert.Equal(baseline, duplicate);
        Assert.Equal(baseline, receivedLater);

        var conflictingFirst = CalculateEmaFeatures(
            [input[0] with { Price = 999m }, .. input],
            configuration);
        Assert.NotEqual(baseline.FastEma, conflictingFirst.FastEma);
        Assert.NotEqual(baseline.Id, conflictingFirst.Id);
    }

    [Fact]
    public void Ema_future_cutoff_is_inclusive_and_eviction_marks_retained_values_partial()
    {
        var configuration = new ScannerConfiguration { EmaFastPeriod = 2, EmaMediumPeriod = 3 };
        var state = new SymbolEventState(Symbol, SessionId, 20);
        foreach (var trade in CreateEmaTrades([100m, 102m, 104m, 106m], Symbol, SessionId))
        {
            state.Add(trade);
        }

        var baseline = _calculator.Calculate(state, AsOfUtc, configuration);
        state.Add(new TradeEvent(Symbol, SessionId, AsOfUtc.AddTicks(1), AsOfUtc,
            "Fixture", "future", 1000m, 1));
        Assert.Equal(baseline, _calculator.Calculate(state, AsOfUtc, configuration));
        Assert.Equal(baseline.FastEma, _calculator.Calculate(state, AsOfUtc, configuration).FastEma);

        var bounded = new SymbolEventState(Symbol, SessionId, 4);
        foreach (var trade in CreateEmaTrades([100m, 100m, 104m, 110m], Symbol, SessionId))
        {
            bounded.Add(trade);
        }

        var complete = _calculator.Calculate(bounded, AsOfUtc, configuration);
        bounded.Add(new TradeEvent(Symbol, SessionId, AsOfUtc.AddSeconds(1), AsOfUtc,
            "Fixture", "future-evicts", 108m, 1));
        var partial = _calculator.Calculate(bounded, AsOfUtc, configuration);

        Assert.True(complete.HasCompleteSessionData);
        Assert.False(partial.HasCompleteSessionData);
        Assert.NotEqual(complete.FastEma, partial.FastEma);
    }

    [Fact]
    public void Ema_fields_round_trip_as_json_and_remain_detector_neutral()
    {
        var configuration = new ScannerConfiguration { EmaFastPeriod = 2, EmaMediumPeriod = 3 };
        var result = Replay(GoldenEvents(), configuration);
        var populated = result.Features with
        {
            FastEma = 102.5m,
            MediumEma = 101.5m,
            FastMediumEmaSpreadPercent = 100m / 101.5m,
            FastEmaSlopePerMinute = 2m,
            MediumEmaSlopePerMinute = -1m
        };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var roundTrip = JsonSerializer.Deserialize<FeatureSnapshot>(
            JsonSerializer.Serialize(populated, options), options);
        Assert.Equal(populated, roundTrip);

        var nullRoundTrip = JsonSerializer.Deserialize<FeatureSnapshot>(
            JsonSerializer.Serialize(Replay(GoldenEvents(), new ScannerConfiguration()).Features, options), options);
        Assert.Equal(Replay(GoldenEvents(), new ScannerConfiguration()).Features, nullRoundTrip);
        Assert.Null(nullRoundTrip!.FastEma);
        Assert.Null(nullRoundTrip.MediumEma);

        var baselineJson = JsonSerializer.Serialize(result.Opportunity, options);
        foreach (var values in new (decimal? Fast, decimal? Medium, decimal? Spread, decimal? FastSlope, decimal? MediumSlope)[]
        {
            (null, null, null, null, null),
            (0m, 0m, 0m, 0m, 0m),
            (123.45m, 100m, 23.45m, -5m, 1m)
        })
        {
            var snapshot = result.Features with
            {
                FastEma = values.Fast,
                MediumEma = values.Medium,
                FastMediumEmaSpreadPercent = values.Spread,
                FastEmaSlopePerMinute = values.FastSlope,
                MediumEmaSlopePerMinute = values.MediumSlope
            };
            var opportunity = _detector.Evaluate(snapshot, configuration);

            Assert.Equal(baselineJson, JsonSerializer.Serialize(opportunity, options));
        }
    }

    [Fact]
    public void Ema_outputs_participate_in_deterministic_feature_identity()
    {
        var configuration = new ScannerConfiguration();
        var baselineState = new SymbolEventState(Symbol, SessionId, 20);
        var changedState = new SymbolEventState(Symbol, SessionId, 20);
        var oldPrices = new[] { 100m, 101m, 102m, 103m, 104m };
        var changedOldPrices = new[] { 100m, 102m, 102m, 103m, 104m };
        for (var index = 0; index < oldPrices.Length; index++)
        {
            var time = AsOfUtc.AddSeconds(-120 + index * 10);
            baselineState.Add(new TradeEvent(Symbol, SessionId, time, AsOfUtc, "Fixture",
                $"identity-old-{index}", oldPrices[index], 0));
            changedState.Add(new TradeEvent(Symbol, SessionId, time, AsOfUtc, "Fixture",
                $"identity-old-{index}", changedOldPrices[index], 0));
        }

        foreach (var trade in GoldenEvents())
        {
            baselineState.Add(trade);
            changedState.Add(trade);
        }

        var baseline = _calculator.Calculate(baselineState, AsOfUtc, configuration);
        var changed = _calculator.Calculate(changedState, AsOfUtc, configuration);

        Assert.NotEqual(baseline.FastEma, changed.FastEma);
        Assert.NotEqual(baseline.Id, changed.Id);
        Assert.Equal(
            baseline with
            {
                Id = Guid.Empty,
                SessionTrendDirection = TrendDirectionContext.Insufficient,
                SessionRealizedVolatilityPercent = null,
                SessionRealizedVolatilityStatus = RealizedVolatilityStatus.Insufficient,
                FastEma = null,
                MediumEma = null,
                FastMediumEmaSpreadPercent = null,
                FastEmaSlopePerMinute = null,
                MediumEmaSlopePerMinute = null
            },
            changed with
            {
                Id = Guid.Empty,
                SessionTrendDirection = TrendDirectionContext.Insufficient,
                SessionRealizedVolatilityPercent = null,
                SessionRealizedVolatilityStatus = RealizedVolatilityStatus.Insufficient,
                FastEma = null,
                MediumEma = null,
                FastMediumEmaSpreadPercent = null,
                FastEmaSlopePerMinute = null,
                MediumEmaSlopePerMinute = null
            });
    }

    [Fact]
    public void Changed_configuration_version_creates_distinct_feature_and_opportunity_identities()
    {
        var firstConfiguration = new ScannerConfiguration();
        var changedConfiguration = firstConfiguration with { Version = "scanner-config-alternate" };
        var first = Replay(GoldenEvents(), firstConfiguration);
        var changed = Replay(GoldenEvents(), changedConfiguration);

        Assert.NotEqual(first.Features.Id, changed.Features.Id);
        Assert.NotEqual(first.Opportunity.Id, changed.Opportunity.Id);
    }

    [Fact]
    public void Session_structure_classifies_higher_lower_mixed_and_flat_confirmed_swings()
    {
        var rising = CalculateStructureFeatures(CreateStructureTrades(
            [1.5m, 1m, 5m, 3m, 2m, 4m, 3m, 7m, 5m, 4m, 6m, 5m]));
        var falling = CalculateStructureFeatures(CreateStructureTrades(
            [5m, 6m, 7m, 6m, 3m, 4m, 5m, 4m, 2m, 1m, 3m, 5m]));
        var mixed = CalculateStructureFeatures(CreateStructureTrades(
            [5m, 6m, 7m, 5m, 4m, 6m, 5m, 8m, 4m, 3m, 1m, 4m, 3m]));
        var flat = CalculateStructureFeatures(CreateStructureTrades(
            [1.5m, 1m, 5m, 3m, 2m, 4m, 3m, 5m, 5m, 4m, 6m, 5m]));
        var exactIncrease = CalculateStructureFeatures(CreateStructureTrades(
            [1.5m, 1m, 5m, 3m, 2m, 4m, 3m, 5.0000000000000000000000000001m, 5m, 4m, 6m, 5m]));

        Assert.Equal(PriceStructure.HigherHighHigherLow, rising.SessionPriceStructure);
        Assert.Equal(PriceStructure.LowerHighLowerLow, falling.SessionPriceStructure);
        Assert.Equal(PriceStructure.Mixed, mixed.SessionPriceStructure);
        Assert.Equal(PriceStructure.Flat, flat.SessionPriceStructure);
        Assert.Equal(PriceStructure.HigherHighHigherLow, exactIncrease.SessionPriceStructure);
    }

    [Fact]
    public void Session_structure_requires_two_confirmed_pivots_and_confirms_after_two_later_trades()
    {
        var trades = CreateStructureTrades([1.5m, 1m, 5m, 3m, 2m, 4m, 3m, 7m, 5m, 4m, 6m, 5m]);
        var beforeConfirmation = CalculateStructureFeatures(trades, trades[9].EventTimeUtc);
        var afterOneConfirmationTrade = CalculateStructureFeatures(trades, trades[10].EventTimeUtc);
        var atSecondConfirmationTrade = CalculateStructureFeatures(trades, trades[11].EventTimeUtc);
        var justBeforeSecondConfirmationTrade =
            CalculateStructureFeatures(trades, trades[11].EventTimeUtc.AddTicks(-1));

        Assert.Null(beforeConfirmation.SessionPriceStructure);
        Assert.Null(afterOneConfirmationTrade.SessionPriceStructure);
        Assert.Null(justBeforeSecondConfirmationTrade.SessionPriceStructure);
        Assert.Equal(PriceStructure.HigherHighHigherLow, atSecondConfirmationTrade.SessionPriceStructure);
        Assert.Equal(TimeSpan.FromSeconds(20), trades[11].EventTimeUtc - trades[9].EventTimeUtc);

        Assert.Null(CalculateStructureFeatures(CreateStructureTrades([100m])).SessionPriceStructure);
        Assert.Null(CalculateStructureFeatures(CreateStructureTrades([100m, 101m, 100m, 102m]))
            .SessionPriceStructure);
    }

    [Fact]
    public void Session_structure_uses_inclusive_as_of_and_canonical_order_not_time_spacing_or_receive_time()
    {
        var prices = new[] { 1.5m, 1m, 5m, 3m, 2m, 4m, 3m, 7m, 5m, 4m, 6m, 5m };
        var trades = CreateStructureTrades(prices, equalTimestamps: true);
        var baseline = CalculateStructureFeatures(trades);
        var reordered = CalculateStructureFeatures(trades
            .Reverse()
            .Select((trade, index) => trade with
            {
                ReceivedTimeUtc = AsOfUtc.AddYears(index + 1)
            })
            .ToArray());
        var timeSpaced = CalculateStructureFeatures(trades
            .Select((trade, index) => trade with
            {
                EventTimeUtc = AsOfUtc.AddSeconds(-((trades.Length - index) * (trades.Length - index) * 3))
            })
            .ToArray());

        Assert.Equal(PriceStructure.HigherHighHigherLow, baseline.SessionPriceStructure);
        Assert.Equal(baseline, reordered);
        Assert.Equal(baseline.SessionPriceStructure, timeSpaced.SessionPriceStructure);

        var state = new SymbolEventState(Symbol, SessionId, 20);
        foreach (var trade in trades)
        {
            Assert.True(state.Add(trade));
        }

        Assert.False(state.Add(trades[0] with
        {
            Price = 900m,
            Size = -1,
            ReceivedTimeUtc = AsOfUtc.AddYears(2)
        }));
        Assert.Equal(baseline, _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration()));
    }

    [Fact]
    public void Session_structure_ignores_trade_size_and_quotes_and_isolated_states_remain_independent()
    {
        var prices = new[] { 1.5m, 1m, 5m, 3m, 2m, 4m, 3m, 7m, 5m, 4m, 6m, 5m };
        var trades = CreateStructureTrades(prices);
        var baseline = CalculateStructureFeatures(trades);
        var changedSizes = CalculateStructureFeatures(trades
            .Select((trade, index) => trade with { Size = index % 2 == 0 ? 0 : -100 })
            .ToArray());
        var quotedState = new SymbolEventState(Symbol, SessionId, 100);
        foreach (var trade in trades)
        {
            quotedState.Add(trade);
        }

        quotedState.Add(new QuoteEvent(
            Symbol, SessionId, AsOfUtc, AsOfUtc, "Fixture", "structure-extreme-quote",
            100_000m, 100_001m, -1, 0));
        var withQuote = _calculator.Calculate(quotedState, AsOfUtc, new ScannerConfiguration());
        var otherSession = CalculateStructureFeatures(
            CreateStructureTrades([5m, 6m, 7m, 6m, 3m, 4m, 5m, 4m, 2m, 1m, 3m, 5m],
                sessionId: "structure-other-session"));
        var otherSymbol = CalculateStructureFeatures(
            CreateStructureTrades([5m, 6m, 7m, 5m, 4m, 6m, 5m, 8m, 4m, 3m, 1m, 4m, 3m],
                symbol: "SILVER"));

        Assert.Equal(PriceStructure.HigherHighHigherLow, baseline.SessionPriceStructure);
        Assert.Equal(baseline.SessionPriceStructure, changedSizes.SessionPriceStructure);
        Assert.Equal(baseline.SessionPriceStructure, withQuote.SessionPriceStructure);
        Assert.Equal(PriceStructure.LowerHighLowerLow, otherSession.SessionPriceStructure);
        Assert.Equal(PriceStructure.Mixed, otherSymbol.SessionPriceStructure);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Session_structure_is_null_when_any_eligible_trade_price_is_non_positive(int invalidPriceValue)
    {
        var invalidPrice = (decimal)invalidPriceValue;
        var validTrades = CreateStructureTrades([1.5m, 1m, 5m, 3m, 2m, 4m, 3m, 7m, 5m, 4m, 6m, 5m]);
        var invalid = validTrades
            .Append(validTrades[0] with
            {
                SequenceId = $"invalid-structure-{invalidPrice}",
                EventTimeUtc = AsOfUtc,
                Price = invalidPrice
            })
            .ToArray();
        var invalidFeature = CalculateStructureFeatures(invalid);
        var futureInvalidFeature = CalculateStructureFeatures(
            [.. validTrades, validTrades[0] with
            {
                SequenceId = "future-invalid-structure",
                EventTimeUtc = AsOfUtc.AddTicks(1),
                Price = invalidPrice
            }]);

        Assert.Null(invalidFeature.SessionPriceStructure);
        Assert.Equal(PriceStructure.HigherHighHigherLow, futureInvalidFeature.SessionPriceStructure);
        Assert.Equal(CalculateStructureFeatures(validTrades), futureInvalidFeature);
    }

    [Fact]
    public void Session_structure_uses_partial_retained_trades_after_future_arrival_evicts_eligible_state()
    {
        var configuration = new ScannerConfiguration { MaximumStateEvents = 12 };
        var trades = CreateStructureTrades([1.5m, 1m, 5m, 3m, 2m, 4m, 3m, 7m, 5m, 4m, 6m, 5m]);
        var state = new SymbolEventState(Symbol, SessionId, configuration.MaximumStateEvents);
        foreach (var trade in trades)
        {
            state.Add(trade);
        }

        var complete = _calculator.Calculate(state, AsOfUtc, configuration);
        state.Add(new TradeEvent(
            Symbol, SessionId, AsOfUtc.AddTicks(1), AsOfUtc.AddTicks(1),
            "Fixture", "future-evicts-structure", 10_000m, 1));
        var retainedPartial = _calculator.Calculate(state, AsOfUtc, configuration);

        Assert.True(complete.HasCompleteSessionData);
        Assert.Equal(PriceStructure.HigherHighHigherLow, complete.SessionPriceStructure);
        Assert.False(retainedPartial.HasCompleteSessionData);
        Assert.Null(retainedPartial.SessionPriceStructure);
    }

    [Fact]
    public void Session_structure_json_round_trips_null_and_values_and_never_changes_opportunity_output()
    {
        var configuration = new ScannerConfiguration();
        var result = Replay(GoldenEvents(), configuration);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var serializedNull = JsonSerializer.Serialize(result.Features, options);
        var nullRoundTrip = JsonSerializer.Deserialize<FeatureSnapshot>(serializedNull, options);
        Assert.Equal(result.Features, nullRoundTrip);
        using (var json = JsonDocument.Parse(serializedNull))
        {
            Assert.Equal(JsonValueKind.Null,
                json.RootElement.GetProperty("sessionPriceStructure").ValueKind);
        }

        var populated = result.Features with
        {
            SessionPriceStructure = PriceStructure.HigherHighHigherLow
        };
        var populatedJson = JsonSerializer.Serialize(populated, options);
        Assert.Equal(populated, JsonSerializer.Deserialize<FeatureSnapshot>(populatedJson, options));
        using (var json = JsonDocument.Parse(populatedJson))
        {
            Assert.Equal((int)PriceStructure.HigherHighHigherLow,
                json.RootElement.GetProperty("sessionPriceStructure").GetInt32());
        }
        var apiOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        apiOptions.Converters.Add(new JsonStringEnumConverter());
        var apiJson = JsonSerializer.Serialize(populated, apiOptions);
        using (var json = JsonDocument.Parse(apiJson))
        {
            Assert.Equal("HigherHighHigherLow",
                json.RootElement.GetProperty("sessionPriceStructure").GetString());
        }
        Assert.Equal(populated, JsonSerializer.Deserialize<FeatureSnapshot>(apiJson, apiOptions));

        var baselineOpportunityJson = JsonSerializer.Serialize(result.Opportunity, options);
        foreach (var structure in new PriceStructure?[]
        {
            null,
            PriceStructure.HigherHighHigherLow,
            PriceStructure.LowerHighLowerLow,
            PriceStructure.Mixed,
            PriceStructure.Flat
        })
        {
            var opportunity = _detector.Evaluate(
                result.Features with { SessionPriceStructure = structure },
                configuration);
            Assert.Equal(baselineOpportunityJson, JsonSerializer.Serialize(opportunity, options));
        }
    }

    [Fact]
    public void Session_structure_participates_explicitly_in_deterministic_feature_identity()
    {
        var configuration = new ScannerConfiguration
        {
            EmaFastPeriod = 100,
            EmaMediumPeriod = 101
        };
        var risingPrices = new[] { 1.5m, 1m, 5m, 3m, 2m, 4m, 3m, 7m, 5m, 4m, 6m, 5m };
        var fallingPrices = new[] { 5m, 6m, 7m, 6m, 3m, 4m, 5m, 4m, 2m, 1m, 3m, 5m };
        var rising = CalculateStructureFeatures(
            CreateStructureTrades(risingPrices, firstEventTimeUtc: AsOfUtc.AddSeconds(-120), interval: TimeSpan.FromSeconds(1), size: 0),
            configuration: configuration);
        var falling = CalculateStructureFeatures(
            CreateStructureTrades(fallingPrices, firstEventTimeUtc: AsOfUtc.AddSeconds(-120), interval: TimeSpan.FromSeconds(1), size: 0),
            configuration: configuration);

        Assert.Equal(PriceStructure.HigherHighHigherLow, rising.SessionPriceStructure);
        Assert.Equal(PriceStructure.LowerHighLowerLow, falling.SessionPriceStructure);
        Assert.NotEqual(rising.Id, falling.Id);
        Assert.Equal(
            rising with
            {
                Id = Guid.Empty,
                SessionPriceStructure = null,
                LatestConfirmedSwingHigh = null,
                LatestConfirmedSwingLow = null,
                PreviousConfirmedSwingHigh = null,
                PreviousConfirmedSwingLow = null,
                StructureState = MarketStructureState.NotComputed,
                HighSwingRelationship = SwingRelationship.NotComputed,
                LowSwingRelationship = SwingRelationship.NotComputed,
                NearestSupportCandidate = null,
                NearestResistanceCandidate = null,
                DistanceToNearestConfirmedSwingHigh = null,
                DistanceToNearestConfirmedSwingHighPercent = null,
                DistanceToNearestConfirmedSwingLow = null,
                DistanceToNearestConfirmedSwingLowPercent = null,
                SessionRealizedVolatilityPercent = null,
                SessionRealizedVolatilityStatus = RealizedVolatilityStatus.Insufficient,
                SessionTrendDirection = TrendDirectionContext.Insufficient
            },
            falling with
            {
                Id = Guid.Empty,
                SessionPriceStructure = null,
                LatestConfirmedSwingHigh = null,
                LatestConfirmedSwingLow = null,
                PreviousConfirmedSwingHigh = null,
                PreviousConfirmedSwingLow = null,
                StructureState = MarketStructureState.NotComputed,
                HighSwingRelationship = SwingRelationship.NotComputed,
                LowSwingRelationship = SwingRelationship.NotComputed,
                NearestSupportCandidate = null,
                NearestResistanceCandidate = null,
                DistanceToNearestConfirmedSwingHigh = null,
                DistanceToNearestConfirmedSwingHighPercent = null,
                DistanceToNearestConfirmedSwingLow = null,
                DistanceToNearestConfirmedSwingLowPercent = null,
                SessionRealizedVolatilityPercent = null,
                SessionRealizedVolatilityStatus = RealizedVolatilityStatus.Insufficient,
                SessionTrendDirection = TrendDirectionContext.Insufficient
            });
    }

    [Fact]
    public void Adx_uses_exact_wilder_trade_point_initialization_and_smoothing()
    {
        var configuration = new ScannerConfiguration { TrendStrengthPeriod = 2 };
        var rising = CalculateAdxFeatures([10m, 11m, 12m, 13m], configuration);
        var falling = CalculateAdxFeatures([13m, 12m, 11m, 10m], configuration);
        var flat = CalculateAdxFeatures([10m, 10m, 10m, 10m], configuration);
        var alternating = CalculateAdxFeatures([10m, 12m, 10m, 12m], configuration);
        var smoothed = CalculateAdxFeatures([10m, 12m, 10m, 12m, 14m], configuration);

        Assert.Equal(100m, rising.SessionAdx);
        Assert.Equal(AdxStatus.Available, rising.SessionAdxStatus);
        Assert.Equal(100m, falling.SessionAdx);
        Assert.Equal(AdxStatus.Available, falling.SessionAdxStatus);
        Assert.Equal(0m, flat.SessionAdx);
        Assert.Equal(25m, alternating.SessionAdx);
        Assert.Equal(50m, smoothed.SessionAdx);
    }

    [Fact]
    public void Session_realized_volatility_uses_full_retained_trade_point_log_returns()
    {
        var configuration = new ScannerConfiguration();
        var rising = CalculateAdxFeatures([100m, 110m], configuration);
        var falling = CalculateAdxFeatures([100m, 90m], configuration);
        var unchanged = CalculateAdxFeatures([100m, 100m, 100m], configuration);
        var sizeAndQuoteIndependent = CalculateAdxFeatures(
            CreateAdxTrades([100m, 110m])
                .Select(trade => trade with { Size = 0 })
                .Cast<MarketEvent>()
                .Append(new QuoteEvent(
                    Symbol, SessionId, AsOfUtc, AsOfUtc, "Fixture", "volatility-quote",
                    1_000m, 1_001m, 0, 0))
                .ToArray(),
            configuration);

        Assert.Equal(9.531017980m, rising.SessionRealizedVolatilityPercent);
        Assert.Equal(RealizedVolatilityStatus.Available, rising.SessionRealizedVolatilityStatus);
        Assert.Equal(10.536051566m, falling.SessionRealizedVolatilityPercent);
        Assert.Equal(RealizedVolatilityStatus.Available, falling.SessionRealizedVolatilityStatus);
        Assert.Equal(0m, unchanged.SessionRealizedVolatilityPercent);
        Assert.Equal(RealizedVolatilityStatus.Available, unchanged.SessionRealizedVolatilityStatus);
        Assert.Equal(rising.SessionRealizedVolatilityPercent, sizeAndQuoteIndependent.SessionRealizedVolatilityPercent);
    }

    [Fact]
    public void Session_realized_volatility_distinguishes_empty_insufficient_and_invalid_prices()
    {
        var configuration = new ScannerConfiguration();
        var empty = CalculateAdxFeatures(Array.Empty<MarketEvent>(), configuration);
        var oneTrade = CalculateAdxFeatures([100m], configuration);
        var zeroPrice = CalculateAdxFeatures([100m, 0m, 110m], configuration);
        var negativePrice = CalculateAdxFeatures([100m, -1m, 110m], configuration);

        Assert.Null(empty.SessionRealizedVolatilityPercent);
        Assert.Equal(RealizedVolatilityStatus.Insufficient, empty.SessionRealizedVolatilityStatus);
        Assert.Null(oneTrade.SessionRealizedVolatilityPercent);
        Assert.Equal(RealizedVolatilityStatus.Insufficient, oneTrade.SessionRealizedVolatilityStatus);
        Assert.Null(zeroPrice.SessionRealizedVolatilityPercent);
        Assert.Equal(RealizedVolatilityStatus.InvalidPriceData, zeroPrice.SessionRealizedVolatilityStatus);
        Assert.Null(negativePrice.SessionRealizedVolatilityPercent);
        Assert.Equal(RealizedVolatilityStatus.InvalidPriceData, negativePrice.SessionRealizedVolatilityStatus);
    }

    [Fact]
    public void Session_realized_volatility_uses_canonical_equal_time_order_and_ignores_duplicates_future_and_receive_time()
    {
        var configuration = new ScannerConfiguration();
        var trades = CreateAdxTrades([100m, 110m, 100m], equalTimestamps: true);
        var baseline = CalculateAdxFeatures(trades, configuration);
        var reordered = CalculateAdxFeatures(
            trades.Reverse().Select(trade => trade with { ReceivedTimeUtc = AsOfUtc.AddYears(1) })
                .Cast<MarketEvent>().ToArray(),
            configuration);
        var state = CreateAdxState(trades);
        Assert.False(state.Add(trades[1] with { Price = 500m, ReceivedTimeUtc = AsOfUtc.AddYears(2) }));
        state.Add(new TradeEvent(
            Symbol,
            SessionId,
            AsOfUtc.AddTicks(1),
            AsOfUtc.AddDays(-1),
            "Fixture",
            "future-volatility",
            0m,
            10));
        var withDuplicateAndFuture = _calculator.Calculate(state, AsOfUtc, configuration);

        Assert.Equal(13.478894891m, baseline.SessionRealizedVolatilityPercent);
        Assert.Equal(baseline, reordered);
        Assert.Equal(baseline, withDuplicateAndFuture);
    }

    [Fact]
    public void Session_realized_volatility_reports_retained_partial_state_after_bounded_eviction()
    {
        var trades = CreateAdxTrades([100m, 110m, 121m]);
        var completeState = CreateAdxState(trades, maximumEvents: 3);
        var partialState = CreateAdxState(trades, maximumEvents: 2);

        var complete = _calculator.Calculate(completeState, AsOfUtc, new ScannerConfiguration());
        var partial = _calculator.Calculate(partialState, AsOfUtc, new ScannerConfiguration());

        Assert.Equal(13.478894891m, complete.SessionRealizedVolatilityPercent);
        Assert.Equal(RealizedVolatilityStatus.Available, complete.SessionRealizedVolatilityStatus);
        Assert.True(complete.HasCompleteSessionData);
        Assert.Equal(9.531017980m, partial.SessionRealizedVolatilityPercent);
        Assert.Equal(RealizedVolatilityStatus.Available, partial.SessionRealizedVolatilityStatus);
        Assert.False(partial.HasCompleteSessionData);
    }

    [Fact]
    public void Session_realized_volatility_isolated_by_symbol_and_explicit_session()
    {
        var firstSession = CalculateAdxFeatures([100m, 110m], new ScannerConfiguration(), Symbol, "session-one");
        var secondSession = CalculateAdxFeatures([100m, 90m], new ScannerConfiguration(), Symbol, "session-two");
        var otherSymbol = CalculateAdxFeatures([100m, 100m], new ScannerConfiguration(), "SILVER", "session-one");

        Assert.Equal(9.531017980m, firstSession.SessionRealizedVolatilityPercent);
        Assert.Equal(10.536051566m, secondSession.SessionRealizedVolatilityPercent);
        Assert.Equal(0m, otherSymbol.SessionRealizedVolatilityPercent);
        Assert.NotEqual(firstSession.Id, secondSession.Id);
        Assert.NotEqual(firstSession.Id, otherSymbol.Id);
    }

    [Fact]
    public void Short_horizon_dollar_volume_uses_the_inclusive_event_time_window_and_canonical_trades()
    {
        var configuration = new ScannerConfiguration();
        var boundary = new TradeEvent(
            Symbol, SessionId, AsOfUtc.AddSeconds(-configuration.MomentumWindowSeconds),
            AsOfUtc.AddYears(1), "B", "dollar-boundary", 4m, 5);
        var older = new TradeEvent(
            Symbol, SessionId, AsOfUtc.AddSeconds(-configuration.MomentumWindowSeconds).AddTicks(-1),
            AsOfUtc, "Fixture", "dollar-older", 999m, 999);
        var firstAtAsOf = new TradeEvent(
            Symbol, SessionId, AsOfUtc, AsOfUtc.AddYears(1), "A", "dollar-a", 1.10m, 7);
        var secondAtAsOf = new TradeEvent(
            Symbol, SessionId, AsOfUtc, AsOfUtc.AddYears(-1), "Z", "dollar-z", 2.25m, 3);
        var quote = new QuoteEvent(
            Symbol, SessionId, AsOfUtc, AsOfUtc.AddYears(1), "Fixture", "dollar-quote",
            1_000m, 1_001m, 1_000_000, 1_000_000);
        var future = new TradeEvent(
            Symbol, SessionId, AsOfUtc.AddTicks(1), AsOfUtc.AddYears(-1),
            "Fixture", "dollar-future", 999m, 999);
        var events = new MarketEvent[] { boundary, older, secondAtAsOf, quote, firstAtAsOf, future };
        var state = CreateAdxState(events);
        Assert.False(state.Add(firstAtAsOf with { Price = 500m, Size = 500 }));
        var baseline = _calculator.Calculate(state, AsOfUtc, configuration);
        var reordered = _calculator.Calculate(
            CreateAdxState(events.Reverse().Select(marketEvent => marketEvent switch
            {
                TradeEvent trade => (MarketEvent)(trade with { ReceivedTimeUtc = AsOfUtc.AddDays(2) }),
                QuoteEvent receivedQuote => receivedQuote with { ReceivedTimeUtc = AsOfUtc.AddDays(2) },
                _ => throw new InvalidOperationException()
            }).ToArray()),
            AsOfUtc,
            configuration);

        Assert.Equal(34.45m, baseline.ShortHorizonDollarVolumeUsd);
        Assert.Equal(ShortHorizonDollarVolumeStatus.Available, baseline.ShortHorizonDollarVolumeStatus);
        Assert.Equal(baseline.ShortHorizonDollarVolumeUsd, reordered.ShortHorizonDollarVolumeUsd);
        Assert.Equal(baseline.Id, reordered.Id);
    }

    [Fact]
    public void Short_horizon_dollar_volume_reports_empty_single_invalid_and_overflow_inputs()
    {
        var configuration = new ScannerConfiguration();
        var empty = CalculateDollarVolumeFeatures(
        [
            new TradeEvent(Symbol, SessionId, AsOfUtc.AddSeconds(-61), AsOfUtc, "Fixture", "outside", 10m, 1),
            new TradeEvent(Symbol, SessionId, AsOfUtc.AddTicks(1), AsOfUtc, "Fixture", "future", 10m, 1)
        ], configuration);
        var oneTrade = CalculateDollarVolumeFeatures(
            [new TradeEvent(Symbol, SessionId, AsOfUtc, AsOfUtc, "Fixture", "single", 12.5m, 2)],
            configuration);
        var invalidCases = new[]
        {
            new TradeEvent(Symbol, SessionId, AsOfUtc, AsOfUtc, "Fixture", "zero-price", 0m, 2),
            new TradeEvent(Symbol, SessionId, AsOfUtc, AsOfUtc, "Fixture", "negative-price", -1m, 2),
            new TradeEvent(Symbol, SessionId, AsOfUtc, AsOfUtc, "Fixture", "zero-size", 10m, 0),
            new TradeEvent(Symbol, SessionId, AsOfUtc, AsOfUtc, "Fixture", "negative-size", 10m, -1)
        };

        Assert.Null(empty.ShortHorizonDollarVolumeUsd);
        Assert.Equal(ShortHorizonDollarVolumeStatus.Insufficient, empty.ShortHorizonDollarVolumeStatus);
        Assert.Equal(25m, oneTrade.ShortHorizonDollarVolumeUsd);
        Assert.Equal(ShortHorizonDollarVolumeStatus.Available, oneTrade.ShortHorizonDollarVolumeStatus);
        foreach (var invalidTrade in invalidCases)
        {
            var result = CalculateDollarVolumeFeatures(
                [
                    new TradeEvent(Symbol, SessionId, AsOfUtc.AddSeconds(-1), AsOfUtc,
                        "Fixture", "valid-trade", 10m, 1),
                    invalidTrade
                ], configuration);
            Assert.Null(result.ShortHorizonDollarVolumeUsd);
            Assert.Equal(ShortHorizonDollarVolumeStatus.InvalidTradeData, result.ShortHorizonDollarVolumeStatus);
        }

        var olderInvalid = CalculateDollarVolumeFeatures(
        [
            new TradeEvent(Symbol, SessionId, AsOfUtc.AddSeconds(-61), AsOfUtc, "Fixture", "old-invalid", 0m, 0),
            new TradeEvent(Symbol, SessionId, AsOfUtc, AsOfUtc, "Fixture", "current-valid", 10m, 1)
        ], configuration);
        Assert.Equal(10m, olderInvalid.ShortHorizonDollarVolumeUsd);
        Assert.Equal(ShortHorizonDollarVolumeStatus.Available, olderInvalid.ShortHorizonDollarVolumeStatus);

        Assert.Throws<OverflowException>(() => CalculateDollarVolumeFeatures(
            [new TradeEvent(Symbol, SessionId, AsOfUtc, AsOfUtc, "Fixture", "overflow", decimal.MaxValue, 2)],
            configuration));
    }

    [Fact]
    public void Short_horizon_dollar_volume_isolated_by_symbol_and_explicit_session()
    {
        var firstSession = CalculateDollarVolumeFeatures(
            [new TradeEvent(Symbol, "dollar-session-one", AsOfUtc, AsOfUtc, "Fixture", "one", 10m, 2)],
            new ScannerConfiguration(),
            Symbol,
            "dollar-session-one");
        var secondSession = CalculateDollarVolumeFeatures(
            [new TradeEvent(Symbol, "dollar-session-two", AsOfUtc, AsOfUtc, "Fixture", "two", 100m, 3)],
            new ScannerConfiguration(),
            Symbol,
            "dollar-session-two");
        var otherSymbol = CalculateDollarVolumeFeatures(
            [new TradeEvent("SILVER", SessionId, AsOfUtc, AsOfUtc, "Fixture", "silver", 7m, 5)],
            new ScannerConfiguration(),
            "SILVER",
            SessionId);

        Assert.Equal(20m, firstSession.ShortHorizonDollarVolumeUsd);
        Assert.Equal(300m, secondSession.ShortHorizonDollarVolumeUsd);
        Assert.Equal(35m, otherSymbol.ShortHorizonDollarVolumeUsd);
        Assert.NotEqual(firstSession.Id, secondSession.Id);
        Assert.NotEqual(firstSession.Id, otherSymbol.Id);
    }

    [Fact]
    public void Short_horizon_dollar_volume_reports_only_retained_values_after_bounded_eviction()
    {
        var state = new SymbolEventState(Symbol, SessionId, 2);
        state.Add(new TradeEvent(Symbol, SessionId, AsOfUtc.AddSeconds(-30), AsOfUtc, "Fixture", "retained-1", 10m, 1));
        state.Add(new TradeEvent(Symbol, SessionId, AsOfUtc.AddSeconds(-10), AsOfUtc, "Fixture", "retained-2", 20m, 1));
        var complete = _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration());
        state.Add(new TradeEvent(Symbol, SessionId, AsOfUtc.AddTicks(1), AsOfUtc,
            "Fixture", "future-evicts-dollar-volume", 30m, 1));
        var partial = _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration());

        Assert.True(complete.HasCompleteSessionData);
        Assert.Equal(30m, complete.ShortHorizonDollarVolumeUsd);
        Assert.False(partial.HasCompleteSessionData);
        Assert.Equal(20m, partial.ShortHorizonDollarVolumeUsd);
        Assert.Equal(ShortHorizonDollarVolumeStatus.Available, partial.ShortHorizonDollarVolumeStatus);
    }

    [Fact]
    public void Short_horizon_dollar_volume_json_and_identity_include_value_and_status()
    {
        var configuration = new ScannerConfiguration();
        var populated = CalculateDollarVolumeFeatures(
            [new TradeEvent(Symbol, SessionId, AsOfUtc, AsOfUtc, "Fixture", "json-dollar", 12.5m, 2)],
            configuration);
        var empty = CalculateDollarVolumeFeatures(Array.Empty<MarketEvent>(), configuration);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(populated, options);
        using var document = JsonDocument.Parse(json);

        Assert.Equal(25m, document.RootElement.GetProperty("shortHorizonDollarVolumeUsd").GetDecimal());
        Assert.Equal((int)ShortHorizonDollarVolumeStatus.Available,
            document.RootElement.GetProperty("shortHorizonDollarVolumeStatus").GetInt32());
        Assert.Equal(populated, JsonSerializer.Deserialize<FeatureSnapshot>(json, options));
        using var emptyJson = JsonDocument.Parse(JsonSerializer.Serialize(empty, options));
        Assert.Equal(JsonValueKind.Null,
            emptyJson.RootElement.GetProperty("shortHorizonDollarVolumeUsd").ValueKind);
        Assert.Equal((int)ShortHorizonDollarVolumeStatus.Insufficient,
            emptyJson.RootElement.GetProperty("shortHorizonDollarVolumeStatus").GetInt32());
        Assert.Equal(empty, JsonSerializer.Deserialize<FeatureSnapshot>(emptyJson.RootElement, options));
        var changedDollarVolume = CalculateDollarVolumeFeatures(
            [new TradeEvent(Symbol, SessionId, AsOfUtc, AsOfUtc, "Fixture", "json-dollar", 13m, 2)],
            configuration);
        Assert.NotEqual(populated.Id, changedDollarVolume.Id);
        Assert.NotEqual(populated.Id, empty.Id);
        Assert.Equal(populated.Id,
            CalculateDollarVolumeFeatures(
                [new TradeEvent(Symbol, SessionId, AsOfUtc, AsOfUtc, "Fixture", "json-dollar", 12.5m, 2)],
                configuration).Id);
    }

    [Fact]
    public void Session_realized_volatility_json_round_trips_and_does_not_change_detector_output()
    {
        var configuration = new ScannerConfiguration();
        var populated = CalculateAdxFeatures([100m, 110m], configuration);
        var empty = CalculateAdxFeatures(Array.Empty<MarketEvent>(), configuration);
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(populated, jsonOptions);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(9.531017980m,
            document.RootElement.GetProperty("sessionRealizedVolatilityPercent").GetDecimal());
        Assert.Equal((int)RealizedVolatilityStatus.Available,
            document.RootElement.GetProperty("sessionRealizedVolatilityStatus").GetInt32());
        Assert.Equal(populated, JsonSerializer.Deserialize<FeatureSnapshot>(json, jsonOptions));

        var apiOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        apiOptions.Converters.Add(new JsonStringEnumConverter());
        using var apiDocument = JsonDocument.Parse(JsonSerializer.Serialize(populated, apiOptions));
        Assert.Equal("Available",
            apiDocument.RootElement.GetProperty("sessionRealizedVolatilityStatus").GetString());
        using var emptyDocument = JsonDocument.Parse(JsonSerializer.Serialize(empty, jsonOptions));
        Assert.Equal(JsonValueKind.Null,
            emptyDocument.RootElement.GetProperty("sessionRealizedVolatilityPercent").ValueKind);
        Assert.Equal((int)RealizedVolatilityStatus.Insufficient,
            emptyDocument.RootElement.GetProperty("sessionRealizedVolatilityStatus").GetInt32());
        Assert.Equal(empty,
            JsonSerializer.Deserialize<FeatureSnapshot>(emptyDocument.RootElement, jsonOptions));

        var opportunity = Replay(GoldenEvents(), configuration);
        var lowVolatilityFeatures = opportunity.Features with
        {
            SessionRealizedVolatilityPercent = 0.5m,
            SessionRealizedVolatilityStatus = RealizedVolatilityStatus.Available
        };
        var baselineOpportunity = _detector.Evaluate(lowVolatilityFeatures, configuration);
        var serializedOpportunity = JsonSerializer.Serialize(baselineOpportunity);
        foreach (var (value, status) in new (decimal?, RealizedVolatilityStatus)[]
        {
            (0m, RealizedVolatilityStatus.Available),
            (0.5m, RealizedVolatilityStatus.Available)
        })
        {
            var changed = lowVolatilityFeatures with
            {
                SessionRealizedVolatilityPercent = value,
                SessionRealizedVolatilityStatus = status
            };
            Assert.Equal(serializedOpportunity,
                JsonSerializer.Serialize(_detector.Evaluate(changed, configuration)));
        }
    }

    [Theory]
    [InlineData(2, 3, 4)]
    [InlineData(14, 27, 28)]
    public void Adx_first_value_requires_two_periods_of_price_observations(
        int period,
        int insufficientCount,
        int warmCount)
    {
        var configuration = new ScannerConfiguration { TrendStrengthPeriod = period };
        var insufficient = CalculateAdxFeatures(
            Enumerable.Repeat(100m, insufficientCount).ToArray(), configuration);
        var warmed = CalculateAdxFeatures(
            Enumerable.Repeat(100m, warmCount).ToArray(), configuration);

        Assert.Null(insufficient.SessionAdx);
        Assert.Equal(AdxStatus.Insufficient, insufficient.SessionAdxStatus);
        Assert.Equal(0m, warmed.SessionAdx);
        Assert.Equal(AdxStatus.Available, warmed.SessionAdxStatus);
    }

    [Fact]
    public void Adx_is_session_wide_inclusive_at_as_of_and_excludes_future_trades()
    {
        var configuration = new ScannerConfiguration { TrendStrengthPeriod = 2 };
        var trades = CreateAdxTrades(
            [10m, 11m, 12m, 13m],
            firstEventTimeUtc: AsOfUtc.AddDays(-1));
        var state = CreateAdxState(trades);
        state.Add(new TradeEvent(
            Symbol, SessionId, AsOfUtc.AddTicks(1), AsOfUtc.AddDays(-1),
            "Fixture", "after-as-of", 500m, 100));

        var features = _calculator.Calculate(state, AsOfUtc, configuration);
        var withoutFuture = CalculateAdxFeatures(trades, configuration);

        Assert.Equal(100m, features.SessionAdx);
        Assert.Equal(AdxStatus.Available, features.SessionAdxStatus);
        Assert.Equal(withoutFuture, features);
    }

    [Fact]
    public void Adx_uses_canonical_equal_time_order_and_ignores_receive_time_and_first_duplicate_conflicts()
    {
        var configuration = new ScannerConfiguration { TrendStrengthPeriod = 2 };
        var trades = CreateAdxTrades([10m, 12m, 10m, 12m], equalTimestamps: true);
        var baseline = CalculateAdxFeatures(trades, configuration);
        var reordered = CalculateAdxFeatures(
            trades.Reverse().Select(trade => trade with { ReceivedTimeUtc = AsOfUtc.AddDays(1) }).ToArray(),
            configuration);
        var duplicateState = CreateAdxState(trades);
        Assert.False(duplicateState.Add(trades[1] with { Price = 500m, ReceivedTimeUtc = AsOfUtc.AddDays(2) }));
        var withConflictingDuplicate = _calculator.Calculate(duplicateState, AsOfUtc, configuration);

        Assert.Equal(25m, baseline.SessionAdx);
        Assert.Equal(baseline, reordered);
        Assert.Equal(baseline, withConflictingDuplicate);
    }

    [Fact]
    public void Adx_ignores_quotes_and_trade_sizes_but_rejects_any_non_positive_eligible_price()
    {
        var configuration = new ScannerConfiguration { TrendStrengthPeriod = 2 };
        var valid = CreateAdxTrades([10m, 11m, 12m, 13m])
            .Select(trade => trade with { Size = 0 })
            .Cast<MarketEvent>()
            .Append(new QuoteEvent(
                Symbol, SessionId, AsOfUtc, AsOfUtc, "Fixture", "invalid-quote",
                -100m, 0m, -1, -1))
            .ToArray();
        var sizeAndQuoteFeatures = CalculateAdxFeatures(valid, configuration);
        var withInvalidPrices = CalculateAdxFeatures(
            [.. CreateAdxTrades([10m, 11m, 12m, 13m]),
             CreateAdxTrades([0m], firstEventTimeUtc: AsOfUtc)[0] with { SequenceId = "invalid-zero-price" }],
            configuration);
        var withNegativePrice = CalculateAdxFeatures(
            [.. CreateAdxTrades([10m, 11m, 12m, 13m]),
             CreateAdxTrades([-1m], firstEventTimeUtc: AsOfUtc)[0] with { SequenceId = "invalid-negative-price" }],
            configuration);
        var empty = CalculateAdxFeatures(Array.Empty<MarketEvent>(), configuration);

        Assert.Equal(100m, sizeAndQuoteFeatures.SessionAdx);
        Assert.Equal(AdxStatus.Available, sizeAndQuoteFeatures.SessionAdxStatus);
        Assert.Null(withInvalidPrices.SessionAdx);
        Assert.Equal(AdxStatus.InvalidPriceData, withInvalidPrices.SessionAdxStatus);
        Assert.Null(withNegativePrice.SessionAdx);
        Assert.Equal(AdxStatus.InvalidPriceData, withNegativePrice.SessionAdxStatus);
        Assert.Null(empty.SessionAdx);
        Assert.Equal(AdxStatus.Insufficient, empty.SessionAdxStatus);
    }

    [Fact]
    public void Adx_isolated_by_symbol_and_explicit_session()
    {
        var configuration = new ScannerConfiguration { TrendStrengthPeriod = 2 };
        var rising = CalculateAdxFeatures([10m, 11m, 12m, 13m], configuration, Symbol, "rising");
        var falling = CalculateAdxFeatures([13m, 12m, 11m, 10m], configuration, Symbol, "falling");
        var otherSymbol = CalculateAdxFeatures([13m, 12m, 11m, 10m], configuration, "SILVER", "rising");

        Assert.Equal(100m, rising.SessionAdx);
        Assert.Equal(100m, falling.SessionAdx);
        Assert.Equal(100m, otherSymbol.SessionAdx);
        Assert.NotEqual(rising.Id, falling.Id);
        Assert.NotEqual(rising.Id, otherSymbol.Id);
    }

    [Fact]
    public void Adx_future_arrival_can_evict_eligible_history_and_make_the_historical_value_partial()
    {
        var configuration = new ScannerConfiguration { TrendStrengthPeriod = 2 };
        var state = CreateAdxState(
            CreateAdxTrades([10m, 11m, 12m, 13m]),
            maximumEvents: 4);
        var complete = _calculator.Calculate(state, AsOfUtc, configuration);
        state.Add(new TradeEvent(
            Symbol, SessionId, AsOfUtc.AddTicks(1), AsOfUtc,
            "Fixture", "future-evicts-eligible", 14m, 1));
        var partial = _calculator.Calculate(state, AsOfUtc, configuration);

        Assert.Equal(100m, complete.SessionAdx);
        Assert.True(complete.HasCompleteSessionData);
        Assert.Null(partial.SessionAdx);
        Assert.Equal(AdxStatus.Insufficient, partial.SessionAdxStatus);
        Assert.False(partial.HasCompleteSessionData);
        Assert.NotEqual(complete.Id, partial.Id);
    }

    [Fact]
    public void Adx_json_round_trips_null_and_values_and_is_detector_neutral()
    {
        var configuration = new ScannerConfiguration();
        var result = Replay(GoldenEvents(), configuration);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var nullJson = JsonSerializer.Serialize(result.Features, options);
        Assert.Equal(result.Features, JsonSerializer.Deserialize<FeatureSnapshot>(nullJson, options));
        using (var document = JsonDocument.Parse(nullJson))
        {
            Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("sessionAdx").ValueKind);
            Assert.Equal((int)AdxStatus.Insufficient,
                document.RootElement.GetProperty("sessionAdxStatus").GetInt32());
        }

        var populated = result.Features with
        {
            SessionAdx = 50m,
            SessionAdxStatus = AdxStatus.Available
        };
        var populatedJson = JsonSerializer.Serialize(populated, options);
        Assert.Equal(populated, JsonSerializer.Deserialize<FeatureSnapshot>(populatedJson, options));
        using (var document = JsonDocument.Parse(populatedJson))
        {
            Assert.Equal(50m, document.RootElement.GetProperty("sessionAdx").GetDecimal());
            Assert.Equal((int)AdxStatus.Available,
                document.RootElement.GetProperty("sessionAdxStatus").GetInt32());
        }

        var apiOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        apiOptions.Converters.Add(new JsonStringEnumConverter());
        using (var document = JsonDocument.Parse(JsonSerializer.Serialize(populated, apiOptions)))
        {
            Assert.Equal("Available", document.RootElement.GetProperty("sessionAdxStatus").GetString());
        }

        var baselineJson = JsonSerializer.Serialize(result.Opportunity, options);
        foreach (var (value, status) in new (decimal?, AdxStatus)[]
        {
            (null, AdxStatus.Insufficient),
            (null, AdxStatus.InvalidPriceData),
            (25m, AdxStatus.Available),
            (100m, AdxStatus.Available)
        })
        {
            var opportunity = _detector.Evaluate(
                result.Features with { SessionAdx = value, SessionAdxStatus = status },
                configuration);
            Assert.Equal(baselineJson, JsonSerializer.Serialize(opportunity, options));
        }
    }

    [Fact]
    public void Adx_output_and_configuration_period_participate_in_deterministic_feature_identity()
    {
        var configuration = new ScannerConfiguration
        {
            TrendStrengthPeriod = 2,
            EmaFastPeriod = 100,
            EmaMediumPeriod = 101
        };
        var first = CalculateAdxFeatures(
            CreateAdxTrades([10m, 12m, 10m, 12m, 10m], size: 0), configuration);
        var changed = CalculateAdxFeatures(
            CreateAdxTrades([10m, 12m, 10m, 11m, 10m], size: 0), configuration);
        var changedPeriod = CalculateAdxFeatures(
            CreateAdxTrades([10m, 12m, 10m, 12m, 10m], size: 0),
            configuration with { TrendStrengthPeriod = 3 });

        Assert.Equal(25m, first.SessionAdx);
        Assert.Equal(18.333333333333333333333333335m, changed.SessionAdx);
        Assert.NotEqual(first.Id, changed.Id);
        Assert.Equal(
            first with
            {
                Id = Guid.Empty,
                SessionAdx = null,
                SessionAdxStatus = AdxStatus.Insufficient,
                SessionRealizedVolatilityPercent = null,
                SessionRealizedVolatilityStatus = RealizedVolatilityStatus.Insufficient
            },
            changed with
            {
                Id = Guid.Empty,
                SessionAdx = null,
                SessionAdxStatus = AdxStatus.Insufficient,
                SessionRealizedVolatilityPercent = null,
                SessionRealizedVolatilityStatus = RealizedVolatilityStatus.Insufficient
            });
        Assert.NotEqual(first.Id, changedPeriod.Id);
        Assert.Equal(AdxStatus.Insufficient, changedPeriod.SessionAdxStatus);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Adx_period_must_be_positive(int period)
    {
        var configuration = new ScannerConfiguration { TrendStrengthPeriod = period };

        Assert.Throws<InvalidOperationException>(configuration.Validate);
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

    [Fact]
    public void Session_volume_includes_trades_outside_both_roc_windows_without_changing_rolling_volume()
    {
        var input = GoldenEvents();
        var baseline = Replay(input, new ScannerConfiguration());
        var extended = Replay(
            [new TradeEvent(Symbol, SessionId, AsOfUtc.AddHours(-2), AsOfUtc.AddDays(1),
                "RecordedFixture", "early", 100m, 700), .. input],
            new ScannerConfiguration());

        Assert.Equal(800, extended.Features.SessionCumulativeVolume);
        Assert.True(extended.Features.HasCompleteSessionData);
        Assert.Equal(100, extended.Features.ShortHorizonVolume);
        Assert.Equal(baseline.Features.ShortHorizonReturnPercent, extended.Features.ShortHorizonReturnPercent);
        Assert.Equal(baseline.Features.SecondaryHorizonReturnPercent, extended.Features.SecondaryHorizonReturnPercent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Session_volume_does_not_require_the_detector_minimum_trade_count(int count)
    {
        var state = new SymbolEventState(Symbol, SessionId, 20);
        foreach (var trade in GoldenEvents().Take(count))
        {
            state.Add(trade);
        }

        var features = _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration());

        Assert.Equal(count == 0 ? (long?)null : count == 1 ? 10L : 30L, features.SessionCumulativeVolume);
        Assert.True(features.HasCompleteSessionData);
        Assert.False(features.HasRequiredData);
        Assert.Equal(DataQualityStatus.Insufficient, features.DataQuality);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Session_volume_is_unavailable_for_any_non_positive_size_even_outside_roc_windows(long size)
    {
        var result = Replay(
            [new TradeEvent(Symbol, SessionId, AsOfUtc.AddHours(-1), AsOfUtc,
                "RecordedFixture", "invalid-size", 100m, size), .. GoldenEvents()],
            new ScannerConfiguration());

        Assert.Null(result.Features.SessionCumulativeVolume);
        Assert.Equal(100, result.Features.ShortHorizonVolume);
        Assert.False(result.Features.IsPriceDataValid);
        Assert.True(result.Features.HasRequiredData);
        Assert.Equal(DataQualityStatus.Degraded, result.Features.DataQuality);
        Assert.Contains("InvalidQuoteOrPrice", result.Opportunity.ActiveVetoes);
        Assert.Contains(result.Opportunity.ContradictoryEvidence, item => item.Code == "InvalidTradeData");
    }

    [Fact]
    public void Session_volume_uses_sizes_independently_of_price_validity()
    {
        var state = new SymbolEventState(Symbol, SessionId, 20);
        state.Add(GoldenEvents()[0] with { Price = 0 });

        var features = _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration());

        Assert.Equal(10, features.SessionCumulativeVolume);
        Assert.False(features.IsPriceDataValid);
    }

    [Fact]
    public void Session_volume_accepts_long_max_value_but_throws_on_checked_sum_overflow()
    {
        var state = new SymbolEventState(Symbol, SessionId, 20);
        state.Add(new TradeEvent(Symbol, SessionId, AsOfUtc.AddHours(-1), AsOfUtc,
            "Fixture", "large", 1m, long.MaxValue));
        Assert.Equal(long.MaxValue,
            _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration()).SessionCumulativeVolume);

        state.Add(new TradeEvent(Symbol, SessionId, AsOfUtc, AsOfUtc, "Fixture", "overflow", 1m, 1));

        Assert.Throws<OverflowException>(() =>
            _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration()));
    }

    [Fact]
    public void Quotes_never_supply_or_add_session_volume()
    {
        var state = new SymbolEventState(Symbol, SessionId, 20);
        state.Add(new QuoteEvent(Symbol, SessionId, AsOfUtc, AsOfUtc, "Fixture", "quote",
            100m, 101m, long.MaxValue, long.MaxValue));
        Assert.Null(_calculator.Calculate(state, AsOfUtc, new ScannerConfiguration()).SessionCumulativeVolume);

        state.Add(GoldenEvents()[0]);

        Assert.Equal(10, _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration()).SessionCumulativeVolume);
    }

    [Fact]
    public void Session_volume_is_isolated_by_symbol_and_explicit_session()
    {
        var state = CreateSession(SessionId, 100m);
        var otherSession = new SymbolEventState(Symbol, "other-session", 20);
        otherSession.Add(GoldenEvents()[0] with { SessionId = "other-session", Size = 200 });
        var otherSymbol = new SymbolEventState("SILVER", SessionId, 20);
        otherSymbol.Add(GoldenEvents()[0] with { Symbol = "SILVER", Size = 300 });

        Assert.Throws<ArgumentException>(() => state.Add(otherSession.GetEventsAsOf(AsOfUtc)[0]));
        Assert.Throws<ArgumentException>(() => state.Add(otherSymbol.GetEventsAsOf(AsOfUtc)[0]));
        Assert.Equal(100, _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration()).SessionCumulativeVolume);
        Assert.Equal(200, _calculator.Calculate(otherSession, AsOfUtc, new ScannerConfiguration()).SessionCumulativeVolume);
        Assert.Equal(300, _calculator.Calculate(otherSymbol, AsOfUtc, new ScannerConfiguration()).SessionCumulativeVolume);
    }

    [Fact]
    public void Session_volume_replay_handles_equal_timestamps_reordering_duplicates_and_receive_times()
    {
        var input = GoldenEvents().Select(trade => trade with { EventTimeUtc = AsOfUtc }).ToArray();
        var baseline = Replay(input, new ScannerConfiguration());
        var reordered = Replay(input.Reverse().ToArray(), new ScannerConfiguration());
        var duplicate = Replay([.. input, input[0] with { Size = 999 }], new ScannerConfiguration());
        var receivedLater = Replay(input.Select(trade =>
            trade with { ReceivedTimeUtc = AsOfUtc.AddYears(1) }).ToArray(), new ScannerConfiguration());

        Assert.Equal(100, baseline.Features.SessionCumulativeVolume);
        Assert.Equal(baseline.Features, reordered.Features);
        Assert.Equal(baseline.Features, duplicate.Features);
        Assert.Equal(baseline.Features, receivedLater.Features);
    }

    [Fact]
    public void Session_volume_cutoff_is_inclusive_and_excludes_future_invalid_and_overflowing_sizes()
    {
        var state = new SymbolEventState(Symbol, SessionId, 20);
        state.Add(GoldenEvents()[0] with { EventTimeUtc = AsOfUtc });
        var baseline = _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration());
        state.Add(GoldenEvents()[1] with { EventTimeUtc = AsOfUtc.AddTicks(1), Size = -1 });
        state.Add(GoldenEvents()[2] with { EventTimeUtc = AsOfUtc.AddSeconds(1), Size = long.MaxValue });

        Assert.Equal(10, baseline.SessionCumulativeVolume);
        Assert.Equal(baseline, _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration()));
    }

    [Fact]
    public void Session_volume_after_eviction_is_a_retained_partial_sum_not_a_complete_total()
    {
        var input = GoldenEvents();
        var state = new SymbolEventState(Symbol, SessionId, 3);
        var reversed = new SymbolEventState(Symbol, SessionId, 3);
        foreach (var trade in input)
        {
            state.Add(trade);
        }
        foreach (var trade in input.Reverse())
        {
            reversed.Add(trade);
        }

        var features = _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration());

        Assert.Equal(90, features.SessionCumulativeVolume);
        Assert.False(features.HasCompleteSessionData);
        Assert.Equal(features, _calculator.Calculate(reversed, AsOfUtc, new ScannerConfiguration()));
        var beforeRetainedTrades = _calculator.Calculate(state, input[0].EventTimeUtc, new ScannerConfiguration());
        Assert.Null(beforeRetainedTrades.SessionCumulativeVolume);
        Assert.False(beforeRetainedTrades.HasCompleteSessionData);
    }

    [Fact]
    public void Future_events_can_evict_earlier_state_so_bounded_state_is_not_historical_replay()
    {
        var state = new SymbolEventState(Symbol, SessionId, 1);
        state.Add(GoldenEvents()[0]);
        var baseline = _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration());
        state.Add(GoldenEvents()[1] with { EventTimeUtc = AsOfUtc.AddTicks(1) });
        var afterEviction = _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration());

        Assert.Equal(10, baseline.SessionCumulativeVolume);
        Assert.True(baseline.HasCompleteSessionData);
        Assert.Null(afterEviction.SessionCumulativeVolume);
        Assert.False(afterEviction.HasCompleteSessionData);
        Assert.NotEqual(baseline.Id, afterEviction.Id);
    }

    [Fact]
    public void Session_volume_and_extrema_are_serialized_and_included_explicitly_in_deterministic_identity()
    {
        var configuration = new ScannerConfiguration();
        var state = new SymbolEventState(Symbol, SessionId, 20);
        foreach (var trade in GoldenEvents())
        {
            state.Add(trade with { Price = 100m });
        }
        state.Add(new QuoteEvent(Symbol, SessionId, AsOfUtc, AsOfUtc,
            "RecordedFixture", "flat-quote", 100m, 100m, 500, 500));
        var features = _calculator.Calculate(state, AsOfUtc, configuration);
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(features, jsonOptions);
        using var document = JsonDocument.Parse(json);

        Assert.Equal(100, document.RootElement.GetProperty("sessionCumulativeVolume").GetInt64());
        Assert.Equal(100m, document.RootElement.GetProperty("sessionHighPrice").GetDecimal());
        Assert.Equal(100m, document.RootElement.GetProperty("sessionLowPrice").GetDecimal());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("sessionRangePositionPercent").ValueKind);
        Assert.Equal(0m, document.RootElement.GetProperty("sessionRealizedVolatilityPercent").GetDecimal());
        Assert.Equal((int)RealizedVolatilityStatus.Available,
            document.RootElement.GetProperty("sessionRealizedVolatilityStatus").GetInt32());
        Assert.Equal(10_000m, document.RootElement.GetProperty("shortHorizonDollarVolumeUsd").GetDecimal());
        Assert.Equal((int)ShortHorizonDollarVolumeStatus.Available,
            document.RootElement.GetProperty("shortHorizonDollarVolumeStatus").GetInt32());
        Assert.Equal(features, JsonSerializer.Deserialize<FeatureSnapshot>(json, jsonOptions));
        Assert.Equal("features-v16", features.FeatureSetVersion);
        Assert.Equal("scanner-config-v16", configuration.Version);
        Assert.Equal("momentum-continuation-v1", configuration.SetupDefinitionVersion);

        var values = new[]
        {
            Symbol, SessionId, AsOfUtc.UtcDateTime.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            configuration.FeatureSetVersion, "100", "100", "100", "0",
            "100", "0", "Flat", "0", "0", "0", "100", "10000", "Available",
            "100", "100", "100", "", "", "", "Insufficient", "0", "Available", "Neutral", "", "", "", "", "", "0",
            "True", "True", "True", "True", "Good",
            JsonSerializer.Serialize(MarketDataCapabilities.Unknown),
            "null", "null", "Insufficient",
            "null", "null", "Insufficient", "Insufficient", "Insufficient",
            "null", "null", "", "", "", ""
        };
        var identity = string.Join("\u001f", JsonSerializer.Serialize(configuration),
            string.Join("\u001e", state.GetEventsAsOf(AsOfUtc).Select(MarketEventIdentity.Create)),
            string.Join("\u001f", values));
        Assert.Equal(new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, 16)), features.Id);
        for (var index = values.Length - 14; index < values.Length; index++)
        {
            var changedValues = values.ToArray();
            changedValues[index] = "changed";
            var changedIdentity = string.Join("\u001f", JsonSerializer.Serialize(configuration),
                string.Join("\u001e", state.GetEventsAsOf(AsOfUtc).Select(MarketEventIdentity.Create)),
                string.Join("\u001f", changedValues));
            Assert.NotEqual(new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(changedIdentity)).AsSpan(0, 16)), features.Id);
        }
        foreach (var index in new[] { 15, 17, 18, 19, 20, 22, 24, 27, 28, 29, 30, 31 })
        {
            foreach (var value in new[] { "101", "102" })
            {
                var changedValues = values.ToArray();
                changedValues[index] = value;
                var changedIdentity = string.Join("\u001f", JsonSerializer.Serialize(configuration),
                    string.Join("\u001e", state.GetEventsAsOf(AsOfUtc).Select(MarketEventIdentity.Create)),
                    string.Join("\u001f", changedValues));
                Assert.NotEqual(new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(changedIdentity)).AsSpan(0, 16)), features.Id);
            }
        }
        foreach (var value in new[] { "", "10001", "10002" })
        {
            var changedValues = values.ToArray();
            changedValues[15] = value;
            var changedIdentity = string.Join("\u001f", JsonSerializer.Serialize(configuration),
                string.Join("\u001e", state.GetEventsAsOf(AsOfUtc).Select(MarketEventIdentity.Create)),
                string.Join("\u001f", changedValues));
            Assert.NotEqual(new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(changedIdentity)).AsSpan(0, 16)), features.Id);
        }
        foreach (var value in Enum.GetNames<ShortHorizonDollarVolumeStatus>()
                     .Where(value => value != "Available"))
        {
            var changedValues = values.ToArray();
            changedValues[16] = value;
            var changedIdentity = string.Join("\u001f", JsonSerializer.Serialize(configuration),
                string.Join("\u001e", state.GetEventsAsOf(AsOfUtc).Select(MarketEventIdentity.Create)),
                string.Join("\u001f", changedValues));
            Assert.NotEqual(new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(changedIdentity)).AsSpan(0, 16)), features.Id);
        }
        foreach (var value in new[] { "", "1", "2" })
        {
            var changedValues = values.ToArray();
            changedValues[24] = value;
            var changedIdentity = string.Join("\u001f", JsonSerializer.Serialize(configuration),
                string.Join("\u001e", state.GetEventsAsOf(AsOfUtc).Select(MarketEventIdentity.Create)),
                string.Join("\u001f", changedValues));
            Assert.NotEqual(new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(changedIdentity)).AsSpan(0, 16)), features.Id);
        }
        foreach (var value in Enum.GetNames<RealizedVolatilityStatus>().Where(value => value != "Available"))
        {
            var changedValues = values.ToArray();
            changedValues[25] = value;
            var changedIdentity = string.Join("\u001f", JsonSerializer.Serialize(configuration),
                string.Join("\u001e", state.GetEventsAsOf(AsOfUtc).Select(MarketEventIdentity.Create)),
                string.Join("\u001f", changedValues));
            Assert.NotEqual(new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(changedIdentity)).AsSpan(0, 16)), features.Id);
        }
        foreach (var value in Enum.GetNames<TrendDirectionContext>().Where(value => value != "Neutral"))
        {
            var changedValues = values.ToArray();
            changedValues[26] = value;
            var changedIdentity = string.Join("\u001f", JsonSerializer.Serialize(configuration),
                string.Join("\u001e", state.GetEventsAsOf(AsOfUtc).Select(MarketEventIdentity.Create)),
                string.Join("\u001f", changedValues));
            Assert.NotEqual(new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(changedIdentity)).AsSpan(0, 16)), features.Id);
        }

        var unavailable = features with { SessionHighPrice = null, SessionLowPrice = null };
        using var nullJson = JsonDocument.Parse(JsonSerializer.Serialize(unavailable, jsonOptions));
        Assert.Equal(JsonValueKind.Null, nullJson.RootElement.GetProperty("sessionHighPrice").ValueKind);
        Assert.Equal(JsonValueKind.Null, nullJson.RootElement.GetProperty("sessionLowPrice").ValueKind);
        Assert.Equal(JsonValueKind.Null, nullJson.RootElement.GetProperty("sessionRangePositionPercent").ValueKind);
        Assert.Equal(unavailable, JsonSerializer.Deserialize<FeatureSnapshot>(nullJson.RootElement, jsonOptions));
    }

    [Fact]
    public void Session_range_position_serializes_non_null_value_and_round_trips()
    {
        var features = Replay(GoldenEvents(), new ScannerConfiguration()).Features;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(features, options);
        using var document = JsonDocument.Parse(json);

        Assert.Equal(100m, document.RootElement.GetProperty("sessionRangePositionPercent").GetDecimal());
        Assert.Equal(features, JsonSerializer.Deserialize<FeatureSnapshot>(json, options));
    }

    [Fact]
    public void Session_volume_is_descriptive_and_does_not_change_detector_results()
    {
        var configuration = new ScannerConfiguration();
        var baseline = Replay(GoldenEvents(), configuration);
        foreach (var value in new long?[] { null, 0, 1, long.MaxValue })
        {
            var opportunity = _detector.Evaluate(
                baseline.Features with { SessionCumulativeVolume = value }, configuration);

            Assert.Equal(JsonSerializer.Serialize(baseline.Opportunity), JsonSerializer.Serialize(opportunity));
        }
    }

    [Fact]
    public void Session_extrema_cover_all_retained_trades_not_either_roc_window()
    {
        var result = Replay(
            [new TradeEvent(Symbol, SessionId, AsOfUtc.AddHours(-1), AsOfUtc,
                "RecordedFixture", "old-high", 500m, 1),
             new TradeEvent(Symbol, SessionId, AsOfUtc.AddSeconds(-61), AsOfUtc,
                "RecordedFixture", "old-low", 5m, 1000),
             .. GoldenEvents()],
            new ScannerConfiguration());

        Assert.Equal(500m, result.Features.SessionHighPrice);
        Assert.Equal(5m, result.Features.SessionLowPrice);
        Assert.Equal(1101, result.Features.SessionCumulativeVolume);
        Assert.Equal(100, result.Features.ShortHorizonVolume);
        Assert.Equal(3m, result.Features.ShortHorizonReturnPercent);
        Assert.Equal(3m, result.Features.SecondaryHorizonReturnPercent);
        Assert.True(result.Features.HasCompleteSessionData);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Session_extrema_accept_empty_single_and_equal_prices_below_minimum_trades(int count)
    {
        var state = new SymbolEventState(Symbol, SessionId, 20);
        foreach (var trade in GoldenEvents().Take(count))
        {
            state.Add(trade with { Price = 12.5m });
        }

        var features = _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration());

        Assert.Equal(count == 0 ? (decimal?)null : 12.5m, features.SessionHighPrice);
        Assert.Equal(count == 0 ? (decimal?)null : 12.5m, features.SessionLowPrice);
        Assert.Null(features.SessionRangePositionPercent);
        Assert.True(features.HasCompleteSessionData);
        Assert.False(features.HasRequiredData);
    }

    [Theory]
    [InlineData(100, 200, 100, 0)]
    [InlineData(100, 200, 200, 100)]
    [InlineData(100, 200, 125, 25)]
    [InlineData(200, 150, 100, 0)]
    [InlineData(100, 150, 200, 100)]
    [InlineData(100, 150, 125, 50)]
    public void Session_range_position_uses_latest_trade_for_rising_and_falling_paths(
        int firstPrice,
        int secondPrice,
        int latestPrice,
        int expectedPosition)
    {
        var features = _calculator.Calculate(
            CreateSlopeState(Symbol, SessionId, [firstPrice, secondPrice, latestPrice]),
            AsOfUtc,
            new ScannerConfiguration());

        Assert.Equal((decimal)expectedPosition, features.SessionRangePositionPercent);
        Assert.Equal((decimal)latestPrice, features.ReferencePrice);
    }

    [Fact]
    public void Session_range_position_does_not_require_minimum_trade_count()
    {
        var state = new SymbolEventState(Symbol, SessionId, 20);
        var trades = GoldenEvents();
        state.Add(trades[0] with { Price = 100m });
        state.Add(trades[1] with { Price = 200m });

        var features = _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration());

        Assert.Equal(100m, features.SessionRangePositionPercent);
        Assert.False(features.HasRequiredData);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Positive_invalid_size_latest_trade_still_defines_range_reference_and_quality(long size)
    {
        var input = GoldenEvents().Append(new TradeEvent(
            Symbol,
            SessionId,
            AsOfUtc,
            AsOfUtc.AddDays(1),
            "ZFixture",
            "invalid-size-latest",
            125m,
            size)).ToArray();
        var result = Replay(input, new ScannerConfiguration());

        Assert.Equal(125m, result.Features.ReferencePrice);
        Assert.Equal(125m, result.Features.SessionHighPrice);
        Assert.Equal(100m, result.Features.SessionLowPrice);
        Assert.Equal(100m, result.Features.SessionRangePositionPercent);
        Assert.Null(result.Features.SessionCumulativeVolume);
        Assert.False(result.Features.IsPriceDataValid);
        Assert.Equal(DataQualityStatus.Degraded, result.Features.DataQuality);
        Assert.Contains("InvalidQuoteOrPrice", result.Opportunity.ActiveVetoes);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, -1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(2, 0)]
    [InlineData(2, -1)]
    public void Session_extrema_reject_non_positive_first_middle_last_prices_outside_both_windows(
        int invalidIndex, int price)
    {
        var older = GoldenEvents().Take(3).Select((trade, index) => trade with
        {
            EventTimeUtc = AsOfUtc.AddHours(-1).AddSeconds(index),
            Price = index == invalidIndex ? price : 50m,
            SequenceId = $"older-{index}"
        });
        var result = Replay([.. older, .. GoldenEvents()], new ScannerConfiguration());

        Assert.Null(result.Features.SessionHighPrice);
        Assert.Null(result.Features.SessionLowPrice);
        Assert.Null(result.Features.SessionRangePositionPercent);
        Assert.False(result.Features.IsPriceDataValid);
        Assert.Equal(160, result.Features.SessionCumulativeVolume);
        Assert.Equal(3m, result.Features.ShortHorizonReturnPercent);
        Assert.Equal(3m, result.Features.SecondaryHorizonReturnPercent);
        Assert.Equal(DataQualityStatus.Degraded, result.Features.DataQuality);
        Assert.Contains("InvalidQuoteOrPrice", result.Opportunity.ActiveVetoes);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, -1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(2, 0)]
    [InlineData(2, -1)]
    public void Session_extrema_reject_non_positive_first_middle_last_prices_inside_windows(
        int invalidIndex, int price)
    {
        var result = Replay(GoldenEvents().Take(3).Select((trade, index) =>
            trade with { Price = index == invalidIndex ? price : 100m }).ToArray(),
            new ScannerConfiguration());

        Assert.Null(result.Features.SessionHighPrice);
        Assert.Null(result.Features.SessionLowPrice);
        Assert.Null(result.Features.SessionRangePositionPercent);
        Assert.False(result.Features.IsPriceDataValid);
        Assert.Contains("InvalidQuoteOrPrice", result.Opportunity.ActiveVetoes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Session_extrema_include_positive_prices_with_invalid_sizes_without_removing_vetoes(long size)
    {
        var result = Replay(
            [new TradeEvent(Symbol, SessionId, AsOfUtc.AddHours(-1), AsOfUtc,
                "RecordedFixture", "invalid-size-high", 500m, size),
             new TradeEvent(Symbol, SessionId, AsOfUtc.AddSeconds(-61), AsOfUtc,
                "RecordedFixture", "invalid-size-low", 5m, size),
             .. GoldenEvents()],
            new ScannerConfiguration());

        Assert.Equal(500m, result.Features.SessionHighPrice);
        Assert.Equal(5m, result.Features.SessionLowPrice);
        Assert.Equal(
            (result.Features.ReferencePrice!.Value - 5m) / (500m - 5m) * 100m,
            result.Features.SessionRangePositionPercent);
        Assert.Null(result.Features.SessionCumulativeVolume);
        Assert.Equal(100, result.Features.ShortHorizonVolume);
        Assert.False(result.Features.IsPriceDataValid);
        Assert.Equal(DataQualityStatus.Degraded, result.Features.DataQuality);
        Assert.Contains("InvalidQuoteOrPrice", result.Opportunity.ActiveVetoes);
        Assert.Contains(result.Opportunity.ContradictoryEvidence, item => item.Code == "InvalidTradeData");
    }

    [Fact]
    public void Session_extrema_ignore_quotes_and_quote_prices()
    {
        var state = new SymbolEventState(Symbol, SessionId, 20);
        state.Add(new QuoteEvent(Symbol, SessionId, AsOfUtc, AsOfUtc, "Fixture", "quote",
            -1000m, 1000m, 100, 100));
        var empty = _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration());
        Assert.Null(empty.SessionHighPrice);
        Assert.Null(empty.SessionLowPrice);
        Assert.Null(empty.SessionRangePositionPercent);
        foreach (var trade in GoldenEvents())
        {
            state.Add(trade);
        }
        var features = _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration());
        Assert.Equal(103m, features.SessionHighPrice);
        Assert.Equal(100m, features.SessionLowPrice);
        Assert.Equal(103m, features.ReferencePrice);
        Assert.Equal(100m, features.SessionRangePositionPercent);
    }

    [Fact]
    public void Session_extrema_are_isolated_by_symbol_and_explicit_session()
    {
        var state = CreateSession(SessionId, 100m);
        var otherSession = CreateSession("other-session", 200m);
        var otherSymbol = CreateSlopeState("SILVER", SessionId, [50m, 51m, 52m]);
        Assert.Throws<ArgumentException>(() => state.Add(otherSession.GetEventsAsOf(AsOfUtc)[0]));
        Assert.Throws<ArgumentException>(() => state.Add(otherSymbol.GetEventsAsOf(AsOfUtc)[0]));
        var features = _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration());
        var sessionFeatures = _calculator.Calculate(otherSession, AsOfUtc, new ScannerConfiguration());
        var symbolFeatures = _calculator.Calculate(otherSymbol, AsOfUtc, new ScannerConfiguration());
        Assert.Equal(103m, features.SessionHighPrice);
        Assert.Equal(100m, features.SessionLowPrice);
        Assert.Equal(100m, features.SessionRangePositionPercent);
        Assert.Equal(203m, sessionFeatures.SessionHighPrice);
        Assert.Equal(200m, sessionFeatures.SessionLowPrice);
        Assert.Equal(100m, sessionFeatures.SessionRangePositionPercent);
        Assert.Equal(52m, symbolFeatures.SessionHighPrice);
        Assert.Equal(50m, symbolFeatures.SessionLowPrice);
        Assert.Equal(100m, symbolFeatures.SessionRangePositionPercent);
    }

    [Fact]
    public void Session_extrema_replay_preserves_first_duplicates_equal_times_and_stable_receive_independence()
    {
        var input = GoldenEvents().Select(trade => trade with { EventTimeUtc = AsOfUtc }).ToArray();
        var configuration = new ScannerConfiguration();
        var baseline = Replay(input, configuration).Features;
        Assert.Equal(103m, baseline.SessionHighPrice);
        Assert.Equal(100m, baseline.SessionLowPrice);
        Assert.Equal(100m, baseline.SessionRangePositionPercent);
        Assert.Equal(baseline, Replay(input.Reverse().ToArray(), configuration).Features);
        Assert.Equal(baseline, Replay(input.Select(trade =>
            trade with { ReceivedTimeUtc = AsOfUtc.AddYears(1) }).ToArray(), configuration).Features);
        Assert.Equal(baseline, Replay(
            [.. input, input[0], input[0] with { Price = 999m }, input[1] with { Price = -1m }],
            configuration).Features);
        var firstConflict = Replay(
            [input[0] with { Price = 999m }, .. input], configuration).Features;
        Assert.Equal(999m, firstConflict.SessionHighPrice);
        Assert.Equal(101m, firstConflict.SessionLowPrice);
        Assert.Equal(
            (firstConflict.ReferencePrice!.Value - 101m) / (999m - 101m) * 100m,
            firstConflict.SessionRangePositionPercent);
        Assert.NotEqual(baseline.Id, firstConflict.Id);
    }

    [Fact]
    public void Session_extrema_cutoff_is_inclusive_and_future_extreme_or_invalid_prices_do_not_change_identity()
    {
        var state = new SymbolEventState(Symbol, SessionId, 20);
        state.Add(GoldenEvents()[0] with { EventTimeUtc = AsOfUtc });
        var baseline = _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration());
        Assert.Equal(100m, baseline.SessionHighPrice);
        Assert.Equal(100m, baseline.SessionLowPrice);
        Assert.Null(baseline.SessionRangePositionPercent);
        foreach (var price in new[] { 1000m, 1m, 0m, -1m })
        {
            state.Add(new TradeEvent(Symbol, SessionId, AsOfUtc.AddTicks(1), AsOfUtc.AddDays(-1),
                "Fixture", $"future-{price}", price, 1));
            Assert.Equal(baseline, _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration()));
        }
    }

    [Fact]
    public void Session_extrema_eviction_removes_prior_extrema_and_marks_retained_values_partial()
    {
        var input = GoldenEvents();
        input[0] = input[0] with { Price = 500m };
        input[1] = input[1] with { Price = 1m };
        var completeState = new SymbolEventState(Symbol, SessionId, 20);
        var state = new SymbolEventState(Symbol, SessionId, 2);
        var reordered = new SymbolEventState(Symbol, SessionId, 2);
        foreach (var trade in input)
        {
            completeState.Add(trade);
            state.Add(trade);
        }
        foreach (var trade in input.Reverse()) reordered.Add(trade);
        var completeFeatures = _calculator.Calculate(completeState, AsOfUtc, new ScannerConfiguration());
        var features = _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration());
        Assert.Equal(
            (completeFeatures.ReferencePrice!.Value - 1m) / (500m - 1m) * 100m,
            completeFeatures.SessionRangePositionPercent);
        Assert.NotEqual(completeFeatures.SessionRangePositionPercent, features.SessionRangePositionPercent);
        Assert.Equal(103m, features.SessionHighPrice);
        Assert.Equal(102m, features.SessionLowPrice);
        Assert.False(features.HasCompleteSessionData);
        Assert.Equal(features, _calculator.Calculate(reordered, AsOfUtc, new ScannerConfiguration()));
        var empty = _calculator.Calculate(state, input[1].EventTimeUtc, new ScannerConfiguration());
        Assert.Null(empty.SessionHighPrice);
        Assert.Null(empty.SessionLowPrice);
        Assert.False(empty.HasCompleteSessionData);
        state.Add(input[3] with { SequenceId = "invalid-retained", Price = 0m });
        var invalid = _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration());
        Assert.Null(invalid.SessionHighPrice);
        Assert.Null(invalid.SessionLowPrice);
        Assert.Null(invalid.SessionRangePositionPercent);
        Assert.False(invalid.HasCompleteSessionData);
    }

    [Fact]
    public void Session_extrema_completeness_is_sticky_even_when_only_a_quote_was_evicted()
    {
        var state = new SymbolEventState(Symbol, SessionId, 2);
        state.Add(new QuoteEvent(Symbol, SessionId, AsOfUtc.AddHours(-1), AsOfUtc,
            "Fixture", "old-quote", 1m, 1000m, 1, 1));
        state.Add(GoldenEvents()[0]);
        var complete = _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration());
        state.Add(GoldenEvents()[1]);
        var partial = _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration());
        Assert.True(complete.HasCompleteSessionData);
        Assert.Equal(101m, partial.SessionHighPrice);
        Assert.Equal(100m, partial.SessionLowPrice);
        Assert.Equal(100m, partial.SessionRangePositionPercent);
        Assert.False(partial.HasCompleteSessionData);
        Assert.False(_calculator.Calculate(state, AsOfUtc.AddHours(-2), new ScannerConfiguration()).HasCompleteSessionData);
        state.Add(GoldenEvents()[2]);
        Assert.False(_calculator.Calculate(state, AsOfUtc, new ScannerConfiguration()).HasCompleteSessionData);
    }

    [Fact]
    public void Future_arrivals_can_evict_session_extrema_before_a_historical_as_of()
    {
        var state = new SymbolEventState(Symbol, SessionId, 1);
        state.Add(GoldenEvents()[0]);
        var baseline = _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration());
        state.Add(GoldenEvents()[1] with { EventTimeUtc = AsOfUtc.AddTicks(1) });
        var after = _calculator.Calculate(state, AsOfUtc, new ScannerConfiguration());
        Assert.Equal(100m, baseline.SessionHighPrice);
        Assert.Equal(100m, baseline.SessionLowPrice);
        Assert.True(baseline.HasCompleteSessionData);
        Assert.Null(after.SessionHighPrice);
        Assert.Null(after.SessionLowPrice);
        Assert.Null(after.SessionRangePositionPercent);
        Assert.False(after.HasCompleteSessionData);
        Assert.NotEqual(baseline.Id, after.Id);
    }

    [Fact]
    public void Session_extrema_are_descriptive_and_do_not_change_any_detector_output()
    {
        var configuration = new ScannerConfiguration();
        var baseline = Replay(GoldenEvents(), configuration);
        foreach (var high in new decimal?[] { null, 1m, 1000m })
        {
            foreach (var low in new decimal?[] { null, 1m, 1000m })
            {
                var opportunity = _detector.Evaluate(
                    baseline.Features with { SessionHighPrice = high, SessionLowPrice = low }, configuration);
                Assert.Equal(JsonSerializer.Serialize(baseline.Opportunity), JsonSerializer.Serialize(opportunity));
            }
        }
    }

    [Fact]
    public void Session_range_position_is_descriptive_and_does_not_change_any_detector_output()
    {
        var configuration = new ScannerConfiguration();
        var baseline = Replay(GoldenEvents(), configuration);
        foreach (var value in new decimal?[] { null, 0m, 25m, 100m })
        {
            var opportunity = _detector.Evaluate(
                baseline.Features with { SessionRangePositionPercent = value }, configuration);
            Assert.Equal(JsonSerializer.Serialize(baseline.Opportunity), JsonSerializer.Serialize(opportunity));
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

    private FeatureSnapshot CalculateEmaFeatures(
        decimal[] prices,
        ScannerConfiguration configuration,
        string symbol = "GOLD",
        string sessionId = "ema-session") =>
        CalculateEmaFeatures(CreateEmaTrades(prices, symbol, sessionId), configuration);

    private FeatureSnapshot CalculateStructureFeatures(
        IReadOnlyList<TradeEvent> trades,
        DateTimeOffset? asOfUtc = null,
        int maximumEvents = 1000,
        ScannerConfiguration? configuration = null)
    {
        var firstTrade = trades.FirstOrDefault();
        var state = new SymbolEventState(
            firstTrade?.Symbol ?? Symbol,
            firstTrade?.SessionId ?? SessionId,
            maximumEvents);
        foreach (var trade in trades)
        {
            state.Add(trade);
        }

        return _calculator.Calculate(state, asOfUtc ?? AsOfUtc, configuration ?? new ScannerConfiguration());
    }

    private FeatureSnapshot CalculateAdxFeatures(
        decimal[] prices,
        ScannerConfiguration configuration,
        string symbol = Symbol,
        string sessionId = SessionId) =>
        CalculateAdxFeatures(CreateAdxTrades(prices, symbol, sessionId), configuration, symbol, sessionId);

    private FeatureSnapshot CalculateAdxFeatures(
        IReadOnlyList<MarketEvent> events,
        ScannerConfiguration configuration,
        string symbol = Symbol,
        string sessionId = SessionId)
    {
        var state = CreateAdxState(events, symbol, sessionId);
        return _calculator.Calculate(state, AsOfUtc, configuration);
    }

    private FeatureSnapshot CalculateDollarVolumeFeatures(
        IReadOnlyCollection<MarketEvent> events,
        ScannerConfiguration configuration,
        string symbol = Symbol,
        string sessionId = SessionId,
        int maximumEvents = 1000)
    {
        var state = new SymbolEventState(symbol, sessionId, maximumEvents);
        foreach (var marketEvent in events)
        {
            state.Add(marketEvent);
        }

        return _calculator.Calculate(state, AsOfUtc, configuration);
    }

    private static SymbolEventState CreateAdxState(
        IReadOnlyList<MarketEvent> events,
        string symbol = Symbol,
        string sessionId = SessionId,
        int maximumEvents = 1000)
    {
        var state = new SymbolEventState(symbol, sessionId, maximumEvents);
        foreach (var marketEvent in events)
        {
            state.Add(marketEvent);
        }

        return state;
    }

    private static TradeEvent[] CreateAdxTrades(
        IReadOnlyList<decimal> prices,
        string symbol = Symbol,
        string sessionId = SessionId,
        bool equalTimestamps = false,
        DateTimeOffset? firstEventTimeUtc = null,
        long size = 10)
    {
        var firstTime = firstEventTimeUtc ?? AsOfUtc.AddSeconds(-10 * (prices.Count - 1));
        return prices.Select((price, index) => new TradeEvent(
            symbol,
            sessionId,
            equalTimestamps ? AsOfUtc : firstTime.AddSeconds(index * 10),
            AsOfUtc,
            "Fixture",
            $"adx-{index:D3}",
            price,
            size)).ToArray();
    }

    private FeatureSnapshot CalculateEmaFeatures(
        IReadOnlyList<MarketEvent> events,
        ScannerConfiguration configuration)
    {
        var first = events[0];
        var state = new SymbolEventState(first.Symbol, first.SessionId, 100);
        foreach (var marketEvent in events)
        {
            state.Add(marketEvent);
        }

        return _calculator.Calculate(state, AsOfUtc, configuration);
    }

    private static TradeEvent[] CreateStructureTrades(
        IReadOnlyList<decimal> prices,
        string symbol = Symbol,
        string sessionId = SessionId,
        bool equalTimestamps = false,
        DateTimeOffset? firstEventTimeUtc = null,
        TimeSpan? interval = null,
        long size = 10)
    {
        var firstTime = firstEventTimeUtc ?? AsOfUtc.AddSeconds(-120);
        var spacing = interval ?? TimeSpan.FromSeconds(10);
        return prices.Select((price, index) => new TradeEvent(
            symbol,
            sessionId,
            equalTimestamps ? AsOfUtc : firstTime.AddTicks(spacing.Ticks * index),
            AsOfUtc.AddSeconds(index),
            "Fixture",
            $"structure-{index:D3}",
            price,
            size)).ToArray();
    }

    private static TradeEvent[] CreateEmaTrades(
        decimal[] prices,
        string symbol = "GOLD",
        string sessionId = "ema-session")
    {
        var lastIndex = prices.Length - 1;
        return prices.Select((price, index) => new TradeEvent(
            symbol,
            sessionId,
            AsOfUtc.AddSeconds(-10 * (lastIndex - index)),
            AsOfUtc,
            "RecordedFixture",
            $"ema-{symbol}-{sessionId}-{index}",
            price,
            10)).ToArray();
    }

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
