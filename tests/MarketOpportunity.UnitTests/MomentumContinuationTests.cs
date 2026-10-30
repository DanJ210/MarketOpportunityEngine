using MarketOpportunity.Analytics;
using MarketOpportunity.Domain;
using System.Text.Json;

namespace MarketOpportunity.UnitTests;

public sealed class MomentumContinuationTests
{
    private static readonly DateTimeOffset AsOfUtc = new(2026, 10, 23, 14, 30, 42, TimeSpan.Zero);
    private const string SessionId = "test-session";
    private readonly FeatureCalculator _calculator = new();
    private readonly MomentumContinuationDetector _detector = new();
    private readonly ScannerConfiguration _configuration = new();

    [Fact]
    public void Calculates_features_without_using_events_after_as_of_time()
    {
        var state = CreateState();
        state.Add(new TradeEvent("MOE", SessionId, AsOfUtc.AddSeconds(30), AsOfUtc.AddSeconds(31), "Fixture", "future", 999m, 9000));

        var features = _calculator.Calculate(state, AsOfUtc, _configuration);

        Assert.Equal(150.40m, features.ReferencePrice);
        Assert.True(features.SessionVwap < features.ReferencePrice);
        Assert.Equal(TimeSpan.Zero, features.QuoteAge);
    }

    [Fact]
    public void Produces_probable_long_with_explainable_score_and_versions()
    {
        var features = _calculator.Calculate(CreateState(), AsOfUtc, _configuration);

        var opportunity = _detector.Evaluate(features, _configuration);

        Assert.Equal(OpportunityClassification.ProbableLong, opportunity.Classification);
        Assert.Equal(SetupBias.Long, opportunity.Bias);
        Assert.Equal(100, opportunity.QualityScore);
        Assert.Empty(opportunity.ActiveVetoes);
        Assert.Contains(opportunity.SupportingEvidence, item => item.Code == "DirectionalMomentum");
        Assert.Contains(opportunity.SupportingEvidence, item => item.Code == "VwapAlignment");
        Assert.Contains("Long invalidated", opportunity.InvalidationCondition);
        Assert.Equal(_configuration.FeatureSetVersion, opportunity.FeatureSetVersion);
        Assert.Equal(_configuration.SetupDefinitionVersion, opportunity.SetupDefinitionVersion);
        Assert.Equal(_configuration.Version, opportunity.ConfigurationVersion);
        Assert.Equal(AsOfUtc.AddSeconds(_configuration.OpportunityExpirationSeconds), opportunity.ValidUntilUtc);
    }

    [Theory]
    [InlineData(99_999.99, true)]
    [InlineData(100_000, false)]
    [InlineData(100_000.01, false)]
    public void Applies_strict_minimum_dollar_volume_to_complete_snapshots(
        decimal dollarVolumeUsd,
        bool expectedLiquidityVeto)
    {
        var features = _calculator.Calculate(CreateState(), AsOfUtc, _configuration) with
        {
            ShortHorizonDollarVolumeUsd = dollarVolumeUsd,
            ShortHorizonDollarVolumeStatus = ShortHorizonDollarVolumeStatus.Available
        };

        var opportunity = _detector.Evaluate(features, _configuration);

        Assert.Equal(expectedLiquidityVeto, opportunity.ActiveVetoes.Contains("InsufficientDollarVolume"));
        Assert.Equal(
            expectedLiquidityVeto ? OpportunityClassification.AvoidNoTrade : OpportunityClassification.ProbableLong,
            opportunity.Classification);
    }

