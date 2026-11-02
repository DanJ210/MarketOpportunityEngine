using MarketOpportunity.Analytics;
using MarketOpportunity.Application;
using MarketOpportunity.Domain;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarketOpportunity.UnitTests;

public sealed class PreTradeReadinessTests
{
    private static readonly DateTimeOffset AsOfUtc = new(2026, 10, 24, 14, 30, 0, TimeSpan.Zero);
    private static readonly Guid OpportunityId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid LongPlanId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ShortPlanId = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SwingId = new("44444444-4444-4444-4444-444444444444");
    private const string Symbol = "MOE";
    private const string Session = "readiness-session";

    // ----- Selection -----

    [Fact]
    public void Selects_one_eligible_plan_and_pins_it_deterministically()
    {
        var tracker = new PreTradeReadinessTracker();
        var plan = LongPlan();
        var evaluation = tracker.Evaluate(
            Features(102m), Candidate(), plan, Active(), null, Config(), AsOfUtc);

        Assert.NotNull(evaluation.Projection);
        var tracked = evaluation.Projection.Tracked;
        Assert.Equal(plan.Id, tracked.PlanId);
        Assert.Equal(OpportunityId, tracked.OpportunityId);
        Assert.Equal(Symbol, tracked.Symbol);
        Assert.Equal(Session, tracked.SessionId);
        Assert.Equal(TradeDirection.Long, tracked.Direction);
        Assert.Equal(AsOfUtc, tracked.SelectedAtUtc);
        Assert.Equal(plan.AsOfUtc, tracked.PlanAsOfUtc);
        Assert.Equal(PreTradeReadinessEvaluator.SelectionDefinitionVersion, tracked.SelectionDefinitionVersion);
        Assert.Equal(TrackedOpportunityStatus.Active, tracked.Status);
        Assert.Equal("EligiblePlanSelected", tracked.SelectionReasonCode);
        Assert.False(tracked.HasEstablishedEntryReady);
        Assert.Equal(tracked.Id, PreTradeReadinessEvaluator
            .CreateTrackedOpportunity(Features(102m), Candidate(), plan).Id);
    }

    [Theory]
    [InlineData(TradePlanStatus.PlanForming)]
    [InlineData(TradePlanStatus.InsufficientStructure)]
    [InlineData(TradePlanStatus.Rejected)]
    public void Non_defined_plan_is_not_selected(TradePlanStatus status)
    {
        var tracker = new PreTradeReadinessTracker();
        var evaluation = tracker.Evaluate(
            Features(102m),
            Candidate(),
            LongPlan() with { Status = status },
            Active(),
            null,
            Config(),
            AsOfUtc);

        Assert.Null(evaluation.Projection);
        Assert.Null(evaluation.PersistTracked);
        Assert.Null(evaluation.PersistAssessment);
    }

    [Fact]
    public void Selection_requires_direction_agreement_active_publication_valid_data_and_positive_risk_reward()
    {
        Assert.Null(new PreTradeReadinessTracker().Evaluate(
            Features(102m), Candidate(bias: SetupBias.Short, classification: OpportunityClassification.ProbableShort),
            LongPlan(), Active(), null, Config(), AsOfUtc).Projection);
        Assert.Null(new PreTradeReadinessTracker().Evaluate(
            Features(102m), Candidate(), LongPlan(),
            new PublicationDecision(OpportunityLifecycleState.Suspended, "MarketDataStale", "Stale.", AsOfUtc),
            null, Config(), AsOfUtc).Projection);
        Assert.Null(new PreTradeReadinessTracker().Evaluate(
            Features(102m, hasRequiredData: false), Candidate(), LongPlan(),
            Active(), null, Config(), AsOfUtc).Projection);
        Assert.Null(new PreTradeReadinessTracker().Evaluate(
            Features(102m), Candidate(), LongPlan() with { RiskPerShare = 0m },
            Active(), null, Config(), AsOfUtc).Projection);
        Assert.Null(new PreTradeReadinessTracker().Evaluate(
            Features(102m), Candidate(), LongPlan() with { RewardPerShareToTarget1 = 0m },
            Active(), null, Config(), AsOfUtc).Projection);
    }

    [Fact]
    public void Newer_defined_plans_do_not_replace_the_tracked_candidate()
    {
        var tracker = new PreTradeReadinessTracker();
        var pinned = LongPlan();
        tracker.Evaluate(Features(102m), Candidate(), pinned, Active(), null, Config(), AsOfUtc);

        var laterAsOf = AsOfUtc.AddSeconds(30);
        var newerPlan = LongPlan() with
        {
            Id = new Guid("55555555-5555-5555-5555-555555555555"),
            AsOfUtc = laterAsOf,
            EntryZone = new PriceZone(101m, 103m)
        };
        var evaluation = tracker.Evaluate(
            Features(102m, asOfUtc: laterAsOf), Candidate(), newerPlan, Active(), null, Config(), laterAsOf);

        Assert.NotNull(evaluation.Projection);
        Assert.Equal(pinned.Id, evaluation.Projection.Tracked.PlanId);
        Assert.Equal(pinned, evaluation.Projection.Plan);
        // The pinned geometry, not the newer moving zone, decides readiness: 102 is above (98, 100).
        Assert.Equal(PreTradeReadinessState.SetupForming, evaluation.Projection.Assessment.State);
    }

    [Fact]
    public void Terminal_candidate_is_not_replaced_in_place_and_allows_subsequent_selection()
    {
        var tracker = new PreTradeReadinessTracker();
        var first = LongPlan();
        tracker.Evaluate(Features(102m), Candidate(), first, Active(), null, Config(), AsOfUtc);

        var crashAsOf = AsOfUtc.AddSeconds(10);
        var terminal = tracker.Evaluate(
            Features(95m, asOfUtc: crashAsOf), Candidate(), first, Active(), null, Config(), crashAsOf);
        Assert.Equal(PreTradeReadinessState.NoLongerActionable, terminal.Projection!.Assessment.State);
        Assert.Equal(PreTradeReadinessReason.InvalidationCrossed, terminal.Projection.Assessment.Reason);
        Assert.Equal(TrackedOpportunityStatus.Invalidated, terminal.PersistTracked!.Status);
        Assert.Equal(crashAsOf, terminal.PersistTracked.TerminalAtUtc);
        Assert.Equal("InvalidationCrossed", terminal.PersistTracked.TerminalReasonCode);

        var laterAsOf = AsOfUtc.AddSeconds(20);
        var second = LongPlan() with
        {
            Id = new Guid("66666666-6666-6666-6666-666666666666"),
            AsOfUtc = laterAsOf
        };
        var reselected = tracker.Evaluate(
            Features(102m, asOfUtc: laterAsOf), Candidate(), second, Active(), null, Config(), laterAsOf);

        Assert.NotNull(reselected.Projection);
        Assert.Equal(second.Id, reselected.Projection.Tracked.PlanId);
        Assert.NotEqual(terminal.Projection.Tracked.Id, reselected.Projection.Tracked.Id);
    }

