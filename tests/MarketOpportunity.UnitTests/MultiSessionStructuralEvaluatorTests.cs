using MarketOpportunity.Analytics;
using MarketOpportunity.Domain;

namespace MarketOpportunity.UnitTests;

public sealed class MultiSessionStructuralEvaluatorTests
{
    private static readonly DateOnly FirstTradingDate = new(2026, 1, 2);
    private readonly MultiSessionStructuralEvaluator _evaluator = new();

    [Theory]
    [InlineData("100", "100", 0)]
    [InlineData("100", "100.1", 0.1)]
    [InlineData("1.00", "1.001", 0.1)]
    [InlineData("1000", "1001", 0.1)]
    public void Percentage_distance_is_exact_decimal_and_symmetric(
        string first,
        string second,
        double expected)
    {
        var firstPrice = decimal.Parse(first);
        var secondPrice = decimal.Parse(second);

        Assert.Equal((decimal)expected, MultiSessionStructuralEvaluator.PercentagePriceDistance(
            firstPrice,
            secondPrice));
        Assert.Equal(
            MultiSessionStructuralEvaluator.PercentagePriceDistance(firstPrice, secondPrice),
            MultiSessionStructuralEvaluator.PercentagePriceDistance(secondPrice, firstPrice));
    }

    [Fact]
    public void Percentage_distance_accepts_inclusive_threshold_and_rejects_just_outside()
    {
        Assert.True(MultiSessionStructuralEvaluator.IsWithinPercentageDistance(100m, 100.1m, 0.10m));
        Assert.True(MultiSessionStructuralEvaluator.IsWithinPercentageDistance(1m, 1.001m, 0.10m));
        Assert.False(MultiSessionStructuralEvaluator.IsWithinPercentageDistance(
            100m,
            100.1000001m,
            0.10m));
    }

    [Theory]
    [InlineData(0, 1, 0.1)]
    [InlineData(-1, 1, 0.1)]
    [InlineData(1, 1, -0.1)]
    public void Percentage_distance_rejects_invalid_prices_and_thresholds(
        int first,
        int second,
        double threshold)
    {
        if (first <= 0 || second <= 0)
        {
            Assert.Null(MultiSessionStructuralEvaluator.PercentagePriceDistance(first, second));
        }

        Assert.False(MultiSessionStructuralEvaluator.IsWithinPercentageDistance(
            first,
            second,
            (decimal)threshold));
    }

    [Fact]
    public void Zone_builder_uses_exact_bounds_and_prevents_transitive_chaining()
    {
        var sessions = CompleteSessions(
            (110m, 100m),
            (110.1m, 100.05m),
            (110.2m, 100.11m));

        var result = Evaluate(sessions);
        var support = result.SupportZones;

        Assert.Collection(
            support.OrderBy(item => item.LowerBound),
            first =>
            {
                Assert.Equal(100m, first.LowerBound);
                Assert.Equal(100.05m, first.UpperBound);
                Assert.Equal(["s1", "s2"], first.SourceSessionIds);
            },
            second =>
            {
                Assert.Equal(100.11m, second.LowerBound);
                Assert.Equal(100.11m, second.UpperBound);
                Assert.Equal(["s3"], second.SourceSessionIds);
            });
    }

    [Fact]
    public void Support_and_resistance_at_the_same_price_remain_separate()
    {
        var sessions = CompleteSessions(
            (100m, 100m),
            (100m, 100m),
            (100m, 100m));

        var result = Evaluate(sessions);

        Assert.All(result.SupportZones, item => Assert.Equal(StructuralRole.Support, item.Role));
        Assert.All(result.ResistanceZones, item => Assert.Equal(StructuralRole.Resistance, item.Role));
        Assert.All(result.SupportZones, zone =>
            Assert.All(zone.RepresentativeObservations, item =>
                Assert.Equal(StructuralRole.Support, item.Role)));
        Assert.All(result.ResistanceZones, zone =>
            Assert.All(zone.RepresentativeObservations, item =>
                Assert.Equal(StructuralRole.Resistance, item.Role)));
    }