    [Fact]
    public void Skips_only_the_threshold_for_partial_available_data_and_keeps_incomplete_veto()
    {
        var features = _calculator.Calculate(CreateState(), AsOfUtc, _configuration) with
        {
            ShortHorizonDollarVolumeUsd = 1m,
            ShortHorizonDollarVolumeStatus = ShortHorizonDollarVolumeStatus.Available,
            HasCompleteSessionData = false
        };

        var opportunity = _detector.Evaluate(features, _configuration);

        Assert.Contains("InsufficientRequiredFeatureData", opportunity.ActiveVetoes);
        Assert.DoesNotContain("InsufficientDollarVolume", opportunity.ActiveVetoes);
        Assert.Contains(opportunity.ContradictoryEvidence, item => item.Code == "TruncatedEventState");

        var partialInsufficient = _detector.Evaluate(features with
        {
            ShortHorizonDollarVolumeUsd = null,
            ShortHorizonDollarVolumeStatus = ShortHorizonDollarVolumeStatus.Insufficient
        }, _configuration);
        Assert.Contains("InsufficientRequiredFeatureData", partialInsufficient.ActiveVetoes);
        Assert.Contains("DollarVolumeUnavailable", partialInsufficient.ActiveVetoes);

        var partialInvalid = _detector.Evaluate(features with
        {
            ShortHorizonDollarVolumeUsd = null,
            ShortHorizonDollarVolumeStatus = ShortHorizonDollarVolumeStatus.InvalidTradeData
        }, _configuration);
        Assert.Contains("InsufficientRequiredFeatureData", partialInvalid.ActiveVetoes);
        Assert.Contains("InvalidDollarVolumeData", partialInvalid.ActiveVetoes);
    }

    [Fact]
    public void Fails_closed_for_invalid_and_inconsistent_dollar_volume_states()
    {
        var features = _calculator.Calculate(CreateState(), AsOfUtc, _configuration);
        var cases = new (decimal? Value, ShortHorizonDollarVolumeStatus Status, string Veto)[]
        {
            (null, ShortHorizonDollarVolumeStatus.Insufficient, "DollarVolumeUnavailable"),
            (null, ShortHorizonDollarVolumeStatus.InvalidTradeData, "InvalidDollarVolumeData"),
            (0.5m, ShortHorizonDollarVolumeStatus.Insufficient, "InvalidDollarVolumeData"),
            (0.5m, ShortHorizonDollarVolumeStatus.InvalidTradeData, "InvalidDollarVolumeData"),
            (null, ShortHorizonDollarVolumeStatus.Available, "InvalidDollarVolumeData"),
            (-0.1m, ShortHorizonDollarVolumeStatus.Available, "InvalidDollarVolumeData"),
            (0m, ShortHorizonDollarVolumeStatus.Available, "InsufficientDollarVolume"),
            (0.5m, (ShortHorizonDollarVolumeStatus)int.MaxValue, "InvalidDollarVolumeData")
        };

        foreach (var (value, status, expectedVeto) in cases)
        {
            var opportunity = _detector.Evaluate(features with
            {
                ShortHorizonDollarVolumeUsd = value,
                ShortHorizonDollarVolumeStatus = status
            }, _configuration);

            Assert.Contains(expectedVeto, opportunity.ActiveVetoes);
            Assert.Equal(OpportunityClassification.AvoidNoTrade, opportunity.Classification);
        }
    }

    [Fact]
    public void Evaluates_dollar_volume_veto_even_when_another_veto_is_active()
    {
        var features = _calculator.Calculate(CreateState(quoteAge: TimeSpan.FromSeconds(30)), AsOfUtc, _configuration) with
        {
            ShortHorizonDollarVolumeUsd = 99_999.99m,
            ShortHorizonDollarVolumeStatus = ShortHorizonDollarVolumeStatus.Available
        };

        var opportunity = _detector.Evaluate(features, _configuration);

        Assert.Contains("StaleQuote", opportunity.ActiveVetoes);
        Assert.Contains("InsufficientDollarVolume", opportunity.ActiveVetoes);
        Assert.Equal(OpportunityClassification.AvoidNoTrade, opportunity.Classification);
    }