    [Fact]
    public void Tracked_candidates_are_isolated_by_symbol_and_session()
    {
        var tracker = new PreTradeReadinessTracker();
        var planA = LongPlan();
        var planB = LongPlan() with { Id = new Guid("77777777-7777-7777-7777-777777777777"), SessionId = "other-session" };
        tracker.Evaluate(Features(102m), Candidate(), planA, Active(), null, Config(), AsOfUtc);
        tracker.Evaluate(
            Features(102m, sessionId: "other-session"),
            Candidate(sessionId: "other-session"),
            planB, Active(), null, Config(), AsOfUtc);

        var crashAsOf = AsOfUtc.AddSeconds(10);
        tracker.Evaluate(Features(95m, asOfUtc: crashAsOf), Candidate(), planA, Active(), null, Config(), crashAsOf);
        var other = tracker.Evaluate(
            Features(102m, sessionId: "other-session", asOfUtc: crashAsOf),
            Candidate(sessionId: "other-session"),
            planB, Active(), null, Config(), crashAsOf);

        Assert.Equal(planB.Id, other.Projection!.Tracked.PlanId);
        Assert.Equal(TrackedOpportunityStatus.Active, other.Projection.Tracked.Status);
    }

    // ----- Long readiness geometry -----

    [Theory]
    [InlineData(95.0, PreTradeReadinessState.NoLongerActionable, PreTradeReadinessReason.InvalidationCrossed)]
    [InlineData(96.0, PreTradeReadinessState.NoLongerActionable, PreTradeReadinessReason.InvalidationCrossed)]
    [InlineData(106.0, PreTradeReadinessState.NoLongerActionable, PreTradeReadinessReason.TargetReachedBeforeEntry)]
    [InlineData(107.0, PreTradeReadinessState.NoLongerActionable, PreTradeReadinessReason.TargetReachedBeforeEntry)]
    [InlineData(97.0, PreTradeReadinessState.NoLongerActionable, PreTradeReadinessReason.ExtendedBeyondEntry)]
    [InlineData(99.0, PreTradeReadinessState.EntryReady, PreTradeReadinessReason.InsideEntryZone)]
    [InlineData(98.0, PreTradeReadinessState.EntryReady, PreTradeReadinessReason.InsideEntryZone)]
    [InlineData(100.0, PreTradeReadinessState.EntryReady, PreTradeReadinessReason.InsideEntryZone)]
    [InlineData(102.0, PreTradeReadinessState.SetupForming, PreTradeReadinessReason.ApproachingEntry)]
    [InlineData(103.0, PreTradeReadinessState.SetupForming, PreTradeReadinessReason.ApproachingEntry)]
    [InlineData(103.01, PreTradeReadinessState.Watching, PreTradeReadinessReason.None)]
    public void Long_readiness_states_follow_the_pinned_geometry(
        decimal price, PreTradeReadinessState expectedState, PreTradeReadinessReason expectedReason)
    {
        var assessment = Assess(LongPlan(), price);

        Assert.Equal(expectedState, assessment.State);
        Assert.Equal(expectedReason, assessment.Reason);
        Assert.Equal(TradeDirection.Long, assessment.Direction);
    }

    // ----- Short readiness geometry -----

    [Theory]
    [InlineData(105.0, PreTradeReadinessState.NoLongerActionable, PreTradeReadinessReason.InvalidationCrossed)]
    [InlineData(104.0, PreTradeReadinessState.NoLongerActionable, PreTradeReadinessReason.InvalidationCrossed)]
    [InlineData(94.0, PreTradeReadinessState.NoLongerActionable, PreTradeReadinessReason.TargetReachedBeforeEntry)]
    [InlineData(93.0, PreTradeReadinessState.NoLongerActionable, PreTradeReadinessReason.TargetReachedBeforeEntry)]
    [InlineData(103.0, PreTradeReadinessState.NoLongerActionable, PreTradeReadinessReason.ExtendedBeyondEntry)]
    [InlineData(101.0, PreTradeReadinessState.EntryReady, PreTradeReadinessReason.InsideEntryZone)]
    [InlineData(100.0, PreTradeReadinessState.EntryReady, PreTradeReadinessReason.InsideEntryZone)]
    [InlineData(102.0, PreTradeReadinessState.EntryReady, PreTradeReadinessReason.InsideEntryZone)]
    [InlineData(98.0, PreTradeReadinessState.SetupForming, PreTradeReadinessReason.ApproachingEntry)]
    [InlineData(97.0, PreTradeReadinessState.SetupForming, PreTradeReadinessReason.ApproachingEntry)]
    [InlineData(96.99, PreTradeReadinessState.Watching, PreTradeReadinessReason.None)]
    public void Short_readiness_states_mirror_the_long_rules(
        decimal price, PreTradeReadinessState expectedState, PreTradeReadinessReason expectedReason)
    {
        var assessment = Assess(
            ShortPlan(),
            price,
            candidate: Candidate(bias: SetupBias.Short, classification: OpportunityClassification.ProbableShort));

        Assert.Equal(expectedState, assessment.State);
        Assert.Equal(expectedReason, assessment.Reason);
        Assert.Equal(TradeDirection.Short, assessment.Direction);
    }

    [Fact]
    public void Established_entry_ready_changes_target_and_beyond_entry_semantics()
    {
        var plan = LongPlan();
        var tracked = Tracked(plan) with { HasEstablishedEntryReady = true };

        var beyondEntry = Assess(plan, 97m, tracked);
        Assert.Equal(PreTradeReadinessState.Watching, beyondEntry.State);
        Assert.Equal(1m, beyondEntry.DistanceToEntry);

        var targetReached = Assess(plan, 107m, tracked);
        Assert.NotEqual(PreTradeReadinessState.NoLongerActionable, targetReached.State);
    }

    // ----- Approach boundary -----