    [Fact]
    public void Two_pivots_in_one_session_do_not_create_recurring_evidence()
    {
        var pivots = new[]
        {
            Pivot("s1", StructuralRole.Support, 95m, 1),
            Pivot("s1", StructuralRole.Support, 95.01m, 2)
        };
        var sessions = CompleteSessions(
            (110m, 90m, pivots),
            (111m, 91m),
            (112m, 92m));

        var result = Evaluate(sessions);

        Assert.DoesNotContain(
            result.SupportZones.SelectMany(item => item.RepresentativeObservations),
            item => item.Kind == StructuralObservationKind.RecurringConfirmedSwingLow);
    }

    [Fact]
    public void Confirmed_pivots_in_distinct_complete_sessions_qualify_as_recurring()
    {
        var sessions = CompleteSessions(
            (110m, 90m, [Pivot("s1", StructuralRole.Support, 95m, 1)]),
            (111m, 91m, [Pivot("s2", StructuralRole.Support, 95.05m, 2)]),
            (112m, 92m));

        var result = Evaluate(sessions);
        var zone = Assert.Single(
            result.SupportZones,
            item => item.LowerBound <= 95m && item.UpperBound >= 95.05m);

        Assert.Equal(2, zone.DistinctSessionCount);
        Assert.Equal(2, zone.RepresentativeObservations.Count);
        Assert.Equal(
            StructuralEligibilityReason.RecurringConfirmedSwingAcrossDistinctSessions,
            Assert.Single(zone.EligibilityReasons));
        var observation = zone.RepresentativeObservations[0];
        Assert.NotNull(observation.SourceSwingId);
        Assert.NotNull(observation.OriginEventIdentity);
        Assert.NotNull(observation.ConfirmationEventIdentity);
        Assert.NotNull(observation.CandidateEventTimeUtc);
        Assert.NotNull(observation.ConfirmedAtEventTimeUtc);
    }

    [Fact]
    public void Pivots_across_three_complete_sessions_form_one_bounded_zone()
    {
        var sessions = CompleteSessions(
            (110m, 90m, [Pivot("s1", StructuralRole.Support, 95m, 1)]),
            (111m, 91m, [Pivot("s2", StructuralRole.Support, 95.03m, 2)]),
            (112m, 92m, [Pivot("s3", StructuralRole.Support, 95.06m, 3)]));

        var result = Evaluate(sessions);
        var zone = Assert.Single(
            result.SupportZones,
            item => item.RepresentativeObservations.Any(observation =>
                observation.Kind == StructuralObservationKind.RecurringConfirmedSwingLow));

        Assert.Equal(3, zone.DistinctSessionCount);
        Assert.Equal(95m, zone.LowerBound);
        Assert.Equal(95.06m, zone.UpperBound);
    }

    [Fact]
    public void Incomplete_session_cannot_supply_recurring_pivots()
    {
        var sessions = CompleteSessions(
            (110m, 90m, [Pivot("s1", StructuralRole.Support, 95m, 1)]),
            (111m, 91m, [Pivot("s2", StructuralRole.Support, 95.05m, 2)]),
            (112m, 92m));
        sessions[1] = sessions[1] with
        {
            Completeness = StructuralSessionCompleteness.Partial,
            DeclaresCompleteCoverage = false
        };

        var result = Evaluate(sessions);

        Assert.Equal(StructuralAssessmentCompleteness.Partial, result.Completeness.Status);
        Assert.Empty(result.SupportZones);
        Assert.Equal(MultiSessionStructuralTrendState.Insufficient, result.Trend.State);
    }

    [Fact]
    public void Insufficient_required_session_downgrades_the_assessment()
    {
        var sessions = CompleteSessions(
            (110m, 90m),
            (111m, 91m),
            (112m, 92m));
        sessions[1] = sessions[1] with
        {
            Completeness = StructuralSessionCompleteness.Insufficient
        };

        var result = Evaluate(sessions);

        Assert.Equal(StructuralAssessmentCompleteness.Insufficient, result.Completeness.Status);
        Assert.Empty(result.SupportZones);
        Assert.Equal(MultiSessionStructuralTrendState.Insufficient, result.Trend.State);
    }

    [Fact]
    public void Complete_session_extrema_are_eligible_without_recurrence()
    {
        var sessions = CompleteSessions(
            (110m, 90m),
            (120m, 80m),
            (130m, 70m));

        var result = Evaluate(sessions);
        var extrema = result.SupportZones
            .Concat(result.ResistanceZones)
            .SelectMany(item => item.RepresentativeObservations)
            .ToArray();

        Assert.Equal(3, extrema.Count(item => item.Kind == StructuralObservationKind.CompleteSessionHigh));
        Assert.Equal(3, extrema.Count(item => item.Kind == StructuralObservationKind.CompleteSessionLow));
        Assert.All(
            extrema,
            item => Assert.Equal(
                StructuralEligibilityReason.CompleteSessionExtreme,
                item.EligibilityReason));
        Assert.DoesNotContain(extrema, item =>
            item.Kind == StructuralObservationKind.RecurringConfirmedSwingHigh
            || item.Kind == StructuralObservationKind.RecurringConfirmedSwingLow);
    }