    [Fact]
    public void Applies_inclusive_realized_volatility_threshold_to_complete_snapshots()
    {
        var features = _calculator.Calculate(CreateState(), AsOfUtc, _configuration);
        foreach (var (volatilityPercent, isVetoed) in new (decimal Value, bool IsVetoed)[]
        {
            (0.999999m, false),
            (1.00m, true),
            (1.000001m, true)
        })
        {
            var opportunity = _detector.Evaluate(features with
            {
                SessionRealizedVolatilityPercent = volatilityPercent,
                SessionRealizedVolatilityStatus = RealizedVolatilityStatus.Available
            }, _configuration);

            Assert.Equal(isVetoed, opportunity.ActiveVetoes.Contains("ExcessiveRealizedVolatility"));
            Assert.Equal(
                isVetoed ? OpportunityClassification.AvoidNoTrade : OpportunityClassification.ProbableLong,
                opportunity.Classification);
        }
    }

    [Fact]
    public void Does_not_apply_the_volatility_threshold_to_partial_retained_session_values()
    {
        var features = _calculator.Calculate(CreateState(), AsOfUtc, _configuration) with
        {
            SessionRealizedVolatilityPercent = 10m,
            SessionRealizedVolatilityStatus = RealizedVolatilityStatus.Available,
            HasCompleteSessionData = false
        };

        var opportunity = _detector.Evaluate(features, _configuration);

        Assert.Contains("InsufficientRequiredFeatureData", opportunity.ActiveVetoes);
        Assert.DoesNotContain("ExcessiveRealizedVolatility", opportunity.ActiveVetoes);
        Assert.Contains(opportunity.ContradictoryEvidence, item => item.Code == "TruncatedEventState");
        Assert.Equal(OpportunityClassification.AvoidNoTrade, opportunity.Classification);

        var insufficientPartial = _detector.Evaluate(features with
        {
            SessionRealizedVolatilityPercent = null,
            SessionRealizedVolatilityStatus = RealizedVolatilityStatus.Insufficient
        }, _configuration);
        Assert.Contains("InsufficientRequiredFeatureData", insufficientPartial.ActiveVetoes);
        Assert.Contains("VolatilityUnavailable", insufficientPartial.ActiveVetoes);
        Assert.DoesNotContain("ExcessiveRealizedVolatility", insufficientPartial.ActiveVetoes);
    }

    [Fact]
    public void Fails_closed_for_unavailable_invalid_and_inconsistent_volatility_states()
    {
        var features = _calculator.Calculate(CreateState(), AsOfUtc, _configuration);
        var cases = new (decimal? Value, RealizedVolatilityStatus Status, string Veto)[]
        {
            (null, RealizedVolatilityStatus.Insufficient, "VolatilityUnavailable"),
            (null, RealizedVolatilityStatus.InvalidPriceData, "InvalidVolatilityData"),
            (0.5m, RealizedVolatilityStatus.Insufficient, "InvalidVolatilityData"),
            (0.5m, RealizedVolatilityStatus.InvalidPriceData, "InvalidVolatilityData"),
            (null, RealizedVolatilityStatus.Available, "InvalidVolatilityData"),
            (-0.1m, RealizedVolatilityStatus.Available, "InvalidVolatilityData"),
            (0.5m, (RealizedVolatilityStatus)int.MaxValue, "InvalidVolatilityData")
        };

        foreach (var (value, status, expectedVeto) in cases)
        {
            var opportunity = _detector.Evaluate(features with
            {
                SessionRealizedVolatilityPercent = value,
                SessionRealizedVolatilityStatus = status
            }, _configuration);

            Assert.Contains(expectedVeto, opportunity.ActiveVetoes);
            Assert.Equal(OpportunityClassification.AvoidNoTrade, opportunity.Classification);
        }
    }