    [Fact]
    public void Approach_distance_is_normalized_by_risk_per_share_with_inclusive_boundary()
    {
        var atBoundary = Assess(LongPlan(), 103m);
        Assert.Equal(PreTradeReadinessState.SetupForming, atBoundary.State);
        Assert.Equal(3m, atBoundary.DistanceToEntry);
        Assert.Equal(1m, atBoundary.DistanceToEntryRiskMultiple);

        var outside = Assess(LongPlan(), 103.3m);
        Assert.Equal(PreTradeReadinessState.Watching, outside.State);
        Assert.Equal(3.3m, outside.DistanceToEntry);
        Assert.Equal(1.1m, outside.DistanceToEntryRiskMultiple);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_approach_distance_configuration_is_rejected(decimal multiple)
    {
        var configuration = Config() with
        {
            PreTrade = new PreTradeReadinessConfiguration { ApproachDistanceRiskMultiple = multiple }
        };

        Assert.Throws<InvalidOperationException>(configuration.Validate);
    }

    [Fact]
    public void Plan_without_positive_risk_is_unavailable_not_miscalculated()
    {
        var plan = LongPlan();
        var tracked = Tracked(plan);
        var broken = plan with { RiskPerShare = 0m };

        var assessment = new PreTradeReadinessEvaluator().Evaluate(
            tracked, broken, Features(102m), Candidate(), Active(), null, Config(), AsOfUtc);

        Assert.Equal(PreTradeReadinessState.Unavailable, assessment.State);
        Assert.Equal(PreTradeReadinessReason.PlanUnavailable, assessment.Reason);
    }

    // ----- Confirmation -----

    [Fact]
    public void Inside_zone_with_misaligned_or_vetoed_opportunity_is_entry_zone_active_not_entry_ready()
    {
        var watch = Assess(LongPlan(), 99m, candidate: Candidate(classification: OpportunityClassification.Watch));
        Assert.Equal(PreTradeReadinessState.EntryZoneActive, watch.State);
        Assert.Equal(PreTradeReadinessReason.ConfirmationNotAligned, watch.Reason);
        Assert.Contains(watch.Evidence, item => item.Code == "ClassificationNotQualifying");

        var vetoed = Assess(
            LongPlan(),
            99m,
            candidate: Candidate(
                classification: OpportunityClassification.AvoidNoTrade,
                vetoes: ["ExcessiveRealizedVolatility"]),
            publication: new PublicationDecision(
                OpportunityLifecycleState.NotQualified, "SetupNotQualified", "Vetoed.", AsOfUtc));
        Assert.Equal(PreTradeReadinessState.EntryZoneActive, vetoed.State);
        Assert.Contains(vetoed.Evidence, item => item.Code == "ActiveAnalyticalVetoes");
        Assert.Contains(vetoed.Evidence, item => item.Code == "PublicationNotActive");

        var mismatched = Assess(
            LongPlan(),
            99m,
            candidate: Candidate(bias: SetupBias.Short, classification: OpportunityClassification.ProbableShort));
        Assert.Equal(PreTradeReadinessState.EntryZoneActive, mismatched.State);
        Assert.Contains(mismatched.Evidence, item => item.Code == "ConfirmationDirectionMismatch");
    }

    [Fact]
    public void Invalid_quote_or_wide_spread_blocks_entry_ready_confirmation()
    {
        var invalidQuote = Assess(LongPlan(), 99m, snapshot: Features(99m, quoteValid: false));
        Assert.Equal(PreTradeReadinessState.EntryZoneActive, invalidQuote.State);
        Assert.Contains(invalidQuote.Evidence, item => item.Code == "InvalidQuote");

        var wideSpread = Assess(LongPlan(), 99m, snapshot: Features(99m, spreadBasisPoints: 26m));
        Assert.Equal(PreTradeReadinessState.EntryZoneActive, wideSpread.State);
        Assert.Contains(wideSpread.Evidence, item => item.Code == "SpreadTooWide");
    }

    [Theory]
    [InlineData(null, 0, false)]
    [InlineData(-1, 0, false)]
    [InlineData(0, -1, false)]
    [InlineData(5, 10, true)]
    [InlineData(5, 11, false)]
    [InlineData(16, 0, false)]
    public void Entry_confirmation_checks_quote_event_time_at_evaluation(
        int? quoteAgeSeconds, int evaluationDelaySeconds, bool entryReady)
    {
        var snapshot = Features(99m) with
        {
            QuoteAge = quoteAgeSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null
        };
        var assessment = Assess(
            LongPlan(), 99m, snapshot: snapshot,
            evaluatedAtUtc: AsOfUtc.AddSeconds(evaluationDelaySeconds));

        Assert.Equal(
            entryReady ? PreTradeReadinessState.EntryReady : PreTradeReadinessState.EntryZoneActive,
            assessment.State);
        Assert.Equal(!entryReady, assessment.Evidence.Any(item => item.Code == "InvalidQuote"));
    }

    [Fact]
    public void Health_only_reevaluation_ages_the_quote_without_rewriting_the_snapshot()
    {
        var configuration = Config();
        var state = new ScannerState(configuration: configuration);
        var plan = LongPlan();
        var snapshot = Features(99m);
        var opportunity = Candidate();
        var evaluation = new PreTradeReadinessTracker().Evaluate(
            snapshot, opportunity, plan, Active(), null, configuration, AsOfUtc);
        var status = new ScannerStatus(MarketDataHealthStatus.Live, "TestProvider", 1, AsOfUtc, null);
        state.SetResults(status,
            [new ScannerAnalysisResult(snapshot, opportunity, Active(), plan, null, evaluation.Projection)],
            AsOfUtc);

        var boundary = AsOfUtc.AddSeconds(configuration.MaximumQuoteAgeSeconds);
        state.SetStatus(status, boundary);
        Assert.Equal(PreTradeReadinessState.EntryReady, Assert.Single(state.Read().Results).Readiness!.Assessment.State);
        state.SetStatus(status, boundary.AddTicks(1));
        var stale = Assert.Single(state.Read().Results);
        Assert.Equal(OpportunityLifecycleState.Active, stale.Publication.State);
        Assert.Equal(PreTradeReadinessState.EntryZoneActive, stale.Readiness!.Assessment.State);
        Assert.Contains(stale.Readiness.Assessment.Evidence, item => item.Code == "InvalidQuote");
        Assert.Same(snapshot, stale.Features);
        Assert.Equal(TimeSpan.Zero, stale.Features.QuoteAge);
    }

    // ----- Suspension and health -----

    [Fact]
    public void Feed_health_and_data_gaps_suspend_without_terminating_the_tracked_candidate()
    {
        var tracker = new PreTradeReadinessTracker();
        var plan = LongPlan();
        tracker.Evaluate(Features(99m), Candidate(), plan, Active(), null, Config(), AsOfUtc);

        var staleAsOf = AsOfUtc.AddSeconds(10);
        var stale = tracker.Evaluate(
            Features(99m, asOfUtc: staleAsOf),
            Candidate(),
            plan,
            new PublicationDecision(OpportunityLifecycleState.Suspended, "MarketDataStale", "Stale.", staleAsOf),
            null,
            Config(),
            staleAsOf);
        Assert.Equal(PreTradeReadinessState.Suspended, stale.Projection!.Assessment.State);
        Assert.Equal(PreTradeReadinessReason.FeedNotLive, stale.Projection.Assessment.Reason);
        Assert.Equal(TrackedOpportunityStatus.Active, stale.Projection.Tracked.Status);
        Assert.Equal(plan.Id, stale.Projection.Tracked.PlanId);

        var gapAsOf = AsOfUtc.AddSeconds(20);
        var gap = tracker.Evaluate(
            Features(99m, asOfUtc: gapAsOf),
            Candidate(),
            plan,
            new PublicationDecision(OpportunityLifecycleState.Suspended, "MarketDataGapUnresolved", "Gap.", gapAsOf),
            null,
            Config(),
            gapAsOf);
        Assert.Equal(PreTradeReadinessReason.DataGapUnresolved, gap.Projection!.Assessment.Reason);

        var recoveredAsOf = AsOfUtc.AddSeconds(30);
        var recovered = tracker.Evaluate(
            Features(99m, asOfUtc: recoveredAsOf), Candidate(), plan, Active(recoveredAsOf), null, Config(), recoveredAsOf);
        Assert.Equal(plan.Id, recovered.Projection!.Tracked.PlanId);
        Assert.Equal(PreTradeReadinessState.EntryReady, recovered.Projection.Assessment.State);
    }

    [Fact]
    public void Incomplete_or_invalid_current_data_suspends_the_assessment()
    {
        var incomplete = Assess(LongPlan(), 99m, snapshot: Features(99m, hasRequiredData: false));
        Assert.Equal(PreTradeReadinessState.Suspended, incomplete.State);
        Assert.Equal(PreTradeReadinessReason.DataIncomplete, incomplete.Reason);

        var invalidPrice = Assess(LongPlan(), 99m, snapshot: Features(99m, priceDataValid: false));
        Assert.Equal(PreTradeReadinessState.Suspended, invalidPrice.State);
        Assert.Equal(PreTradeReadinessReason.InvalidCurrentPrice, invalidPrice.Reason);
        Assert.Null(invalidPrice.CurrentReferencePrice);
    }

    [Fact]
    public void Scanner_state_re_derives_suspension_and_recovery_for_the_same_pinned_plan()
    {
        var configuration = Config();
        var state = new ScannerState(new OpportunityPublicationGate(), null, configuration);
        var tracker = new PreTradeReadinessTracker();
        var plan = LongPlan();
        var snapshot = Features(99m);
        var candidate = Candidate();
        var evaluation = tracker.Evaluate(snapshot, candidate, plan, Active(), null, configuration, AsOfUtc);
        var result = new ScannerAnalysisResult(
            snapshot, candidate, Active(), plan, null, evaluation.Projection);
        var status = new ScannerStatus(MarketDataHealthStatus.Live, "TestProvider", 1, AsOfUtc, null);

        state.SetResults(status, [result], AsOfUtc.AddSeconds(1));
        var live = Assert.Single(state.Read().Results);
        Assert.Equal(PreTradeReadinessState.EntryReady, live.Readiness!.Assessment.State);

        state.SetStatus(status with { FeedStatus = MarketDataHealthStatus.Stale }, AsOfUtc.AddSeconds(5));
        var suspended = Assert.Single(state.Read().Results);
        Assert.Equal(PreTradeReadinessState.Suspended, suspended.Readiness!.Assessment.State);
        Assert.Equal(PreTradeReadinessReason.FeedNotLive, suspended.Readiness.Assessment.Reason);
        Assert.Equal(plan.Id, suspended.Readiness.Tracked.PlanId);
        Assert.Equal(plan, suspended.Readiness.Plan);

        state.SetStatus(status, AsOfUtc.AddSeconds(6));
        var recovered = Assert.Single(state.Read().Results);
        Assert.Equal(PreTradeReadinessState.EntryReady, recovered.Readiness!.Assessment.State);
        Assert.Equal(plan.Id, recovered.Readiness.Tracked.PlanId);
    }

    [Fact]
    public void Expired_plan_validity_is_no_longer_actionable()
    {
        var assessment = Assess(LongPlan(), 99m, evaluatedAtUtc: AsOfUtc.AddMinutes(3));

        Assert.Equal(PreTradeReadinessState.NoLongerActionable, assessment.State);
        Assert.Equal(PreTradeReadinessReason.OpportunityExpired, assessment.Reason);
    }

    [Theory]
    [InlineData(95, TrackedOpportunityStatus.Invalidated)]
    [InlineData(106, TrackedOpportunityStatus.TargetReachedBeforeEntry)]
    [InlineData(97, TrackedOpportunityStatus.ExtendedBeyondEntry)]
    public void Terminal_assessments_survive_health_and_publication_changes(
        decimal price, TrackedOpportunityStatus terminalStatus)
    {
        var configuration = Config();
        var plan = LongPlan();
        var snapshot = Features(price);
        var opportunity = Candidate();
        var evaluation = new PreTradeReadinessTracker().Evaluate(
            snapshot, opportunity, plan, Active(), null, configuration, AsOfUtc);
        var projection = Assert.IsType<PreTradeReadinessProjection>(evaluation.Projection);
        Assert.Equal(terminalStatus, projection.Tracked.Status);
        var state = new ScannerState(configuration: configuration);
        var status = new ScannerStatus(MarketDataHealthStatus.Live, "TestProvider", 1, AsOfUtc, null);
        state.SetResults(status,
            [new ScannerAnalysisResult(snapshot, opportunity, Active(), plan, null, projection)], AsOfUtc);

        foreach (var health in new[] { MarketDataHealthStatus.Stale, MarketDataHealthStatus.Live, MarketDataHealthStatus.Error })
        {
            state.SetStatus(status with { FeedStatus = health }, AsOfUtc.AddSeconds(5));
            Assert.Same(projection.Assessment, Assert.Single(state.Read().Results).Readiness!.Assessment);
        }

        state.SetStatus(status, plan.ValidUntilUtc);
        var expiredPublication = Assert.Single(state.Read().Results);
        Assert.Equal(OpportunityLifecycleState.Expired, expiredPublication.Publication.State);
        Assert.Same(projection.Assessment, expiredPublication.Readiness!.Assessment);
    }

    [Fact]
    public void Expiry_timestamp_is_the_pinned_validity_boundary_not_the_last_market_event()
    {
        var plan = LongPlan();
        var tracker = new PreTradeReadinessTracker();
        var evaluation = tracker.Evaluate(
            Features(99m), Candidate(), plan, Active(), null, Config(), plan.ValidUntilUtc.AddMinutes(1));

        Assert.Equal(TrackedOpportunityStatus.Expired, evaluation.PersistTracked!.Status);
        Assert.Equal(plan.ValidUntilUtc, evaluation.PersistTracked.TerminalAtUtc);
        Assert.Equal(AsOfUtc, evaluation.PersistAssessment!.AsOfUtc);
    }

    [Fact]
    public void Session_change_is_terminal_for_the_tracked_candidate()
    {
        var plan = LongPlan();
        var tracked = Tracked(plan);
        var assessment = new PreTradeReadinessEvaluator().Evaluate(
            tracked,
            plan,
            Features(99m, sessionId: "another-session"),
            Candidate(sessionId: "another-session"),
            Active(),
            null,
            Config(),
            AsOfUtc);

        Assert.Equal(PreTradeReadinessState.NoLongerActionable, assessment.State);
        Assert.Equal(PreTradeReadinessReason.SessionChanged, assessment.Reason);
        Assert.Equal(
            TrackedOpportunityStatus.SessionEnded,
            PreTradeReadinessEvaluator.TerminalStatus(assessment));
    }

    // ----- Context mode -----

    [Fact]
    public void Complete_prior_context_does_not_claim_to_participate_without_using_its_levels()
    {
        var context = PriorContext(PriorSessionContextStatus.Available);
        var assessment = Assess(LongPlan(), 99m, priorContext: context);
        var differentContext = context with
        {
            Context = context.Context with { ContextId = Guid.NewGuid(), PriorSessionLow = 90m }
        };
        var replay = Assess(LongPlan(), 99m, priorContext: differentContext);

        Assert.Equal(ReadinessContextMode.CurrentSessionOnly, assessment.ContextMode);
        Assert.DoesNotContain(assessment.Evidence, item => item.Code == "PriorSessionContextUsed");
        Assert.Contains(assessment.Evidence, item => item.Description.Contains("its levels are not used"));
        Assert.Equal(assessment.Id, replay.Id);
        Assert.Equal(assessment.Evidence, replay.Evidence);
        Assert.Equal(PreTradeReadinessState.EntryReady, assessment.State);
    }

    [Theory]
    [InlineData(PriorSessionContextStatus.Partial)]
    [InlineData(PriorSessionContextStatus.Unavailable)]
    [InlineData(PriorSessionContextStatus.Invalid)]
    public void Partial_or_unavailable_prior_context_falls_back_to_current_session_only(
        PriorSessionContextStatus completeness)
    {
        var assessment = Assess(LongPlan(), 99m, priorContext: PriorContext(completeness));

        Assert.Equal(ReadinessContextMode.CurrentSessionOnly, assessment.ContextMode);
        var fallback = Assert.Single(assessment.Evidence, item => item.Code == "CurrentSessionOnlyFallback");
        Assert.Contains("did not treat partial history as confirmed support or resistance", fallback.Description);
        Assert.Equal(PreTradeReadinessState.EntryReady, assessment.State);
    }

    [Fact]
    public void Missing_prior_context_still_produces_current_session_only_readiness()
    {
        var assessment = Assess(LongPlan(), 99m, priorContext: null);

        Assert.Equal(ReadinessContextMode.CurrentSessionOnly, assessment.ContextMode);
        Assert.Contains(assessment.Evidence, item => item.Code == "CurrentSessionOnlyFallback");
        Assert.Equal(PreTradeReadinessState.EntryReady, assessment.State);
    }

    // ----- Restart -----

    [Fact]
    public void Reconciliation_expires_idle_restored_candidates_at_the_exact_boundary()
    {
        var plan = LongPlan();
        var tracked = Tracked(plan);
        var tracker = new PreTradeReadinessTracker();
        tracker.Restore([new TrackedOpportunitySnapshot(tracked, plan)]);

        Assert.Empty(tracker.Reconcile(plan.ValidUntilUtc.AddTicks(-1)));
        var terminal = Assert.Single(tracker.Reconcile(plan.ValidUntilUtc));
        Assert.Equal(tracked.Id, terminal.Id);
        Assert.Equal(plan.Id, terminal.PlanId);
        Assert.Equal(TrackedOpportunityStatus.Expired, terminal.Status);
        Assert.Equal(plan.ValidUntilUtc, terminal.TerminalAtUtc);
        Assert.Empty(tracker.Reconcile(plan.ValidUntilUtc.AddDays(1)));
    }

    [Fact]
    public void Session_observation_reconciles_old_candidates_without_analysis_and_ignores_late_sessions()
    {
        var tracker = new PreTradeReadinessTracker();
        var oldPlan = LongPlan();
        var original = tracker.Evaluate(
            Features(102m), Candidate(), oldPlan, Active(), null, Config(), AsOfUtc);
        var nextSessionTime = AsOfUtc.AddSeconds(30);
        tracker.ObserveSession(Symbol, "next-session", nextSessionTime);
        var terminal = Assert.Single(tracker.Reconcile(nextSessionTime));
        Assert.Equal(original.PersistTracked!.Id, terminal.Id);
        Assert.Equal(TrackedOpportunityStatus.SessionEnded, terminal.Status);
        Assert.Equal(nextSessionTime, terminal.TerminalAtUtc);

        var nextPlan = oldPlan with { Id = Guid.NewGuid(), SessionId = "next-session", AsOfUtc = nextSessionTime };
        var selected = tracker.Evaluate(
            Features(102m, sessionId: "next-session", asOfUtc: nextSessionTime),
            Candidate(sessionId: "next-session"), nextPlan, Active(), null, Config(), nextSessionTime);
        Assert.NotNull(selected.Projection);

        tracker.ObserveSession(Symbol, Session, AsOfUtc.AddSeconds(20));
        var late = tracker.Evaluate(
            Features(102m, asOfUtc: AsOfUtc.AddSeconds(20)),
            Candidate(), oldPlan, Active(), null, Config(), nextSessionTime);
        Assert.Null(late.Projection);
        Assert.Empty(tracker.Reconcile(nextSessionTime));
        Assert.Equal(nextPlan.Id, Assert.Single(tracker.Reconcile(nextPlan.ValidUntilUtc)).PlanId);
    }

    [Fact]
    public async Task Live_health_timer_persists_idle_expiry_without_any_market_events()
    {
        var plan = LongPlan();
        var tracker = new PreTradeReadinessTracker();
        tracker.Restore([new TrackedOpportunitySnapshot(Tracked(plan), plan)]);
        var clock = new MutableTimeProvider(AsOfUtc);
        var source = new IdleLiveSource();
        var store = new RecordingReadinessStore();
        using var cancellation = new CancellationTokenSource();
        var pipeline = new ScannerPipeline(
            source, new NullScannerStore(), new ScannerState(configuration: Config()), Config(),
            new FeatureCalculator(), new MomentumContinuationDetector(), clock,
            readinessTracker: tracker, readinessStore: store);
        var run = pipeline.RunAsync(cancellation.Token);
        try
        {
            await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock.SetUtcNow(plan.ValidUntilUtc.AddSeconds(10));
            var terminal = await store.TerminalPersisted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(TrackedOpportunityStatus.Expired, terminal.Status);
            Assert.Equal(plan.ValidUntilUtc, terminal.TerminalAtUtc);
            Assert.Empty(tracker.Reconcile(clock.GetUtcNow()));
        }
        finally
        {
            await cancellation.CancelAsync();
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Live_startup_persists_expiry_of_restored_candidates_before_reading_market_data()
    {
        var plan = LongPlan();
        var tracker = new PreTradeReadinessTracker();
        tracker.Restore([new TrackedOpportunitySnapshot(Tracked(plan), plan)]);
        var clock = new MutableTimeProvider(plan.ValidUntilUtc.AddDays(1));
        var source = new IdleLiveSource();
        var store = new RecordingReadinessStore();
        using var cancellation = new CancellationTokenSource();
        var pipeline = new ScannerPipeline(
            source, new NullScannerStore(), new ScannerState(configuration: Config()), Config(),
            new FeatureCalculator(), new MomentumContinuationDetector(), clock,
            readinessTracker: tracker, readinessStore: store);
        var run = pipeline.RunAsync(cancellation.Token);
        try
        {
            var terminal = await store.TerminalPersisted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(plan.ValidUntilUtc, terminal.TerminalAtUtc);
            Assert.Null(Assert.Single(store.Persisted).Assessment);
        }
        finally
        {
            await cancellation.CancelAsync();
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Live_session_events_end_old_candidates_without_needing_a_new_eligible_plan()
    {
        var plan = LongPlan();
        var tracker = new PreTradeReadinessTracker();
        tracker.Restore([new TrackedOpportunitySnapshot(Tracked(plan), plan)]);
        var nextSessionTime = AsOfUtc.AddSeconds(30);
        var source = new IdleLiveSource(
        [
            new TradeEvent(Symbol, "next-session", nextSessionTime, nextSessionTime,
                "Synthetic", "next-session-trade", 100m, 10),
            new TradeEvent(Symbol, Session, AsOfUtc.AddSeconds(10), nextSessionTime,
                "Synthetic", "late-old-session-trade", 100m, 10)
        ]);
        var store = new RecordingReadinessStore();
        using var cancellation = new CancellationTokenSource();
        var pipeline = new ScannerPipeline(
            source, new NullScannerStore(), new ScannerState(configuration: Config()), Config(),
            new FeatureCalculator(), new MomentumContinuationDetector(), new MutableTimeProvider(nextSessionTime),
            readinessTracker: tracker, readinessStore: store);
        var run = pipeline.RunAsync(cancellation.Token);
        try
        {
            var terminal = await store.TerminalPersisted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(TrackedOpportunityStatus.SessionEnded, terminal.Status);
            Assert.Equal(plan.Id, terminal.PlanId);
            Assert.Equal(nextSessionTime, terminal.TerminalAtUtc);
        }
        finally
        {
            await cancellation.CancelAsync();
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Single(store.Persisted);
        Assert.Empty(tracker.Reconcile(plan.ValidUntilUtc));
    }

    [Fact]
    public void Restored_tracked_candidate_is_reevaluated_not_blindly_restored()
    {
        var firstTracker = new PreTradeReadinessTracker();
        var plan = LongPlan();
        var entryReady = firstTracker.Evaluate(
            Features(99m), Candidate(), plan, Active(), null, Config(), AsOfUtc);
        Assert.Equal(PreTradeReadinessState.EntryReady, entryReady.Projection!.Assessment.State);

        var restartedTracker = new PreTradeReadinessTracker();
        restartedTracker.Restore([new TrackedOpportunitySnapshot(entryReady.PersistTracked!, plan)]);
        var laterAsOf = AsOfUtc.AddSeconds(45);
        var reevaluated = restartedTracker.Evaluate(
            Features(103.5m, asOfUtc: laterAsOf), Candidate(), plan, Active(laterAsOf), null, Config(), laterAsOf);

        Assert.Equal(entryReady.Projection.Tracked.Id, reevaluated.Projection!.Tracked.Id);
        Assert.Equal(plan.Id, reevaluated.Projection.Tracked.PlanId);
        Assert.True(reevaluated.Projection.Tracked.HasEstablishedEntryReady);
        Assert.Equal(PreTradeReadinessState.Watching, reevaluated.Projection.Assessment.State);
    }

    [Fact]
    public void Restored_candidate_without_a_resolvable_plan_is_conservatively_terminal()
    {
        var tracker = new PreTradeReadinessTracker();
        var plan = LongPlan();
        var tracked = Tracked(plan);
        tracker.Restore([new TrackedOpportunitySnapshot(tracked, null)]);

        var evaluation = tracker.Evaluate(
            Features(99m), Candidate(), plan with { Id = Guid.NewGuid() }, Active(), null, Config(), AsOfUtc);

        Assert.Equal(PreTradeReadinessState.Unavailable, evaluation.Projection!.Assessment.State);
        Assert.Equal(PreTradeReadinessReason.PlanUnavailable, evaluation.Projection.Assessment.Reason);
        Assert.Equal(TrackedOpportunityStatus.PlanUnavailable, evaluation.PersistTracked!.Status);
    }

    [Fact]
    public void Restore_ignores_terminal_candidates()
    {
        var tracker = new PreTradeReadinessTracker();
        var plan = LongPlan();
        var earlierPlan = plan with { Id = new Guid("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA") };
        var terminal = Tracked(earlierPlan) with
        {
            Status = TrackedOpportunityStatus.Invalidated,
            TerminalAtUtc = AsOfUtc,
            TerminalReasonCode = "InvalidationCrossed"
        };
        tracker.Restore([new TrackedOpportunitySnapshot(terminal, earlierPlan)]);

        var evaluation = tracker.Evaluate(
            Features(102m), Candidate(), plan, Active(), null, Config(), AsOfUtc);

        // A fresh selection occurred because no active candidate was restored.
        Assert.Equal(TrackedOpportunityStatus.Active, evaluation.Projection!.Tracked.Status);
        Assert.Equal(plan.Id, evaluation.Projection.Tracked.PlanId);
        Assert.NotEqual(terminal.Id, evaluation.Projection.Tracked.Id);
        Assert.Null(evaluation.Projection.Tracked.TerminalAtUtc);
    }

    // ----- Identity, determinism, serialization -----

    [Fact]
    public void Assessment_identity_is_deterministic_and_excludes_audit_timing()
    {
        var first = Assess(LongPlan(), 102m, evaluatedAtUtc: AsOfUtc);
        var second = Assess(LongPlan(), 102m, evaluatedAtUtc: AsOfUtc.AddSeconds(30));

        Assert.Equal(first.Id, second.Id);
        Assert.NotEqual(first.EvaluatedAtUtc, second.EvaluatedAtUtc);
    }

    [Fact]
    public void Changed_material_inputs_change_the_assessment_identity()
    {
        var baseline = Assess(LongPlan(), 102m);
        Assert.NotEqual(baseline.Id, Assess(LongPlan(), 102.5m).Id);
        Assert.NotEqual(
            baseline.Id,
            Assess(LongPlan(), 102m, candidate: Candidate(configurationVersion: "config-test-2")).Id);
        Assert.NotEqual(
            baseline.Id,
            Assess(LongPlan(), 102m, priorContext: PriorContext(PriorSessionContextStatus.Available)).Id);
    }

    [Fact]
    public void Assessment_serializes_with_exact_decimals_and_round_trips()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };
        var assessment = Assess(LongPlan(), 103.3m);
        Assert.Equal(3.3m, assessment.DistanceToEntry);
        Assert.Equal(1.1m, assessment.DistanceToEntryRiskMultiple);

        var json = JsonSerializer.Serialize(assessment, options);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("Watching", document.RootElement.GetProperty("state").GetString());
        Assert.Equal("CurrentSessionOnly", document.RootElement.GetProperty("contextMode").GetString());
        Assert.Equal(3.3m, document.RootElement.GetProperty("distanceToEntry").GetDecimal());
        Assert.Equal(1.1m, document.RootElement.GetProperty("distanceToEntryRiskMultiple").GetDecimal());
        Assert.Equal(
            PreTradeReadinessEvaluator.DefinitionVersion,
            document.RootElement.GetProperty("readinessDefinitionVersion").GetString());

        var restored = JsonSerializer.Deserialize<PreTradeReadinessAssessment>(json, options);
        Assert.NotNull(restored);
        Assert.Equal(assessment.Id, restored.Id);
        Assert.Equal(assessment.State, restored.State);
        Assert.Equal(assessment.Reason, restored.Reason);
        Assert.Equal(assessment.ContextMode, restored.ContextMode);
        Assert.Equal(assessment.CurrentReferencePrice, restored.CurrentReferencePrice);
        Assert.Equal(assessment.DistanceToEntry, restored.DistanceToEntry);
        Assert.Equal(assessment.DistanceToEntryRiskMultiple, restored.DistanceToEntryRiskMultiple);
        Assert.Equal(assessment.AsOfUtc, restored.AsOfUtc);
        Assert.Equal(assessment.EvaluatedAtUtc, restored.EvaluatedAtUtc);
        Assert.Equal(assessment.Evidence, restored.Evidence);
    }

    [Fact]
    public void Opportunity_read_model_exposes_readiness_fields()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };
        var tracker = new PreTradeReadinessTracker();
        var plan = LongPlan();
        var snapshot = Features(102m);
        var candidate = Candidate();
        var evaluation = tracker.Evaluate(snapshot, candidate, plan, Active(), null, Config(), AsOfUtc);
        var result = new ScannerAnalysisResult(
            snapshot, candidate, Active(), plan, PriorContext(PriorSessionContextStatus.Partial), evaluation.Projection);
        var readModel = PreTradeReadinessReadModel.From(result);

        Assert.NotNull(readModel);
        var json = JsonSerializer.Serialize(
            new OpportunityPublicationReadModel(candidate, Active(), plan, readModel), options);
        using var document = JsonDocument.Parse(json);
        var readiness = document.RootElement.GetProperty("readiness");
        Assert.Equal("SetupForming", readiness.GetProperty("state").GetString());
        Assert.Equal("ApproachingEntry", readiness.GetProperty("reason").GetString());
        Assert.Equal("CurrentSessionOnly", readiness.GetProperty("contextMode").GetString());
        Assert.Equal(plan.Id, readiness.GetProperty("planId").GetGuid());
        Assert.Equal(102m, readiness.GetProperty("currentPrice").GetDecimal());
        Assert.Equal(98m, readiness.GetProperty("entryZone").GetProperty("low").GetDecimal());
        Assert.Equal(100m, readiness.GetProperty("entryZone").GetProperty("high").GetDecimal());
        Assert.Equal(2m, readiness.GetProperty("distanceToEntry").GetDecimal());
        Assert.Equal(96m, readiness.GetProperty("invalidationPrice").GetDecimal());
        Assert.Equal(106m, readiness.GetProperty("target1Price").GetDecimal());
        Assert.Equal("Partial", readiness.GetProperty("priorContextCompleteness").GetString());

        Assert.Null(PreTradeReadinessReadModel.From(
            new ScannerAnalysisResult(snapshot, candidate, Active(), plan)));
    }

    // ----- Fixture pipeline wiring -----

    [Fact]
    public async Task Fixture_replay_wires_readiness_through_the_pipeline_deterministically()
    {
        var fixturePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "MarketOpportunity.Web", "Fixtures", "momentum-continuation.json"));
        var configuration = new ScannerConfiguration();
        var first = await ReplayFixtureAsync(fixturePath, configuration);
        var second = await ReplayFixtureAsync(fixturePath, configuration);

        Assert.Equal(4, first.Results.Count);
        Assert.Equal(
            first.Results.Select(result => result.Readiness?.Assessment.Id),
            second.Results.Select(result => result.Readiness?.Assessment.Id));
        Assert.Equal(
            first.Results.Select(result => result.Readiness?.Tracked.Id),
            second.Results.Select(result => result.Readiness?.Tracked.Id));
        foreach (var result in first.Results)
        {
            if (result.TradePlan.Status != TradePlanStatus.PlanDefined)
            {
                // No eligible candidate plan exists, so readiness honestly reports no tracked
                // candidate instead of inventing one.
                Assert.Null(result.Readiness);
            }
            else
            {
                Assert.NotNull(result.Readiness);
                Assert.Equal(result.TradePlan.Id, result.Readiness.Tracked.PlanId);
            }
        }
    }