    [Fact]
    public void Session_extreme_is_known_only_after_complete_session_evidence()
    {
        var sessions = CompleteSessions(
            (110m, 90m),
            (120m, 80m),
            (130m, 70m));
        var result = Evaluate(sessions);

        var observations = result.SupportZones
            .Concat(result.ResistanceZones)
            .SelectMany(item => item.RepresentativeObservations)
            .ToArray();
        Assert.All(observations, item =>
            Assert.True(item.EvidenceKnownAtUtc >= FindSession(sessions, item.SourceSessionId).EndUtc));
        Assert.Equal(sessions[^1].EndUtc, result.EvidenceKnownAtUtc);
    }

    [Fact]
    public void Backdated_complete_session_evidence_is_invalid()
    {
        var sessions = CompleteSessions(
            (110m, 90m),
            (120m, 80m),
            (130m, 70m));
        sessions[2] = sessions[2] with
        {
            EvidenceKnownAtUtc = sessions[2].EndUtc.AddTicks(-1)
        };

        var result = Evaluate(sessions);

        Assert.Equal(StructuralAssessmentCompleteness.Invalid, result.Completeness.Status);
        Assert.Equal(MultiSessionStructuralTrendState.Insufficient, result.Trend.State);
    }

    [Fact]
    public void Unconfirmed_pivot_is_unavailable()
    {
        var unconfirmed = Pivot("s1", StructuralRole.Support, 95m, 1) with
        {
            ConfirmedAtEventTimeUtc = null,
            ConfirmationEventIdentity = null
        };
        var sessions = CompleteSessions(
            (110m, 90m, [unconfirmed]),
            (111m, 91m, [Pivot("s2", StructuralRole.Support, 95.05m, 2)]),
            (112m, 92m));

        var result = Evaluate(sessions);

        Assert.DoesNotContain(
            result.SupportZones.SelectMany(item => item.RepresentativeObservations),
            item => item.EligibilityReason
                == StructuralEligibilityReason.RecurringConfirmedSwingAcrossDistinctSessions);
    }

    [Fact]
    public void Pivot_from_different_symbol_or_session_cannot_confirm_zone()
    {
        var foreign = Pivot("s1", StructuralRole.Support, 95m, 1) with { Symbol = "OTHER" };
        var sessions = CompleteSessions(
            (110m, 90m, [foreign]),
            (111m, 91m, [Pivot("s2", StructuralRole.Support, 95.05m, 2)]),
            (112m, 92m));

        var result = Evaluate(sessions);

        Assert.DoesNotContain(
            result.SupportZones.SelectMany(item => item.RepresentativeObservations),
            item => item.OriginEventIdentity == foreign.OriginEventIdentity);
    }

    [Fact]
    public void Zone_first_known_time_waits_for_newest_required_source()
    {
        var sessions = CompleteSessions(
            (110m, 90m, [Pivot("s1", StructuralRole.Support, 95m, 1)]),
            (111m, 91m, [Pivot("s2", StructuralRole.Support, 95.05m, 2)]),
            (112m, 92m));

        var result = Evaluate(sessions);
        var recurringZone = Assert.Single(
            result.SupportZones,
            item => item.RepresentativeObservations.Any(observation =>
                observation.EligibilityReason
                    == StructuralEligibilityReason.RecurringConfirmedSwingAcrossDistinctSessions));

        Assert.Equal(
            sessions[1].EndUtc,
            recurringZone.EvidenceKnownAtUtc > sessions[1].EndUtc
                ? recurringZone.EvidenceKnownAtUtc
                : sessions[1].EndUtc);
        Assert.Equal(
            recurringZone.RepresentativeObservations.Max(item => item.ConfirmedAtEventTimeUtc),
            recurringZone.EvidenceKnownAtUtc);
    }