    [Fact]
    public void Validates_and_serializes_the_versioned_volatility_threshold()
    {
        var configuration = new ScannerConfiguration();
        configuration.Validate();

        Assert.Equal("scanner-config-v16", configuration.Version);
        Assert.Equal("features-v15", configuration.FeatureSetVersion);
        Assert.Equal("momentum-continuation-v1", configuration.SetupDefinitionVersion);
        Assert.Equal(1.00m, configuration.MaximumSessionRealizedVolatilityPercent);
        Assert.Equal(100_000m, configuration.MinimumShortHorizonDollarVolumeUsd);
        Assert.Equal(configuration, JsonSerializer.Deserialize<ScannerConfiguration>(
            JsonSerializer.Serialize(configuration)));
        Assert.Throws<InvalidOperationException>(() =>
            (configuration with { MaximumSessionRealizedVolatilityPercent = 0m }).Validate());
        Assert.Throws<InvalidOperationException>(() =>
            (configuration with { MaximumSessionRealizedVolatilityPercent = -0.01m }).Validate());
        Assert.Throws<InvalidOperationException>(() =>
            (configuration with { MinimumShortHorizonDollarVolumeUsd = 0m }).Validate());
        Assert.Throws<InvalidOperationException>(() =>
            (configuration with { MinimumShortHorizonDollarVolumeUsd = -0.01m }).Validate());
        (configuration with { MaximumSessionRealizedVolatilityPercent = decimal.MaxValue }).Validate();
        (configuration with { MinimumShortHorizonDollarVolumeUsd = decimal.MaxValue }).Validate();
    }

    [Fact]
    public void Produces_probable_short_when_momentum_and_vwap_align_downward()
    {
        var state = new SymbolEventState("MOE", SessionId, _configuration.MaximumStateEvents);
        state.Add(new TradeEvent("MOE", SessionId, AsOfUtc.AddSeconds(-30), AsOfUtc, "Fixture", "short-1", 150.40m, 1200));
        state.Add(new TradeEvent("MOE", SessionId, AsOfUtc.AddSeconds(-20), AsOfUtc, "Fixture", "short-2", 150.20m, 900));
        state.Add(new TradeEvent("MOE", SessionId, AsOfUtc.AddSeconds(-10), AsOfUtc, "Fixture", "short-3", 150.08m, 1400));
        state.Add(new TradeEvent("MOE", SessionId, AsOfUtc.AddSeconds(-1), AsOfUtc, "Fixture", "short-4", 149.90m, 1800));
        state.Add(new QuoteEvent(
            "MOE", SessionId, AsOfUtc, AsOfUtc, "Fixture", "short-quote", 149.89m, 149.91m, 500, 500));
        var features = _calculator.Calculate(state, AsOfUtc, _configuration);

        var opportunity = _detector.Evaluate(features, _configuration);

        Assert.Equal(OpportunityClassification.ProbableShort, opportunity.Classification);
        Assert.Equal(SetupBias.Short, opportunity.Bias);
        Assert.Empty(opportunity.ActiveVetoes);
        Assert.Contains("Short invalidated", opportunity.InvalidationCondition);
    }

    [Fact]
    public void Classifies_directional_momentum_with_vwap_conflict_as_watch()
    {
        var state = new SymbolEventState("MOE", SessionId, _configuration.MaximumStateEvents);
        state.Add(new TradeEvent("MOE", SessionId, AsOfUtc.AddSeconds(-30), AsOfUtc, "Fixture", "watch-1", 149.00m, 10));
        state.Add(new TradeEvent("MOE", SessionId, AsOfUtc.AddSeconds(-20), AsOfUtc, "Fixture", "watch-2", 151.00m, 1000));
        state.Add(new TradeEvent("MOE", SessionId, AsOfUtc.AddSeconds(-10), AsOfUtc, "Fixture", "watch-3", 150.85m, 1000));
        state.Add(new TradeEvent("MOE", SessionId, AsOfUtc.AddSeconds(-1), AsOfUtc, "Fixture", "watch-4", 150.90m, 1000));
        state.Add(new QuoteEvent(
            "MOE", SessionId, AsOfUtc, AsOfUtc, "Fixture", "watch-quote", 150.89m, 150.91m, 500, 500));
        var features = _calculator.Calculate(state, AsOfUtc, _configuration) with
        {
            SessionRealizedVolatilityPercent = 0.5m,
            SessionRealizedVolatilityStatus = RealizedVolatilityStatus.Available
        };

        var opportunity = _detector.Evaluate(features, _configuration);

        Assert.Equal(OpportunityClassification.Watch, opportunity.Classification);
        Assert.Equal(SetupBias.Long, opportunity.Bias);
        Assert.Contains(opportunity.ContradictoryEvidence, item => item.Code == "VwapConflict");
    }