    private static async Task<ScannerReadModel> ReplayFixtureAsync(
        string fixturePath, ScannerConfiguration configuration)
    {
        var state = new ScannerState(new OpportunityPublicationGate(), null, configuration);
        var pipeline = new ScannerPipeline(
            new MarketOpportunity.Infrastructure.FixtureMarketDataSource(fixturePath),
            new NullScannerStore(),
            state,
            configuration,
            new FeatureCalculator(),
            new MomentumContinuationDetector(),
            TimeProvider.System,
            readinessTracker: new PreTradeReadinessTracker(),
            readinessStore: new RecordingReadinessStore());
        await pipeline.RunFixtureAsync(CancellationToken.None);
        return state.Read();
    }

    private sealed class NullScannerStore : IScannerStore
    {
        public Task PersistRunAsync(
            ScannerConfiguration configuration,
            IReadOnlyCollection<MarketEvent> marketEvents,
            FeatureSnapshot featureSnapshot,
            Opportunity opportunity,
            TradePlan tradePlan,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingReadinessStore : IPreTradeReadinessStore
    {
        public List<(TrackedOpportunity Tracked, PreTradeReadinessAssessment? Assessment)> Persisted { get; } = [];
        public TaskCompletionSource<TrackedOpportunity> TerminalPersisted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<TrackedOpportunitySnapshot>> RestoreActiveAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TrackedOpportunitySnapshot>>([]);

        public Task PersistAsync(
            TrackedOpportunity tracked,
            PreTradeReadinessAssessment? assessment,
            CancellationToken cancellationToken)
        {
            Persisted.Add((tracked, assessment));
            if (tracked.Status != TrackedOpportunityStatus.Active)
            {
                TerminalPersisted.TrySetResult(tracked);
            }
            return Task.CompletedTask;
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private long _utcTicks = now.UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);
        public void SetUtcNow(DateTimeOffset value) => Interlocked.Exchange(ref _utcTicks, value.UtcTicks);
    }

    private sealed class IdleLiveSource(IReadOnlyList<MarketEvent>? events = null) : ILiveMarketDataSource
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<MarketEvent> ReadEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            foreach (var marketEvent in events ?? [])
            {
                yield return marketEvent;
            }
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }
    }