    [Fact]
    public void Bullish_trend_requires_higher_high_and_higher_low_in_both_intervals()
    {
        var result = Evaluate(CompleteSessions(
            (100m, 90m),
            (101m, 91m),
            (102m, 92m)));

        Assert.Equal(MultiSessionStructuralTrendState.Bullish, result.Trend.State);
        Assert.All(result.Trend.Intervals, item =>
            Assert.Equal(StructuralIntervalTrend.Bullish, item.State));
    }

    [Fact]
    public void Bearish_trend_requires_lower_high_and_lower_low_in_both_intervals()
    {
        var result = Evaluate(CompleteSessions(
            (102m, 92m),
            (101m, 91m),
            (100m, 90m)));

        Assert.Equal(MultiSessionStructuralTrendState.Bearish, result.Trend.State);
        Assert.All(result.Trend.Intervals, item =>
            Assert.Equal(StructuralIntervalTrend.Bearish, item.State));
    }

    [Fact]
    public void Range_requires_equal_high_and_low_within_tolerance_in_both_intervals()
    {
        var result = Evaluate(
            CompleteSessions(
                (100m, 90m),
                (100.1m, 90.09m),
                (100.2m, 90.18m)),
            Configuration(equalityTolerancePercent: 0.10m));

        Assert.Equal(MultiSessionStructuralTrendState.Range, result.Trend.State);
        Assert.All(result.Trend.Intervals, item =>
            Assert.Equal(StructuralIntervalTrend.Range, item.State));
    }

    [Theory]
    [InlineData("equal-high-higher-low", 100, 100, 90, 91)]
    [InlineData("higher-high-equal-low", 101, 100, 90, 90)]
    [InlineData("lower-high-equal-low", 99, 100, 90, 90)]
    [InlineData("equal-high-lower-low", 100, 100, 89, 90)]
    [InlineData("high-up-low-down", 101, 100, 89, 90)]
    [InlineData("high-down-low-up", 99, 100, 91, 90)]
    public void Mixed_high_low_movement_is_conflicting_not_directional(
        string _,
        double secondHigh,
        double firstHigh,
        double secondLow,
        double firstLow)
    {
        var sessions = CompleteSessions(
            ((decimal)firstHigh, (decimal)firstLow),
            ((decimal)secondHigh, (decimal)secondLow),
            ((decimal)secondHigh, (decimal)secondLow));

        var result = Evaluate(sessions);

        Assert.Equal(MultiSessionStructuralTrendState.Conflicting, result.Trend.State);
        Assert.Equal(StructuralIntervalTrend.Conflicting, result.Trend.Intervals[0].State);
    }

    [Fact]
    public void Direction_reversal_between_intervals_is_transitioning()
    {
        var result = Evaluate(CompleteSessions(
            (100m, 90m),
            (101m, 91m),
            (100m, 90m)));

        Assert.Equal(MultiSessionStructuralTrendState.Transitioning, result.Trend.State);
        Assert.Equal(StructuralIntervalTrend.Bullish, result.Trend.Intervals[0].State);
        Assert.Equal(StructuralIntervalTrend.Bearish, result.Trend.Intervals[1].State);
    }

    [Fact]
    public void Direction_loss_to_range_is_transitioning()
    {
        var result = Evaluate(CompleteSessions(
            (100m, 90m),
            (101m, 91m),
            (101m, 91m)));

        Assert.Equal(MultiSessionStructuralTrendState.Transitioning, result.Trend.State);
    }

    [Fact]
    public void Trend_equality_boundary_is_inclusive_and_just_outside_is_directional()
    {
        var boundary = Evaluate(CompleteSessions(
            (100m, 90m),
            (100.1m, 90.09m),
            (100.2001m, 90.18009m)));
        var outside = Evaluate(CompleteSessions(
            (100m, 90m),
            (100.1000001m, 90.0900001m),
            (100.2001003m, 90.1800903m)));

        Assert.Equal(MultiSessionStructuralTrendState.Range, boundary.Trend.State);
        Assert.Equal(MultiSessionStructuralTrendState.Bullish, outside.Trend.State);
    }

    [Fact]
    public void Fewer_than_three_sessions_is_insufficient()
    {
        var sessions = CompleteSessions(
            (100m, 90m),
            (101m, 91m),
            (102m, 92m));

        var result = Evaluate(sessions.Take(2).ToArray());

        Assert.Equal(StructuralAssessmentCompleteness.Insufficient, result.Completeness.Status);
        Assert.Equal(MultiSessionStructuralTrendState.Insufficient, result.Trend.State);
    }