    [Fact]
    public void Reprocessing_identical_as_of_input_preserves_snapshot_and_opportunity_identity()
    {
        var firstFeatures = _calculator.Calculate(CreateState(), AsOfUtc, _configuration);
        var secondFeatures = _calculator.Calculate(CreateState(), AsOfUtc, _configuration);

        var firstOpportunity = _detector.Evaluate(firstFeatures, _configuration);
        var secondOpportunity = _detector.Evaluate(secondFeatures, _configuration);

        Assert.Equal(firstFeatures.Id, secondFeatures.Id);
        Assert.Equal(firstOpportunity.Id, secondOpportunity.Id);
    }

    [Fact]
    public void Rejects_an_otherwise_strong_setup_when_quote_is_stale()
    {
        var features = _calculator.Calculate(
            CreateState(quoteAge: TimeSpan.FromSeconds(_configuration.MaximumQuoteAgeSeconds + 1)),
            AsOfUtc,
            _configuration);

        var opportunity = _detector.Evaluate(features, _configuration);

        Assert.Equal(OpportunityClassification.AvoidNoTrade, opportunity.Classification);
        Assert.Contains("StaleQuote", opportunity.ActiveVetoes);
        Assert.Contains(opportunity.SupportingEvidence, item => item.Code == "DirectionalMomentum");
    }

    [Fact]
    public void Rejects_an_invalid_quote_and_reports_the_vetoes()
    {
        var features = _calculator.Calculate(CreateState(bid: 150.60m, ask: 150.40m), AsOfUtc, _configuration) with
        {
            SessionRealizedVolatilityPercent = 1.00m,
            SessionRealizedVolatilityStatus = RealizedVolatilityStatus.Available
        };

        var opportunity = _detector.Evaluate(features, _configuration);

        Assert.Equal(OpportunityClassification.AvoidNoTrade, opportunity.Classification);
        Assert.Contains("InvalidQuoteOrPrice", opportunity.ActiveVetoes);
        Assert.Contains("ExcessiveSpread", opportunity.ActiveVetoes);
        Assert.Contains("ExcessiveRealizedVolatility", opportunity.ActiveVetoes);
        Assert.Equal(DataQualityStatus.Degraded, features.DataQuality);
    }

    [Fact]
    public void Rejects_a_quote_with_excessive_spread()
    {
        var features = _calculator.Calculate(CreateState(bid: 149m, ask: 151m), AsOfUtc, _configuration);

        var opportunity = _detector.Evaluate(features, _configuration);

        Assert.Contains("ExcessiveSpread", opportunity.ActiveVetoes);
        Assert.Equal(OpportunityClassification.AvoidNoTrade, opportunity.Classification);
    }

    [Fact]
    public void Rejects_invalid_trade_price_data()
    {
        var state = CreateState();
        state.Add(new TradeEvent("MOE", SessionId, AsOfUtc, AsOfUtc, "Fixture", "invalid-trade", 0m, 100));
        var features = _calculator.Calculate(state, AsOfUtc, _configuration);

        var opportunity = _detector.Evaluate(features, _configuration);

        Assert.False(features.IsPriceDataValid);
        Assert.Contains("InvalidQuoteOrPrice", opportunity.ActiveVetoes);
        Assert.Equal(OpportunityClassification.AvoidNoTrade, opportunity.Classification);
        Assert.Equal(DataQualityStatus.Degraded, features.DataQuality);
    }