    // ----- Helpers -----

    private static PreTradeReadinessAssessment Assess(
        TradePlan plan,
        decimal price,
        TrackedOpportunity? tracked = null,
        Opportunity? candidate = null,
        PublicationDecision? publication = null,
        PriorSessionContextView? priorContext = null,
        FeatureSnapshot? snapshot = null,
        DateTimeOffset? evaluatedAtUtc = null)
    {
        candidate ??= plan.Direction == TradeDirection.Short
            ? Candidate(bias: SetupBias.Short, classification: OpportunityClassification.ProbableShort)
            : Candidate();
        return new PreTradeReadinessEvaluator().Evaluate(
            tracked ?? Tracked(plan),
            plan,
            snapshot ?? Features(price),
            candidate,
            publication ?? Active(),
            priorContext,
            Config(),
            evaluatedAtUtc ?? AsOfUtc);
    }

    private static TrackedOpportunity Tracked(TradePlan plan) =>
        PreTradeReadinessEvaluator.CreateTrackedOpportunity(
            Features(102m),
            plan.Direction == TradeDirection.Short
                ? Candidate(bias: SetupBias.Short, classification: OpportunityClassification.ProbableShort)
                : Candidate(),
            plan);

    private static ScannerConfiguration Config() => new()
    {
        FeatureSetVersion = "features-test",
        SetupDefinitionVersion = "setup-test",
        Version = "config-test"
    };

