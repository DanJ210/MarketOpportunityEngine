using System.Text.Json;
using MarketOpportunity.Analytics;
using MarketOpportunity.Domain;

namespace MarketOpportunity.UnitTests;

public sealed class MarketSituationEvaluatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 4, 14, 30, 0, TimeSpan.Zero);
    private static readonly MarketDataCapabilities Capabilities = new(
        "UnitTest",
        "Recorded",
        MarketDataCoverage.Synthetic,
        false,
        true,
        true);
    private readonly MarketSituationEvaluator _evaluator = new();
    private readonly ScannerConfiguration _configuration = new();

    [Fact]
    public void Collects_all_approved_complete_prior_session_sources_with_provenance()
    {
        var support = Candidate(StructuralLevelType.Support, 99m, 1);
        var resistance = Candidate(StructuralLevelType.Resistance, 101m, 2);
        var context = AvailableContext([support], [resistance], priorHigh: 102m, priorLow: 98m);

        var result = Evaluate(100m, context);

        Assert.Collection(
            result.EligibleLevels,
            item => Assert.Equal(ImportantPriceLevelSource.PriorSessionLow, item.Source),
            item => Assert.Equal(ImportantPriceLevelSource.PriorSwingLow, item.Source),
            item => Assert.Equal(ImportantPriceLevelSource.PriorSwingHigh, item.Source),
            item => Assert.Equal(ImportantPriceLevelSource.PriorSessionHigh, item.Source));
        Assert.Equal(MarketSituationContextMode.PriorSessionEnhanced, result.ContextMode);
        Assert.All(result.EligibleLevels, level =>
        {
            Assert.Equal("prior-session", level.SourceSessionId);
            Assert.Equal(context.ContextId, level.SourceContextId);
            Assert.Equal("Complete prior-session history", level.SourceCompleteness);
            Assert.Equal("test-history", level.HistoricalSource);
            Assert.Equal("prior-session-context-v1", level.ContextDefinitionVersion);
        });
        var priorSwingLevel = Assert.Single(
            result.EligibleLevels,
            item => item.Source == ImportantPriceLevelSource.PriorSwingLow);
        Assert.Equal(support.OriginEventIdentity, priorSwingLevel.SourceEventIdentity);
        Assert.Equal(support.ConfirmationEventIdentity, priorSwingLevel.ConfirmationEventIdentity);
    }

    [Theory]
    [InlineData(PriorSessionContextStatus.Partial)]
    [InlineData(PriorSessionContextStatus.Unavailable)]
    [InlineData(PriorSessionContextStatus.Invalid)]
    public void Excludes_non_complete_prior_context(PriorSessionContextStatus status)
    {
        var context = AvailableContext(
            [Candidate(StructuralLevelType.Support, 99m, 1)],
            [Candidate(StructuralLevelType.Resistance, 101m, 2)],
            priorHigh: 102m,
            priorLow: 98m) with { Completeness = status };

        var result = Evaluate(100m, context);

        Assert.Empty(result.EligibleLevels);
        Assert.Equal(MarketSituationContextMode.CurrentSessionOnly, result.ContextMode);
        Assert.Equal(MarketSituationStatus.NoEligibleLevels, result.Status);
    }

    [Fact]
    public void Excludes_prior_levels_when_coverage_or_session_provenance_is_incomplete()
    {
        var complete = AvailableContext(
            [Candidate(StructuralLevelType.Support, 99m, 1)],
            [],
            priorHigh: null,
            priorLow: null);
        var uncovered = complete with { IsSessionEndCovered = false };
        var wrongSession = complete with { CurrentSessionId = "other-session" };

        Assert.Empty(Evaluate(100m, uncovered).EligibleLevels);
        Assert.Empty(Evaluate(100m, wrongSession).EligibleLevels);
    }

    [Fact]
    public void Collects_all_current_confirmed_swings_and_falls_back_when_prior_context_is_unavailable()
    {
        var events = CreateTrades([101m, 100.5m, 99m, 101m, 100.8m, 100m]);
        var snapshot = Snapshot(events, _configuration);
        var unavailable = UnavailableContext();

        var result = _evaluator.Evaluate(
            snapshot, events, ToView(unavailable, snapshot.ReferencePrice), _configuration, snapshot.AsOfUtc);

        Assert.Equal(MarketSituationStatus.Available, result.Status);
        Assert.Equal(MarketSituationContextMode.CurrentSessionOnly, result.ContextMode);
        Assert.Contains(result.EligibleLevels, item => item.Source == ImportantPriceLevelSource.CurrentSwingLow);
        Assert.Contains(result.EligibleLevels, item => item.Source == ImportantPriceLevelSource.CurrentSwingHigh);
        Assert.All(result.EligibleLevels, item =>
        {
            Assert.Equal("current-session", item.SourceSessionId);
            Assert.Equal("Complete current-session retained event history", item.SourceCompleteness);
            Assert.NotNull(item.SourceEventIdentity);
            Assert.NotNull(item.ConfirmationEventIdentity);
        });
    }

    [Fact]
    public void Current_session_levels_are_excluded_for_incomplete_or_invalid_structure()
    {
        var events = CreateTrades([101m, 100.5m, 99m, 101m, 100.8m, 100m]);
        var snapshot = Snapshot(events, _configuration);
        var incomplete = snapshot with { HasCompleteSessionData = false };
        var invalid = snapshot with { IsPriceDataValid = false };
        var unavailableStructure = snapshot with { MarketStructureStatus = MarketStructureStatus.Insufficient };

        Assert.Empty(_evaluator.Evaluate(
            incomplete, events, null, _configuration, snapshot.AsOfUtc).EligibleLevels);
        Assert.Empty(_evaluator.Evaluate(
            invalid, events, null, _configuration, snapshot.AsOfUtc).EligibleLevels);
        Assert.Empty(_evaluator.Evaluate(
            unavailableStructure, events, null, _configuration, snapshot.AsOfUtc).EligibleLevels);
    }

    [Fact]
    public void Keeps_nearest_support_and_resistance_by_exact_decimal_distance()
    {
        var context = AvailableContext(
            [
                Candidate(StructuralLevelType.Support, 99m, 1),
                Candidate(StructuralLevelType.Support, 99.5m, 2)
            ],
            [
                Candidate(StructuralLevelType.Resistance, 100.5m, 3),
                Candidate(StructuralLevelType.Resistance, 101m, 4)
            ],
            priorHigh: null,
            priorLow: null);

        var result = Evaluate(100m, context);

        Assert.Equal(99.5m, result.NearestSupportLevel!.Price);
        Assert.Equal(100.5m, result.NearestResistanceLevel!.Price);
        Assert.Null(result.SupportInteraction);
        Assert.Null(result.ResistanceInteraction);
    }

    [Fact]
    public void Resolves_equal_distance_by_prior_swing_then_current_swing_then_prior_extreme()
    {
        var events = CreateTrades([101m, 100.5m, 99.9m, 101m, 100.8m, 100m]);
        var snapshot = Snapshot(events, _configuration);
        var context = AvailableContext(
            [Candidate(StructuralLevelType.Support, 99.9m, 5)],
            [],
            priorHigh: null,
            priorLow: 99.9m);

        var result = _evaluator.Evaluate(
            snapshot, events, ToView(context, snapshot.ReferencePrice), _configuration, snapshot.AsOfUtc);

        Assert.Equal(99.9m, result.NearestSupportLevel!.Price);
        Assert.Equal(ImportantPriceLevelSource.PriorSwingLow, result.NearestSupportLevel.Source);
        Assert.Equal(3, result.EligibleLevels.Count(item => item.Role == ImportantPriceLevelRole.Support));

        var currentWinsTieWithPriorExtreme = AvailableContext([], [], null, 99.9m);
        var tiedSources = _evaluator.Evaluate(
            snapshot,
            events,
            ToView(currentWinsTieWithPriorExtreme, snapshot.ReferencePrice),
            _configuration,
            snapshot.AsOfUtc);
        Assert.Equal(ImportantPriceLevelSource.CurrentSwingLow, tiedSources.NearestSupportLevel!.Source);
    }

    [Fact]
    public void Preserves_simultaneous_support_and_resistance_interactions_and_uses_support_on_primary_tie()
    {
        var context = AvailableContext(
            [Candidate(StructuralLevelType.Support, 100m, 1)],
            [Candidate(StructuralLevelType.Resistance, 100m, 2)],
            priorHigh: null,
            priorLow: null);

        var result = Evaluate(100m, context);

        Assert.Equal(MarketSituationType.TestingSupport, result.SupportInteraction!.SituationType);
        Assert.Equal(MarketSituationType.TestingResistance, result.ResistanceInteraction!.SituationType);
        Assert.Equal(MarketSituationSide.Support, result.PrimarySituation!.Side);
        Assert.Equal(result.SupportInteraction.Level.Id, result.PrimarySituation.LevelId);
    }

    [Fact]
    public void Builds_exact_decimal_test_zone_and_support_boundary_classifications()
    {
        var level = Candidate(StructuralLevelType.Support, 100m, 1);
        var context = AvailableContext([level], [], null, null);
        var baseAssessment = Evaluate(100m, context);
        var zone = baseAssessment.SupportInteraction!;

        Assert.Equal(99.9m, zone.ZoneLow);
        Assert.Equal(100.1m, zone.ZoneHigh);
        Assert.Equal(0m, zone.SignedDistanceFromLevel);
        Assert.Equal(0m, zone.PercentDistanceFromReference);
        Assert.Equal(MarketSituationType.TestingSupport, zone.SituationType);
        Assert.Equal(MarketSituationType.TestingSupport, Evaluate(100.1m, context).SupportInteraction!.SituationType);
        Assert.Equal(MarketSituationType.TestingSupport, Evaluate(99.9m, context).SupportInteraction!.SituationType);
        Assert.Equal(MarketSituationType.TestingSupport, Evaluate(100m, context).SupportInteraction!.SituationType);

        var approachBoundary = 100.1m + 100.1m * 0.25m / 100m;
        var approach = Evaluate(approachBoundary, context).SupportInteraction!;
        Assert.Equal(MarketSituationType.ApproachingSupport, approach.SituationType);
        Assert.Equal(0.25m, approach.DistanceToActiveZonePercent);
        Assert.Null(Evaluate(approachBoundary + 0.00001m, context).SupportInteraction);

        var crossedBoundary = 99.9m - 99.9m * 0.25m / 100m;
        var crossed = Evaluate(crossedBoundary, context).SupportInteraction!;
        Assert.Equal(MarketSituationType.CrossedSupport, crossed.SituationType);
        Assert.Equal(0.25m, crossed.DistanceToActiveZonePercent);
        Assert.Null(Evaluate(crossedBoundary - 0.00001m, context).SupportInteraction);
    }

    [Fact]
    public void Builds_mirrored_resistance_boundary_classifications()
    {
        var context = AvailableContext(
            [],
            [Candidate(StructuralLevelType.Resistance, 100m, 1)],
            null,
            null);
        var approachBoundary = 99.9m - 99.9m * 0.25m / 100m;
        var crossedBoundary = 100.1m + 100.1m * 0.25m / 100m;

        Assert.Equal(MarketSituationType.TestingResistance, Evaluate(100m, context).ResistanceInteraction!.SituationType);
        Assert.Equal(MarketSituationType.TestingResistance, Evaluate(99.9m, context).ResistanceInteraction!.SituationType);
        Assert.Equal(MarketSituationType.TestingResistance, Evaluate(100.1m, context).ResistanceInteraction!.SituationType);
        var approaching = Evaluate(approachBoundary, context).ResistanceInteraction!;
        Assert.Equal(MarketSituationType.ApproachingResistance, approaching.SituationType);
        Assert.Equal(0.25m, approaching.DistanceToActiveZonePercent);
        Assert.Null(Evaluate(approachBoundary - 0.00001m, context).ResistanceInteraction);
        var crossed = Evaluate(crossedBoundary, context).ResistanceInteraction!;
        Assert.Equal(MarketSituationType.CrossedResistance, crossed.SituationType);
        Assert.Equal(0.25m, crossed.DistanceToActiveZonePercent);
        Assert.Null(Evaluate(crossedBoundary + 0.00001m, context).ResistanceInteraction);
    }

    [Fact]
    public void Reports_no_active_interaction_when_approach_is_too_far_or_no_levels_exist()
    {
        var context = AvailableContext(
            [Candidate(StructuralLevelType.Support, 100m, 1)],
            [],
            null,
            null);
        var tooFar = Evaluate(100.35026m, context);
        var none = Evaluate(100m, null);

        Assert.Null(tooFar.SupportInteraction);
        Assert.Equal(100m, tooFar.NearestSupportLevel!.Price);
        Assert.Equal(MarketSituationStatus.Available, tooFar.Status);
        Assert.Equal(MarketSituationStatus.NoEligibleLevels, none.Status);
        Assert.Empty(none.EligibleLevels);
        Assert.Null(none.PrimarySituation);
    }

    [Fact]
    public void Requires_a_valid_positive_reference_price_to_compute_distance()
    {
        var invalidPriceEvents = CreateTrades([0m]);
        var invalidSnapshot = Snapshot(invalidPriceEvents, _configuration);
        var result = _evaluator.Evaluate(
            invalidSnapshot,
            invalidPriceEvents,
            ToView(AvailableContext([], [], 101m, 99m), invalidSnapshot.ReferencePrice),
            _configuration,
            invalidSnapshot.AsOfUtc);

        Assert.Equal(MarketSituationStatus.NotComputable, result.Status);
        Assert.Null(result.CurrentReferencePrice);
        Assert.Null(result.SupportInteraction);
        Assert.Null(result.ResistanceInteraction);
        Assert.Null(result.NearestSupportLevel);
        Assert.Null(result.NearestResistanceLevel);
    }

    [Fact]
    public void Validates_test_zone_and_approach_configuration()
    {
        _configuration.Validate();
        Assert.Throws<InvalidOperationException>(() =>
            (_configuration with
            {
                MarketSituation = new MarketSituationConfiguration { TestZonePercent = 0m }
            }).Validate());
        Assert.Throws<InvalidOperationException>(() =>
            (_configuration with
            {
                MarketSituation = new MarketSituationConfiguration { TestZonePercent = 100m }
            }).Validate());
        Assert.Throws<InvalidOperationException>(() =>
            (_configuration with
            {
                MarketSituation = new MarketSituationConfiguration
                {
                    TestZonePercent = 0.25m,
                    ApproachDistancePercent = 0.25m
                }
            }).Validate());
        Assert.Throws<InvalidOperationException>(() =>
            (_configuration with
            {
                MarketSituation = new MarketSituationConfiguration
                {
                    TestZonePercent = 0.25m,
                    ApproachDistancePercent = 0.1m
                }
            }).Validate());
    }

    [Fact]
    public void Assessment_identity_is_deterministic_material_and_excludes_evaluation_time()
    {
        var context = AvailableContext(
            [Candidate(StructuralLevelType.Support, 99.9m, 1)],
            [],
            null,
            null);
        var snapshot = Snapshot(CreateTrades([100m]), _configuration);
        var contextView = ToView(context, snapshot.ReferencePrice);
        var first = _evaluator.Evaluate(snapshot, CreateTrades([100m]), contextView, _configuration, Start);
        var replay = _evaluator.Evaluate(snapshot, CreateTrades([100m]), contextView, _configuration, Start.AddMinutes(1));
        var changedPriceSnapshot = Snapshot(CreateTrades([100.01m]), _configuration);
        var changedPrice = _evaluator.Evaluate(
            changedPriceSnapshot,
            CreateTrades([100.01m]),
            ToView(context, changedPriceSnapshot.ReferencePrice),
            _configuration,
            Start);
        var changedLevelContext = AvailableContext(
            [Candidate(StructuralLevelType.Support, 99.8m, 1)],
            [],
            null,
            null);
        var changedLevel = _evaluator.Evaluate(
            snapshot, CreateTrades([100m]), ToView(changedLevelContext, snapshot.ReferencePrice), _configuration, Start);
        var changedConfiguration = _configuration with
        {
            Version = "scanner-config-v20",
            MarketSituation = new MarketSituationConfiguration
            {
                TestZonePercent = 0.11m,
                ApproachDistancePercent = 0.26m
            }
        };
        var changedConfig = _evaluator.Evaluate(
            snapshot, CreateTrades([100m]), contextView, changedConfiguration, Start);

        Assert.Equal(first.Id, replay.Id);
        Assert.NotEqual(first.Id, changedPrice.Id);
        Assert.NotEqual(first.Id, changedLevel.Id);
        Assert.NotEqual(first.Id, changedConfig.Id);
        Assert.Equal(first.SupportInteraction, replay.SupportInteraction);
        Assert.NotEqual(first.TestZonePercent, changedConfig.TestZonePercent);
    }

    [Fact]
    public void Excludes_future_events_and_isolates_symbol_and_session_inputs()
    {
        var events = CreateTrades([101m, 100.5m, 99m, 101m, 100.8m, 100m]);
        var snapshot = Snapshot(events.Take(1).ToArray(), _configuration);
        var future = ((TradeEvent)events[^1]) with
        {
            SequenceId = "future",
            EventTimeUtc = snapshot.AsOfUtc.AddSeconds(1),
            Price = 90m
        };
        var wrongSymbol = events[^1] with { Symbol = "OTHER", SequenceId = "wrong-symbol" };
        var wrongSession = events[^1] with { SessionId = "other-session", SequenceId = "wrong-session" };
        var reference = _evaluator.Evaluate(snapshot, events.Take(1).ToArray(), null, _configuration, Start);
        var replay = _evaluator.Evaluate(
            snapshot, [.. events, future, wrongSymbol, wrongSession], null, _configuration, Start);

        Assert.Equal(reference.Id, replay.Id);
        Assert.Equal(reference.EligibleLevels, replay.EligibleLevels);
        Assert.Equal(reference.CurrentReferencePrice, replay.CurrentReferencePrice);
    }

    [Fact]
    public void Assessment_round_trips_with_structured_levels_and_interactions()
    {
        var context = AvailableContext(
            [Candidate(StructuralLevelType.Support, 100m, 1)],
            [Candidate(StructuralLevelType.Resistance, 100.1m, 2)],
            102m,
            98m);
        var assessment = Evaluate(100m, context);
        var json = JsonSerializer.Serialize(assessment);
        var restored = JsonSerializer.Deserialize<MarketSituationAssessment>(json);

        Assert.NotNull(restored);
        Assert.Equal(assessment.Id, restored.Id);
        Assert.Equal(assessment.EligibleLevels, restored.EligibleLevels);
        Assert.Equal(assessment.SupportInteraction, restored.SupportInteraction);
        Assert.Equal(assessment.ResistanceInteraction, restored.ResistanceInteraction);
    }

    private MarketSituationAssessment Evaluate(
        decimal price,
        PriorSessionContext? context) =>
        EvaluateView(price, context is null ? null : ToView(context, price));

    private MarketSituationAssessment EvaluateView(decimal price, PriorSessionContextView? context)
    {
        var events = CreateTrades([price]);
        var snapshot = Snapshot(events, _configuration);
        return _evaluator.Evaluate(snapshot, events, context, _configuration, snapshot.AsOfUtc);
    }

    private static PriorSessionContextView ToView(PriorSessionContext context, decimal? price) =>
        new(context, price, null, null, null, null, null, null, null, null, null, null);

    private static FeatureSnapshot Snapshot(
        IReadOnlyList<MarketEvent> events,
        ScannerConfiguration configuration)
    {
        var symbol = events.Count == 0 ? "TEST" : events[0].Symbol;
        var session = events.Count == 0 ? "current-session" : events[0].SessionId;
        var state = new SymbolEventState(symbol, session, Math.Max(1, events.Count));
        foreach (var item in events)
        {
            state.Add(item);
        }

        var asOf = events.Count == 0 ? Start : events.Max(item => item.EventTimeUtc);
        return new FeatureCalculator().Calculate(state, asOf, configuration, Capabilities);
    }

    private static MarketEvent[] CreateTrades(IReadOnlyList<decimal> prices) =>
        prices.Select((price, index) => (MarketEvent)new TradeEvent(
            "TEST",
            "current-session",
            Start.AddSeconds(index),
            Start.AddSeconds(index + 1),
            "UnitTest",
            $"trade-{index:D3}",
            price,
            10)).ToArray();

    private static PriorSessionContext AvailableContext(
        IReadOnlyList<StructuralLevelCandidate> supports,
        IReadOnlyList<StructuralLevelCandidate> resistances,
        decimal? priorHigh,
        decimal? priorLow) =>
        UnavailableContext() with
        {
            PriorSessionId = "prior-session",
            PriorSessionStartUtc = Start.AddDays(-1),
            PriorSessionEndUtc = Start.AddDays(-1).AddHours(6),
            PriorSessionHigh = priorHigh,
            PriorSessionLow = priorLow,
            SupportCandidates = supports,
            ResistanceCandidates = resistances,
            Completeness = PriorSessionContextStatus.Available,
            CompletenessReason = "Complete test prior-session history.",
            MarketData = Capabilities,
            HistoricalSource = "test-history",
            CoverageStartUtc = Start.AddDays(-1),
            CoverageEndUtc = Start.AddDays(-1).AddHours(6),
            IsSessionStartCovered = true,
            IsSessionEndCovered = true,
            HasKnownGaps = false
        };

    private static PriorSessionContext UnavailableContext() =>
        new PriorSessionContextBuilder(new FeatureCalculator()).BuildUnavailable(
            "TEST",
            "current-session",
            "Test prior context unavailable.");

    private static StructuralLevelCandidate Candidate(
        StructuralLevelType role,
        decimal price,
        int id)
    {
        var swingId = Id(id);
        return new StructuralLevelCandidate(
            swingId,
            role,
            "TEST",
            "prior-session",
            price,
            Start.AddDays(-1).AddMinutes(id),
            Start.AddDays(-1).AddMinutes(id + 2),
            swingId,
            $"origin-{id}",
            $"confirmation-{id}",
            "features-v16",
            "session-swing-fractal-v1");
    }

    private static Guid Id(int value) => new(value, 0, 0, new byte[8]);
}