    [Fact]
    public void Missing_immediate_prior_session_cannot_be_skipped()
    {
        var sessions = CompleteSessions(
            (100m, 90m),
            (101m, 91m),
            (102m, 92m));

        var result = Evaluate(
            sessions,
            input: new StructuralAssessmentInput(
                "TEST",
                "current",
                "different-prior",
                sessions[^1].EndUtc.AddMinutes(1),
                sessions));

        Assert.Equal(StructuralAssessmentCompleteness.Insufficient, result.Completeness.Status);
        Assert.False(result.Completeness.IsContiguous);
        Assert.Equal(MultiSessionStructuralTrendState.Insufficient, result.Trend.State);
    }

    [Fact]
    public void Noncontiguous_middle_session_is_insufficient()
    {
        var sessions = CompleteSessions(
            (100m, 90m),
            (101m, 91m),
            (102m, 92m));
        sessions[2] = sessions[2] with { PreviousSessionId = "s1" };

        var result = Evaluate(sessions);

        Assert.False(result.Completeness.IsContiguous);
        Assert.Equal(StructuralAssessmentCompleteness.Insufficient, result.Completeness.Status);
        Assert.Empty(result.Trend.Intervals);
    }

    [Fact]
    public void Incompatible_provenance_is_invalid()
    {
        var sessions = CompleteSessions(
            (100m, 90m),
            (101m, 91m),
            (102m, 92m));
        sessions[1] = sessions[1] with { HistoricalFeed = "other-feed" };

        var result = Evaluate(sessions);

        Assert.False(result.Completeness.HasUniformProvenance);
        Assert.Equal(StructuralAssessmentCompleteness.Invalid, result.Completeness.Status);
    }

    [Fact]
    public void Future_complete_session_is_excluded_from_earlier_assessment()
    {
        var sessions = CompleteSessions(
            (100m, 90m),
            (101m, 91m),
            (102m, 92m));
        var asOf = sessions[^1].StartUtc;

        var result = Evaluate(
            sessions,
            input: Input(sessions, asOf));

        Assert.Equal(StructuralAssessmentCompleteness.Insufficient, result.Completeness.Status);
        Assert.Empty(result.SupportZones);
        Assert.Empty(result.ResistanceZones);
        Assert.Null(result.EvidenceKnownAtUtc);
    }

    [Fact]
    public void Later_unconfirmed_data_does_not_change_an_earlier_result()
    {
        var sessions = CompleteSessions(
            (100m, 90m),
            (101m, 91m),
            (102m, 92m));
        var earlier = Evaluate(sessions);
        var withFutureUnconfirmed = CompleteSessions(
            (100m, 90m, [Pivot("s1", StructuralRole.Support, 95m, 1) with
            {
                ConfirmedAtEventTimeUtc = null,
                ConfirmationEventIdentity = null
            }]),
            (101m, 91m),
            (102m, 92m));

        var laterReplay = Evaluate(sessions);

        Assert.Equal(earlier.Id, laterReplay.Id);
        Assert.Equivalent(earlier.Trend, laterReplay.Trend);
        Assert.Equal(earlier.SupportZones.Count, Evaluate(withFutureUnconfirmed).SupportZones.Count);
    }

    [Fact]
    public void Identical_replay_is_deterministic_and_retrieval_or_wall_clock_is_absent()
    {
        var sessions = CompleteSessions(
            (100m, 90m),
            (101m, 91m),
            (102m, 92m));

        var first = Evaluate(sessions);
        var replay = Evaluate(sessions);

        Assert.Equal(first.Id, replay.Id);
        Assert.Equivalent(first, replay);
    }