    private static PublicationDecision Active(DateTimeOffset? evaluatedAtUtc = null) => new(
        OpportunityLifecycleState.Active,
        "PublicationAllowed",
        "Market data is healthy and the candidate remains within its validity period.",
        evaluatedAtUtc ?? AsOfUtc);

    private static TradePlan LongPlan() => new(
        LongPlanId,
        Symbol,
        Session,
        AsOfUtc,
        AsOfUtc.AddMinutes(2),
        TradeDirection.Long,
        TradePlanStatus.PlanDefined,
        new PriceZone(98m, 100m),
        99m,
        96m,
        new TradeTarget(106m, SwingId, "origin-event", "confirmation-event", "session-swing-fractal-v1"),
        null,
        3m,
        7m,
        7m / 3m,
        "Momentum Continuation",
        OpportunityId,
        "session-swing-fractal-v1",
        "features-test",
        "setup-test",
        "config-test",
        "trade-plan-v1",
        MarketDataCapabilities.Unknown,
        []);

    private static TradePlan ShortPlan() => LongPlan() with
    {
        Id = ShortPlanId,
        Direction = TradeDirection.Short,
        EntryZone = new PriceZone(100m, 102m),
        EntryReferencePrice = 101m,
        InvalidationPrice = 104m,
        Target1 = new TradeTarget(94m, SwingId, "origin-event", "confirmation-event", "session-swing-fractal-v1")
    };

