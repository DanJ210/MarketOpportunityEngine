using System.Text.Json;
using System.Text.Json.Serialization;
using MarketOpportunity.Analytics;
using MarketOpportunity.Domain;

namespace MarketOpportunity.UnitTests;

public sealed class TradePlanBuilderTests
{
    private static readonly DateTimeOffset AsOfUtc =
        new(2026, 10, 30, 14, 30, 20, TimeSpan.Zero);
    private readonly TradePlanBuilder _builder = new();

    [Fact]
    public void Builds_long_plan_from_support_to_resistance_with_decimal_midpoint_arithmetic()
    {
        var features = Features(reference: 100m, support: 98m, invalidation: 92m, resistance: 103m);
        var plan = _builder.Build(features, OpportunityFor(features, SetupBias.Long));

        Assert.Equal(TradePlanStatus.PlanDefined, plan.Status);
        Assert.Equal(TradeDirection.Long, plan.Direction);
        Assert.Equal(new PriceZone(98m, 100m), plan.EntryZone);
        Assert.Equal(99m, plan.EntryReferencePrice);
        Assert.Equal(92m, plan.InvalidationPrice);
        Assert.Equal(103m, plan.Target1!.Price);
        Assert.Equal(7m, plan.RiskPerShare);
        Assert.Equal(4m, plan.RewardPerShareToTarget1);
        Assert.Equal(4m / 7m, plan.RewardRiskRatioTarget1);
        Assert.Contains(plan.Evidence, item => item.Code == "EntryZoneFromConfirmedSupport");
        Assert.Contains(plan.Evidence, item => item.Code == "InvalidationFromLowerConfirmedSwing");
        Assert.Contains(plan.Evidence, item => item.Code == "TargetFromConfirmedResistance");
    }

    [Fact]
    public void Builds_short_plan_with_symmetric_structural_geometry()
    {
        var features = Features(
            reference: 100m,
            support: 90m,
            invalidation: 85m,
            resistance: 105m,
            invalidationHigh: 110m);
        var plan = _builder.Build(features, OpportunityFor(features, SetupBias.Short));

        Assert.Equal(TradePlanStatus.PlanDefined, plan.Status);
        Assert.Equal(TradeDirection.Short, plan.Direction);
        Assert.Equal(new PriceZone(100m, 105m), plan.EntryZone);
        Assert.Equal(102.5m, plan.EntryReferencePrice);
        Assert.Equal(110m, plan.InvalidationPrice);
        Assert.Equal(90m, plan.Target1!.Price);
        Assert.Equal(7.5m, plan.RiskPerShare);
        Assert.Equal(12.5m, plan.RewardPerShareToTarget1);
        Assert.Equal(12.5m / 7.5m, plan.RewardRiskRatioTarget1);
    }

    [Fact]
    public void Does_not_apply_an_unapproved_minimum_reward_risk_filter()
    {
        var features = Features(reference: 100m, support: 98m, invalidation: 92m, resistance: 103m);
        var opportunity = OpportunityFor(features, SetupBias.Long);
        var plan = _builder.Build(features, opportunity);

        Assert.Equal(TradePlanStatus.PlanDefined, plan.Status);
        Assert.True(plan.RewardRiskRatioTarget1 < 1m);
    }

    [Fact]
    public void Reports_insufficient_structure_when_directional_anchor_is_missing()
    {
        var longFeatures = Features(reference: 100m, support: null, invalidation: 92m, resistance: 110m);
        var shortFeatures = Features(reference: 100m, support: 90m, invalidation: 85m, resistance: null);

        var longPlan = _builder.Build(longFeatures, OpportunityFor(longFeatures, SetupBias.Long));
        var shortPlan = _builder.Build(shortFeatures, OpportunityFor(shortFeatures, SetupBias.Short));

        Assert.Equal(TradePlanStatus.InsufficientStructure, longPlan.Status);
        Assert.Equal(TradePlanStatus.InsufficientStructure, shortPlan.Status);
        Assert.Null(longPlan.EntryZone);
        Assert.Null(shortPlan.EntryZone);
    }