    [Fact]
    public void Equivalent_reordered_source_input_and_duplicate_pivots_are_idempotent()
    {
        var pivot = Pivot("s1", StructuralRole.Support, 95m, 1);
        var sessions = CompleteSessions(
            (110m, 90m, [pivot, Pivot("s1", StructuralRole.Support, 95.01m, 2)]),
            (111m, 91m, [Pivot("s2", StructuralRole.Support, 95.02m, 3)]),
            (112m, 92m));
        var reordered = new[]
        {
            sessions[2],
            sessions[0] with { ConfirmablePivots = [pivot, pivot, .. sessions[0].ConfirmablePivots.Skip(1)] },
            sessions[1]
        };

        var first = Evaluate(sessions);
        var second = Evaluate(
            reordered,
            input: new StructuralAssessmentInput(
                "TEST",
                "current",
                "s3",
                sessions[^1].EndUtc.AddMinutes(1),
                reordered));

        Assert.Equal(
            first.SupportZones.Select(item => item.Id),
            second.SupportZones.Select(item => item.Id));
        Assert.Equal(
            first.ResistanceZones.Select(item => item.Id),
            second.ResistanceZones.Select(item => item.Id));
        Assert.Equivalent(first.Trend, second.Trend);
        Assert.Equal(first.Id, second.Id);
        Assert.Equivalent(first, second);
    }

    [Fact]
    public void Material_configuration_and_zone_composition_change_identity()
    {
        var sessions = CompleteSessions(
            (110m, 90m, [Pivot("s1", StructuralRole.Support, 95m, 1)]),
            (111m, 91m, [Pivot("s2", StructuralRole.Support, 95.05m, 2)]),
            (112m, 92m));
        var first = Evaluate(sessions);
        var changedThreshold = Evaluate(
            sessions,
            Configuration(zoneMergeDistancePercent: 0.11m, version: "scanner-config-v21"));
        var changedComposition = Evaluate(CompleteSessions(
            (110m, 90m, [Pivot("s1", StructuralRole.Support, 95.01m, 1)]),
            (111m, 91m, [Pivot("s2", StructuralRole.Support, 95.05m, 2)]),
            (112m, 92m)));

        Assert.NotEqual(first.Id, changedThreshold.Id);
        var firstRecurringZone = Assert.Single(
            first.SupportZones,
            item => item.RepresentativeObservations.Any(observation =>
                observation.EligibilityReason
                    == StructuralEligibilityReason.RecurringConfirmedSwingAcrossDistinctSessions));
        var changedRecurringZone = Assert.Single(
            changedComposition.SupportZones,
            item => item.RepresentativeObservations.Any(observation =>
                observation.EligibilityReason
                    == StructuralEligibilityReason.RecurringConfirmedSwingAcrossDistinctSessions));
        Assert.NotEqual(firstRecurringZone.Id, changedRecurringZone.Id);
    }

    [Fact]
    public void Projection_is_bounded_deterministically_and_reports_omitted_zones()
    {
        var sessions = CompleteSessions(
            (500m, 400m, RecurringPivots("s1", 1)),
            (501m, 401m, RecurringPivots("s2", 20)),
            (502m, 402m, RecurringPivots("s3", 40)));
        var configuration = Configuration(maximumProjectedSupportZones: 2);

        var result = Evaluate(sessions, configuration);
        var pivots = result.SupportZones
            .Where(item => item.RepresentativeObservations.Any(observation =>
                observation.EligibilityReason
                    == StructuralEligibilityReason.RecurringConfirmedSwingAcrossDistinctSessions))
            .ToArray();

        Assert.True(result.OmittedSupportZoneCount > 0);
        Assert.True(result.SupportZones.Count <= 2);
        Assert.Empty(pivots);
        Assert.Equal(
            result.SupportZones.Select(item => item.Id),
            Evaluate(sessions, configuration).SupportZones.Select(item => item.Id));
        Assert.DoesNotContain(result.SupportZones, item => item.UpperBound < 95m);
    }

    [Fact]
    public void Presentation_projection_limits_do_not_change_analytical_identity()
    {
        var sessions = CompleteSessions(
            (500m, 400m, RecurringPivots("s1", 1)),
            (501m, 401m, RecurringPivots("s2", 20)),
            (502m, 402m, RecurringPivots("s3", 40)));
        var smallerProjection = Evaluate(
            sessions,
            Configuration(maximumProjectedSupportZones: 1));
        var largerProjection = Evaluate(
            sessions,
            Configuration(maximumProjectedSupportZones: 3));

        Assert.Equal(smallerProjection.Id, largerProjection.Id);
        Assert.NotEqual(smallerProjection.SupportZones.Count, largerProjection.SupportZones.Count);
        Assert.True(smallerProjection.OmittedSupportZoneCount >
                    largerProjection.OmittedSupportZoneCount);
    }

