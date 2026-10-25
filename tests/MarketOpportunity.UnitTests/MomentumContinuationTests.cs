using MarketOpportunity.Analytics;
using MarketOpportunity.Domain;

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
        var features = _calculator.Calculate(state, AsOfUtc, _configuration);

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
        var features = _calculator.Calculate(CreateState(bid: 150.60m, ask: 150.40m), AsOfUtc, _configuration);

        var opportunity = _detector.Evaluate(features, _configuration);

        Assert.Equal(OpportunityClassification.AvoidNoTrade, opportunity.Classification);
        Assert.Contains("InvalidQuoteOrPrice", opportunity.ActiveVetoes);
        Assert.Contains("ExcessiveSpread", opportunity.ActiveVetoes);
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