    [Fact]
    public void Reports_forming_when_a_target_or_invalidation_is_not_yet_supported()
    {
        var noTarget = Features(reference: 100m, support: 95m, invalidation: 90m, resistance: null);
        var noShortTarget = Features(reference: 100m, support: null, invalidation: 85m, resistance: 105m);
        var noInvalidation = Features(reference: 100m, support: 95m, invalidation: null, resistance: 110m);

        var targetPlan = _builder.Build(noTarget, OpportunityFor(noTarget, SetupBias.Long));
        var shortTargetPlan = _builder.Build(
            noShortTarget, OpportunityFor(noShortTarget, SetupBias.Short));
        var invalidationPlan = _builder.Build(noInvalidation, OpportunityFor(noInvalidation, SetupBias.Long));

        Assert.Equal(TradePlanStatus.PlanForming, targetPlan.Status);
        Assert.Null(targetPlan.Target1);
        Assert.Equal(TradePlanStatus.PlanForming, shortTargetPlan.Status);
        Assert.Null(shortTargetPlan.Target1);
        Assert.Equal(TradePlanStatus.PlanForming, invalidationPlan.Status);
        Assert.Null(invalidationPlan.InvalidationPrice);
        Assert.Null(invalidationPlan.RewardRiskRatioTarget1);
    }

    [Theory]
    [InlineData(101, 100, 110, TradePlanStatus.Rejected)]
    [InlineData(95, 100, 100, TradePlanStatus.Rejected)]
    [InlineData(95, 100, 95, TradePlanStatus.Rejected)]
    public void Rejects_long_support_or_target_geometry_that_cannot_define_positive_risk_reward(
        decimal support,
        decimal reference,
        decimal target,
        TradePlanStatus expectedStatus)
    {
        var features = Features(
            reference: reference,
            support: support,
            invalidation: support,
            resistance: target);

        var plan = _builder.Build(features, OpportunityFor(features, SetupBias.Long));

        Assert.Equal(expectedStatus, plan.Status);
        Assert.Null(plan.RewardRiskRatioTarget1);
    }

    [Fact]
    public void Rejects_short_target_inside_or_on_the_entry_zone()
    {
        var features = Features(
            reference: 100m,
            support: 100m,
            invalidation: 95m,
            resistance: 105m,
            invalidationHigh: 110m);

        var plan = _builder.Build(features, OpportunityFor(features, SetupBias.Short));

        Assert.Equal(TradePlanStatus.Rejected, plan.Status);
        Assert.Null(plan.RewardRiskRatioTarget1);
        Assert.Contains(plan.Evidence, item => item.Code == "TargetInsideEntryZone");
    }

    [Fact]
    public void Preserves_zero_width_entry_zone_without_inventing_a_width()
    {
        var features = Features(reference: 95m, support: 95m, invalidation: 90m, resistance: 110m);
        var plan = _builder.Build(features, OpportunityFor(features, SetupBias.Long));

        Assert.Equal(TradePlanStatus.PlanDefined, plan.Status);
        Assert.Equal(new PriceZone(95m, 95m), plan.EntryZone);
        Assert.Equal(95m, plan.EntryReferencePrice);
    }

    [Fact]
    public void Uses_exact_decimal_prices_and_calculates_reward_risk_without_rounding()
    {
        var features = Features(
            reference: 10.004m,
            support: 10.001m,
            invalidation: 10.000m,
            resistance: 10.010m);
        var plan = _builder.Build(features, OpportunityFor(features, SetupBias.Long));

        Assert.Equal(10.0025m, plan.EntryReferencePrice);
        Assert.Equal(0.0025m, plan.RiskPerShare);
        Assert.Equal(0.0075m, plan.RewardPerShareToTarget1);
        Assert.Equal(3m, plan.RewardRiskRatioTarget1);
    }

    [Fact]
    public void Partial_retained_structure_remains_forming_even_with_calculable_geometry()
    {
        var features = Features(
            reference: 100m,
            support: 95m,
            invalidation: 90m,
            resistance: 110m,
            hasCompleteSessionData: false);

        var plan = _builder.Build(features, OpportunityFor(features, SetupBias.Long));

        Assert.Equal(TradePlanStatus.PlanForming, plan.Status);
        Assert.NotNull(plan.EntryZone);
        Assert.NotNull(plan.RiskPerShare);
        Assert.NotNull(plan.RewardRiskRatioTarget1);
        Assert.Contains(plan.Evidence, item => item.Code == "PartialRetainedState");
    }