    [Fact]
    public void Representative_limit_bounds_each_session_and_preserves_omission_count()
    {
        var sessions = CompleteSessions(
            (500m, 400m, [
                Pivot("s1", StructuralRole.Support, 95m, 1),
                Pivot("s1", StructuralRole.Support, 95.001m, 2),
                Pivot("s1", StructuralRole.Support, 95.002m, 3)
            ]),
            (501m, 401m, [Pivot("s2", StructuralRole.Support, 95.003m, 4)]),
            (502m, 402m));
        var configuration = Configuration(
            maximumProjectedSupportZones: 10,
            maximumRepresentativesPerSessionPerZone: 1);

        var result = Evaluate(sessions, configuration);
        var zone = Assert.Single(
            result.SupportZones,
            item => item.RepresentativeObservations.Any(observation =>
                observation.EligibilityReason
                    == StructuralEligibilityReason.RecurringConfirmedSwingAcrossDistinctSessions));

        Assert.Equal(
            1,
            zone.RepresentativeObservations.Count(item => item.SourceSessionId == "s1"));
        Assert.Equal(2, zone.OmittedObservationCount);
        Assert.Equal(4, zone.TotalObservationCount);
    }

    [Fact]
    public void Shared_configuration_advances_version_and_validates_structural_settings()
    {
        Assert.Equal("scanner-config-v20", new ScannerConfiguration().Version);
        Assert.Equal(3, new ScannerConfiguration().MultiSessionStructure.HistoricalSessionCount);
        Assert.Equal(2, new ScannerConfiguration().MultiSessionStructure.MinimumDistinctSessionsForRecurringZone);
        Assert.Equal(0.10m, new ScannerConfiguration().MultiSessionStructure.ZoneMergeDistancePercent);
        Assert.Equal(0.10m, new ScannerConfiguration().MultiSessionStructure.StructuralEqualityTolerancePercent);

        Assert.Throws<InvalidOperationException>(() =>
            (new ScannerConfiguration() with
            {
                MultiSessionStructure = new MultiSessionStructureConfiguration
                {
                    ZoneMergeDistancePercent = 0m
                }
            }).Validate());
        Assert.Throws<InvalidOperationException>(() =>
            (new ScannerConfiguration() with
            {
                MultiSessionStructure = new MultiSessionStructureConfiguration
                {
                    HistoricalSessionCount = 3,
                    MinimumDistinctSessionsForRecurringZone = 1
                }
            }).Validate());
        Assert.Throws<InvalidOperationException>(() =>
            (new ScannerConfiguration() with
            {
                MultiSessionStructure = new MultiSessionStructureConfiguration
                {
                    HistoricalSessionCount = 4
                }
            }).Validate());
    }

    private MultiSessionStructuralAssessment Evaluate(
        IReadOnlyList<StructuralSessionSummary> sessions,
        ScannerConfiguration? configuration = null,
        StructuralAssessmentInput? input = null) =>
        _evaluator.Evaluate(
            input ?? Input(sessions),
            configuration ?? Configuration());

    private static StructuralAssessmentInput Input(
        IReadOnlyList<StructuralSessionSummary> sessions,
        DateTimeOffset? asOfUtc = null) =>
        new(
            "TEST",
            "current",
            sessions.Count == 0 ? "missing" : sessions[^1].SessionId,
            asOfUtc ?? (sessions.Count == 0
                ? FirstTradingDate.ToDateTime(new TimeOnly(22, 0), DateTimeKind.Utc)
                : sessions[^1].EndUtc.AddMinutes(1)),
            sessions);

    private static ScannerConfiguration Configuration(
        decimal zoneMergeDistancePercent = 0.10m,
        decimal equalityTolerancePercent = 0.10m,
        int maximumProjectedSupportZones = 3,
        int maximumProjectedResistanceZones = 3,
        int maximumRepresentativesPerSessionPerZone = 3,
        string version = "scanner-config-v20") =>
        new()
        {
            Version = version,
            MultiSessionStructure = new MultiSessionStructureConfiguration
            {
                HistoricalSessionCount = 3,
                MinimumDistinctSessionsForRecurringZone = 2,
                ZoneMergeDistancePercent = zoneMergeDistancePercent,
                StructuralEqualityTolerancePercent = equalityTolerancePercent,
                MaximumProjectedSupportZones = maximumProjectedSupportZones,
                MaximumProjectedResistanceZones = maximumProjectedResistanceZones,
                MaximumRepresentativesPerSessionPerZone = maximumRepresentativesPerSessionPerZone
            }
        };