    private static Opportunity Candidate(
        SetupBias bias = SetupBias.Long,
        OpportunityClassification classification = OpportunityClassification.ProbableLong,
        IReadOnlyList<string>? vetoes = null,
        string sessionId = Session,
        string configurationVersion = "config-test") => new(
        OpportunityId,
        Symbol,
        sessionId,
        AsOfUtc,
        AsOfUtc.AddMinutes(2),
        "Momentum Continuation",
        bias,
        classification,
        90,
        [new OpportunityEvidence("Momentum", "Positive momentum.")],
        [],
        vetoes ?? [],
        "Below session VWAP.",
        100m,
        "features-test",
        "setup-test",
        configurationVersion,
        MarketDataCapabilities.Unknown);

    private static FeatureSnapshot Features(
        decimal? price,
        bool quoteValid = true,
        bool priceDataValid = true,
        bool hasRequiredData = true,
        decimal? spreadBasisPoints = 2m,
        string sessionId = Session,
        DateTimeOffset? asOfUtc = null) => new(
        new Guid("99999999-9999-9999-9999-999999999999"),
        Symbol,
        sessionId,
        asOfUtc ?? AsOfUtc,
        "features-test",
        price,
        price - 0.01m,
        price + 0.01m,
        spreadBasisPoints,
        99.5m,
        null,
        VwapSlopeDirection.Insufficient,
        0.5m,
        0.1m,
        null,
        100,
        100_000m,
        ShortHorizonDollarVolumeStatus.Available,
        100,
        price ?? 100m,
        price ?? 100m,
        null,
        null,
        null,
        AdxStatus.Insufficient,
        null,
        RealizedVolatilityStatus.Insufficient,
        TrendDirectionContext.Insufficient,
        null,
        null,
        null,
        null,
        null,
        TimeSpan.Zero,
        quoteValid,
        priceDataValid,
        hasRequiredData,
        true,
        DataQualityStatus.Good,
        MarketDataCapabilities.Unknown);

    private static PriorSessionContextView PriorContext(PriorSessionContextStatus completeness)
    {
        var available = completeness == PriorSessionContextStatus.Available;
        var context = new PriorSessionContext(
            new Guid("88888888-8888-8888-8888-888888888888"),
            Symbol,
            Session,
            available || completeness == PriorSessionContextStatus.Partial ? "prior-session" : null,
            AsOfUtc.AddDays(-1),
            AsOfUtc.AddDays(-1).AddHours(6.5),
            available ? 101m : null,
            available ? 95m : null,
            available ? 100m : null,
            available ? AsOfUtc.AddDays(-1).AddHours(6) : null,
            available
                ? PriorSessionCloseSemantics.LastEligibleTradeAtOrBeforeSessionEnd
                : PriorSessionCloseSemantics.Unavailable,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            MarketStructureState.NotComputed,
            [],
            [],
            completeness,
            "Deterministic test context.",
            MarketDataCapabilities.Unknown,
            "RecordedFixture",
            null,
            null,
            available,
            available,
            false,
            "prior-session-context-v1");
        return new PriorSessionContextView(
            context, 100m, null, null, null, null, null, null, null, null, null, null);
    }
}