    [Fact]
    public void Plan_identity_is_stable_for_replay_and_changes_with_input_configuration_or_provenance()
    {
        var features = Features(reference: 100m, support: 95m, invalidation: 90m, resistance: 110m);
        var opportunity = OpportunityFor(features, SetupBias.Long);
        var first = _builder.Build(features, opportunity);
        var replay = _builder.Build(features, opportunity);
        var changedInput = _builder.Build(
            features with { Id = Guid.NewGuid(), ReferencePrice = 101m },
            opportunity with { Id = Guid.NewGuid(), ReferencePrice = 101m });
        var changedStructureFeatures = Features(
            reference: 100m, support: 94m, invalidation: 90m, resistance: 110m);
        var changedStructure = _builder.Build(
            changedStructureFeatures,
            OpportunityFor(changedStructureFeatures, SetupBias.Long));
        var changedConfiguration = _builder.Build(
            features,
            opportunity with { ConfigurationVersion = "scanner-config-next" });
        var changedProvenanceFeatures = Features(
            reference: 100m,
            support: 95m,
            invalidation: 90m,
            resistance: 110m,
            marketData: new MarketDataCapabilities(
                "AlternateFixture", "Recorded", MarketDataCoverage.Synthetic, false, true, true));
        var changedProvenance = _builder.Build(
            changedProvenanceFeatures,
            OpportunityFor(changedProvenanceFeatures, SetupBias.Long));

        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(replay));
        Assert.NotEqual(first.Id, changedInput.Id);
        Assert.NotEqual(first.Id, changedStructure.Id);
        Assert.NotEqual(first.Id, changedConfiguration.Id);
        Assert.NotEqual(first.Id, changedProvenance.Id);
        Assert.Equal(changedProvenanceFeatures.MarketData, changedProvenance.MarketData);
    }

    [Fact]
    public void Keeps_symbol_and_explicit_session_identity_isolated()
    {
        var firstFeatures = Features(symbol: "AAA", sessionId: "session-a");
        var secondFeatures = Features(symbol: "BBB", sessionId: "session-b");
        var first = _builder.Build(firstFeatures, OpportunityFor(firstFeatures, SetupBias.Long));
        var second = _builder.Build(secondFeatures, OpportunityFor(secondFeatures, SetupBias.Long));

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal("AAA", first.Symbol);
        Assert.Equal("session-a", first.SessionId);
        Assert.Equal("BBB", second.Symbol);
        Assert.Equal("session-b", second.SessionId);
    }

    [Fact]
    public void Future_events_do_not_change_as_of_snapshot_or_plan_identity()
    {
        var events = CreateTrades();
        var asOfUtc = events[^1].EventTimeUtc;
        var firstState = State(events);
        var laterState = State([.. events, new TradeEvent(
            "TST", "session-a", asOfUtc.AddSeconds(1), asOfUtc.AddSeconds(1),
            "Fixture", "future", 50m, 10)]);
        var calculator = new FeatureCalculator();
        var configuration = new ScannerConfiguration();
        var firstFeatures = AddStructuralLevels(calculator.Calculate(firstState, asOfUtc, configuration));
        var laterFeatures = AddStructuralLevels(calculator.Calculate(laterState, asOfUtc, configuration));
        var firstPlan = _builder.Build(firstFeatures, OpportunityFor(firstFeatures, SetupBias.Long));
        var laterPlan = _builder.Build(laterFeatures, OpportunityFor(laterFeatures, SetupBias.Long));

        Assert.Equal(firstFeatures.Id, laterFeatures.Id);
        Assert.Equal(firstPlan.Id, laterPlan.Id);
    }

    [Fact]
    public void Does_not_accept_a_structural_level_confirmed_after_plan_as_of_time()
    {
        var features = Features(reference: 100m, support: 95m, invalidation: 90m, resistance: 110m);
        var futureSwing = features.LatestConfirmedSwingLow! with
        {
            ConfirmedAtEventTimeUtc = AsOfUtc.AddTicks(1)
        };
        var futureLevel = MakeLevel(futureSwing, StructuralLevelType.Support, 95m);
        var futureFeatures = features with
        {
            LatestConfirmedSwingLow = futureSwing,
            NearestSupportCandidate = futureLevel
        };

        var plan = _builder.Build(futureFeatures, OpportunityFor(futureFeatures, SetupBias.Long));

        Assert.Equal(TradePlanStatus.Rejected, plan.Status);
        Assert.Null(plan.EntryZone);
        Assert.Contains(plan.Evidence, item => item.Code == "InvalidEntryStructure");
    }

    [Fact]
    public void Serializes_plan_status_prices_evidence_and_provider_provenance()
    {
        var features = Features(
            reference: 100m,
            support: 95m,
            invalidation: 90m,
            resistance: 110m,
            marketData: new MarketDataCapabilities(
                "RecordedFixture", "Synthetic", MarketDataCoverage.Synthetic, false, true, true));
        var plan = _builder.Build(features, OpportunityFor(features, SetupBias.Long));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };
        var json = JsonSerializer.Serialize(plan, options);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("PlanDefined", root.GetProperty("status").GetString());
        Assert.Equal("Long", root.GetProperty("direction").GetString());
        Assert.Equal("trade-plan-v1", root.GetProperty("planDefinitionVersion").GetString());
        Assert.Equal(95m, root.GetProperty("entryZone").GetProperty("low").GetDecimal());
        Assert.Equal(100m, root.GetProperty("entryZone").GetProperty("high").GetDecimal());
        Assert.Equal(110m, root.GetProperty("target1").GetProperty("price").GetDecimal());
        Assert.Equal("RecordedFixture", root.GetProperty("marketData").GetProperty("provider").GetString());
        Assert.Contains(root.GetProperty("evidence").EnumerateArray(),
            item => item.GetProperty("code").GetString() == "EntryZoneFromConfirmedSupport");
    }

    private static FeatureSnapshot Features(
        decimal reference = 100m,
        decimal? support = 95m,
        decimal? invalidation = 90m,
        decimal? resistance = 110m,
        decimal invalidationHigh = 115m,
        bool hasCompleteSessionData = true,
        string symbol = "TST",
        string sessionId = "session-a",
        MarketDataCapabilities? marketData = null)
    {
        marketData ??= new MarketDataCapabilities(
            "RecordedFixture", "Synthetic", MarketDataCoverage.Synthetic, false, true, true);
        var state = State(
        [
            new TradeEvent(symbol, sessionId, AsOfUtc.AddSeconds(-2), AsOfUtc.AddSeconds(-2),
                marketData.Provider, "base-1", reference - 1m, 10),
            new TradeEvent(symbol, sessionId, AsOfUtc.AddSeconds(-1), AsOfUtc.AddSeconds(-1),
                marketData.Provider, "base-2", reference, 10),
            new TradeEvent(symbol, sessionId, AsOfUtc, AsOfUtc,
                marketData.Provider, "base-3", reference, 10)
        ]);
        var snapshot = new FeatureCalculator().Calculate(
            state, AsOfUtc, new ScannerConfiguration(), marketData);

        var latestHigh = MakeSwing(symbol, sessionId, resistance ?? reference + 10m, true, "latest-high");
        var previousHigh = MakeSwing(symbol, sessionId, invalidationHigh, true, "previous-high");
        var latestLow = MakeSwing(symbol, sessionId, support ?? reference - 5m, false, "latest-low");
        var previousLow = invalidation is decimal low
            ? MakeSwing(symbol, sessionId, low, false, "previous-low")
            : null;

        return snapshot with
        {
            MarketStructureStatus = MarketStructureStatus.Available,
            HasCompleteSessionData = hasCompleteSessionData,
            LatestConfirmedSwingHigh = latestHigh,
            PreviousConfirmedSwingHigh = previousHigh,
            LatestConfirmedSwingLow = latestLow,
            PreviousConfirmedSwingLow = previousLow,
            NearestSupportCandidate = support is decimal supportPrice
                ? MakeLevel(latestLow, StructuralLevelType.Support, supportPrice)
                : null,
            NearestResistanceCandidate = resistance is decimal resistancePrice
                ? MakeLevel(latestHigh, StructuralLevelType.Resistance, resistancePrice)
                : null
        };
    }

    private static Opportunity OpportunityFor(
        FeatureSnapshot features,
        SetupBias bias,
        string configurationVersion = "scanner-config-test") =>
        new(
            DeterministicId(features.Id.ToString("N"), bias.ToString(), configurationVersion),
            features.Symbol,
            features.SessionId,
            features.AsOfUtc,
            features.AsOfUtc.AddMinutes(2),
            "Momentum Continuation",
            bias,
            bias == SetupBias.Long
                ? OpportunityClassification.ProbableLong
                : OpportunityClassification.ProbableShort,
            80,
            [],
            [],
            [],
            "Structural context is recorded separately.",
            features.ReferencePrice,
            features.FeatureSetVersion,
            "momentum-continuation-v1",
            configurationVersion,
            features.MarketData);

    private static ConfirmedSwing MakeSwing(
        string symbol,
        string sessionId,
        decimal price,
        bool high,
        string identity) =>
        new(
            DeterministicId(identity),
            symbol,
            sessionId,
            price,
            AsOfUtc.AddSeconds(high ? -1 : -2),
            AsOfUtc.AddSeconds(-1),
            $"{identity}-origin",
            $"{identity}-confirmation",
            high ? ConfirmedSwingType.ConfirmedSwingHigh : ConfirmedSwingType.ConfirmedSwingLow,
            "features-v16",
            "session-swing-fractal-v1");

    private static StructuralLevelCandidate MakeLevel(
        ConfirmedSwing swing,
        StructuralLevelType type,
        decimal price) =>
        new(
            DeterministicId(swing.Id.ToString("N"), type.ToString()),
            type,
            swing.Symbol,
            swing.SessionId,
            price,
            swing.EventTimeUtc,
            swing.ConfirmedAtEventTimeUtc,
            swing.Id,
            swing.OriginEventIdentity,
            swing.ConfirmationEventIdentity,
            swing.FeatureSetVersion,
            swing.AlgorithmVersion);

    private static FeatureSnapshot AddStructuralLevels(FeatureSnapshot snapshot)
    {
        var low = MakeSwing(snapshot.Symbol, snapshot.SessionId, 4m, false, "future-test-low");
        var priorLow = MakeSwing(snapshot.Symbol, snapshot.SessionId, 2m, false, "future-test-prior-low");
        var high = MakeSwing(snapshot.Symbol, snapshot.SessionId, 6m, true, "future-test-high");
        var priorHigh = MakeSwing(snapshot.Symbol, snapshot.SessionId, 7m, true, "future-test-prior-high");
        return snapshot with
        {
            MarketStructureStatus = MarketStructureStatus.Available,
            HasCompleteSessionData = true,
            LatestConfirmedSwingLow = low,
            PreviousConfirmedSwingLow = priorLow,
            LatestConfirmedSwingHigh = high,
            PreviousConfirmedSwingHigh = priorHigh,
            NearestSupportCandidate = MakeLevel(low, StructuralLevelType.Support, 4m),
            NearestResistanceCandidate = MakeLevel(high, StructuralLevelType.Resistance, 6m),
            ReferencePrice = 5m
        };
    }

    private static MarketEvent[] CreateTrades()
    {
        decimal[] prices = [1.5m, 1m, 5m, 3m, 2m, 4m, 3m, 7m, 5m, 4m, 6m, 5m];
        return prices.Select((price, index) => (MarketEvent)new TradeEvent(
            "TST",
            "session-a",
            AsOfUtc.AddSeconds(index - prices.Length + 1),
            AsOfUtc.AddSeconds(index - prices.Length + 1),
            "Fixture",
            $"trade-{index}",
            price,
            10)).ToArray();
    }

    private static SymbolEventState State(IReadOnlyCollection<MarketEvent> events)
    {
        var first = events.First();
        var state = new SymbolEventState(first.Symbol, first.SessionId, 1_000);
        foreach (var marketEvent in events)
        {
            state.Add(marketEvent);
        }

        return state;
    }

    private static Guid DeterministicId(params string[] values) =>
        new(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(string.Join("\u001f", values))).AsSpan(0, 16));
}