    private static StructuralSessionSummary[] CompleteSessions(
        (decimal High, decimal Low) first,
        (decimal High, decimal Low) second,
        (decimal High, decimal Low) third) =>
        [
            Session("s1", null, 0, first.High, first.Low),
            Session("s2", "s1", 1, second.High, second.Low),
            Session("s3", "s2", 2, third.High, third.Low)
        ];

    private static StructuralSessionSummary[] CompleteSessions(
        (decimal High, decimal Low, IReadOnlyList<StructuralPivotCandidate> Pivots) first,
        (decimal High, decimal Low) second,
        (decimal High, decimal Low) third) =>
        [
            Session("s1", null, 0, first.High, first.Low, first.Pivots),
            Session("s2", "s1", 1, second.High, second.Low),
            Session("s3", "s2", 2, third.High, third.Low)
        ];

    private static StructuralSessionSummary[] CompleteSessions(
        (decimal High, decimal Low, IReadOnlyList<StructuralPivotCandidate> Pivots) first,
        (decimal High, decimal Low, IReadOnlyList<StructuralPivotCandidate> Pivots) second,
        (decimal High, decimal Low) third) =>
        [
            Session("s1", null, 0, first.High, first.Low, first.Pivots),
            Session("s2", "s1", 1, second.High, second.Low, second.Pivots),
            Session("s3", "s2", 2, third.High, third.Low)
        ];

    private static StructuralSessionSummary[] CompleteSessions(
        (decimal High, decimal Low, IReadOnlyList<StructuralPivotCandidate> Pivots) first,
        (decimal High, decimal Low, IReadOnlyList<StructuralPivotCandidate> Pivots) second,
        (decimal High, decimal Low, IReadOnlyList<StructuralPivotCandidate> Pivots) third) =>
        [
            Session("s1", null, 0, first.High, first.Low, first.Pivots),
            Session("s2", "s1", 1, second.High, second.Low, second.Pivots),
            Session("s3", "s2", 2, third.High, third.Low, third.Pivots)
        ];

    private static StructuralSessionSummary Session(
        string id,
        string? previous,
        int day,
        decimal high,
        decimal low,
        IReadOnlyList<StructuralPivotCandidate>? pivots = null)
    {
        var date = FirstTradingDate.AddDays(day);
        var start = new DateTimeOffset(date, new TimeOnly(14, 30), TimeSpan.Zero);
        var end = new DateTimeOffset(date, new TimeOnly(21, 0), TimeSpan.Zero);
        return new StructuralSessionSummary(
            "TEST",
            id,
            previous,
            start,
            end,
            StructuralSessionCompleteness.Available,
            true,
            false,
            high,
            low,
            $"{id}-high-event",
            $"{id}-low-event",
            end.AddHours(-2),
            end.AddHours(-1),
            end,
            "UnitTest",
            "Recorded",
            "Synthetic",
            pivots ?? []);
    }

    private static StructuralPivotCandidate Pivot(
        string sessionId,
        StructuralRole role,
        decimal price,
        int seed)
    {
        var sessionIndex = int.Parse(sessionId[^1..]) - 1;
        var sessionDate = FirstTradingDate.AddDays(sessionIndex);
        var candidateTime = new DateTimeOffset(
            sessionDate,
            new TimeOnly(15, seed % 24, seed % 60),
            TimeSpan.Zero);
        return new StructuralPivotCandidate(
            Guid.Parse($"00000000-0000-0000-0000-{seed:000000000000}"),
            "TEST",
            sessionId,
            role,
            price,
            candidateTime,
            candidateTime.AddMinutes(1),
            $"origin-{seed}",
            $"confirmation-{seed}",
            "features-v16",
            "session-swing-fractal-v1");
    }

    private static IReadOnlyList<StructuralPivotCandidate> RecurringPivots(
        string sessionId,
        int startingSeed) =>
    [
        Pivot(sessionId, StructuralRole.Support, 100m, startingSeed),
        Pivot(sessionId, StructuralRole.Support, 101m, startingSeed + 1),
        Pivot(sessionId, StructuralRole.Support, 102m, startingSeed + 2),
        Pivot(sessionId, StructuralRole.Support, 103m, startingSeed + 3)
    ];

    private static StructuralSessionSummary FindSession(
        IEnumerable<StructuralSessionSummary> sessions,
        string sessionId) =>
        sessions.Single(item => item.SessionId == sessionId);
}