    [Fact]
    public void Vetoes_when_required_feature_data_is_missing()
    {
        var state = new SymbolEventState("MOE", SessionId, _configuration.MaximumStateEvents);
        state.Add(new TradeEvent("MOE", SessionId, AsOfUtc.AddSeconds(-1), AsOfUtc, "Fixture", "only-trade", 150m, 10));
        var features = _calculator.Calculate(state, AsOfUtc, _configuration);

        var opportunity = _detector.Evaluate(features, _configuration);

        Assert.Equal(OpportunityClassification.AvoidNoTrade, opportunity.Classification);
        Assert.Contains("InsufficientRequiredFeatureData", opportunity.ActiveVetoes);
    }

    [Fact]
    public void Orders_late_events_and_ignores_duplicate_sequence_identity()
    {
        var state = new SymbolEventState("MOE", SessionId, 10);
        var late = new TradeEvent("MOE", SessionId, AsOfUtc.AddSeconds(-2), AsOfUtc, "Fixture", "same-id", 100m, 10);
        var current = new TradeEvent("MOE", SessionId, AsOfUtc.AddSeconds(-1), AsOfUtc, "Fixture", "new-id", 101m, 10);

        Assert.True(state.Add(current));
        Assert.True(state.Add(late));
        Assert.False(state.Add(late with { Price = 102m }));

        var ordered = state.GetEventsAsOf(AsOfUtc).OfType<TradeEvent>().ToArray();
        Assert.Equal(new[] { 100m, 101m }, ordered.Select(item => item.Price));
    }

    [Fact]
    public void Vetoes_an_incomplete_bounded_session_after_event_eviction()
    {
        var state = new SymbolEventState("MOE", SessionId, 3);
        state.Add(new TradeEvent("MOE", SessionId, AsOfUtc.AddSeconds(-4), AsOfUtc, "Fixture", "bounded-1", 150m, 10));
        state.Add(new TradeEvent("MOE", SessionId, AsOfUtc.AddSeconds(-3), AsOfUtc, "Fixture", "bounded-2", 150.1m, 10));
        state.Add(new TradeEvent("MOE", SessionId, AsOfUtc.AddSeconds(-2), AsOfUtc, "Fixture", "bounded-3", 150.2m, 10));
        state.Add(new TradeEvent("MOE", SessionId, AsOfUtc.AddSeconds(-1), AsOfUtc, "Fixture", "bounded-4", 150.3m, 10));
        state.Add(new QuoteEvent(
            "MOE", SessionId, AsOfUtc, AsOfUtc, "Fixture", "bounded-quote", 150.29m, 150.31m, 500, 500));

        var features = _calculator.Calculate(state, AsOfUtc, _configuration);
        var opportunity = _detector.Evaluate(features, _configuration);

        Assert.False(features.HasCompleteSessionData);
        Assert.Contains("InsufficientRequiredFeatureData", opportunity.ActiveVetoes);
        Assert.Contains(opportunity.ContradictoryEvidence, item => item.Code == "TruncatedEventState");
    }

    private SymbolEventState CreateState(
        TimeSpan? quoteAge = null,
        decimal bid = 150.39m,
        decimal ask = 150.41m)
    {
        var state = new SymbolEventState("MOE", SessionId, _configuration.MaximumStateEvents);
        state.Add(new TradeEvent("MOE", SessionId, AsOfUtc.AddSeconds(-42), AsOfUtc, "Fixture", "trade-1", 149.90m, 1200));
        state.Add(new TradeEvent("MOE", SessionId, AsOfUtc.AddSeconds(-32), AsOfUtc, "Fixture", "trade-2", 150.08m, 900));
        state.Add(new TradeEvent("MOE", SessionId, AsOfUtc.AddSeconds(-18), AsOfUtc, "Fixture", "trade-3", 150.23m, 1400));
        state.Add(new TradeEvent("MOE", SessionId, AsOfUtc.AddSeconds(-1), AsOfUtc, "Fixture", "trade-4", 150.40m, 1800));
        state.Add(new QuoteEvent(
            "MOE",
            SessionId,
            AsOfUtc - (quoteAge ?? TimeSpan.Zero),
            AsOfUtc,
            "Fixture",
            "quote-1",
            bid,
            ask,
            500,
            500));
        return state;
    }
}
