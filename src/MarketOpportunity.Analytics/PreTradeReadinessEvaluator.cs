using System.Globalization;
using MarketOpportunity.Domain;

namespace MarketOpportunity.Analytics;

public sealed record TrackedOpportunitySelectionResult(
    bool IsEligible,
    IReadOnlyList<PreTradeReadinessEvidence> Reasons);

/// <summary>
/// Deterministic pre-trade readiness over existing approved analytical outputs. Readiness combines
/// the pinned immutable TradePlan, the current analytical opportunity, the current publication
/// decision, and current data-quality fields; it is not a second strategy detector and adds no new
/// confirmation indicator. States describe market conditions around a tracked candidate, never the
/// user's holdings. Readiness is recomputed from current inputs; it is not a linear workflow.
/// </summary>
public sealed class PreTradeReadinessEvaluator
{
    public const string DefinitionVersion = "pre-trade-readiness-v2";
    public const string SelectionDefinitionVersion = "tracked-opportunity-selection-v1";

    /// <summary>
    /// Selection policy for pinning a candidate TradePlan (tracked-opportunity-selection-v1).
    /// The caller enforces the remaining rule that no active tracked candidate already exists
    /// for the symbol/session.
    /// </summary>
    public static TrackedOpportunitySelectionResult EvaluateSelection(
        FeatureSnapshot snapshot,
        Opportunity opportunity,
        TradePlan plan,
        PublicationDecision publication)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(opportunity);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(publication);
        var reasons = new List<PreTradeReadinessEvidence>();
        if (plan.OpportunityId != opportunity.Id
            || !plan.Symbol.Equals(snapshot.Symbol, StringComparison.OrdinalIgnoreCase)
            || !plan.SessionId.Equals(snapshot.SessionId, StringComparison.Ordinal)
            || plan.AsOfUtc != snapshot.AsOfUtc)
        {
            reasons.Add(new PreTradeReadinessEvidence(
                "PlanAnalysisMismatch",
                "The candidate plan does not describe the same analysis as the snapshot and opportunity."));
        }

        if (plan.Status != TradePlanStatus.PlanDefined)
        {
            reasons.Add(new PreTradeReadinessEvidence(
                "PlanNotDefined",
                "Only a PlanDefined candidate TradePlan may be pinned for tracking."));
        }

        if (plan.Direction is null)
        {
            reasons.Add(new PreTradeReadinessEvidence(
                "DirectionUnavailable",
                "The candidate plan has no trade direction."));
        }
        else if (DirectionFromBias(opportunity.Bias) != plan.Direction)
        {
            reasons.Add(new PreTradeReadinessEvidence(
                "DirectionMismatch",
                "The analytical opportunity direction does not agree with the candidate plan direction."));
        }

        if (publication.State != OpportunityLifecycleState.Active)
        {
            reasons.Add(new PreTradeReadinessEvidence(
                "PublicationNotActive",
                $"Publication is {publication.State} ({publication.ReasonCode}); only Active candidates are selected."));
        }

        if (!snapshot.HasRequiredData || !snapshot.IsPriceDataValid)
        {
            reasons.Add(new PreTradeReadinessEvidence(
                "RequiredDataInvalid",
                "Current required market data is incomplete or invalid."));
        }

        if (plan.RiskPerShare is not > 0)
        {
            reasons.Add(new PreTradeReadinessEvidence(
                "NonPositiveRisk",
                "Risk per share must be positive for a tracked candidate."));
        }

        if (plan.RewardPerShareToTarget1 is not > 0)
        {
            reasons.Add(new PreTradeReadinessEvidence(
                "NonPositiveReward",
                "Reward per share to Target 1 must be positive for a tracked candidate."));
        }

        if (plan.Direction is { } direction && !HasValidGeometry(plan, direction))
        {
            reasons.Add(new PreTradeReadinessEvidence(
                "InvalidPlanGeometry",
                "The candidate plan geometry does not satisfy the expected structural price ordering."));
        }

        return new TrackedOpportunitySelectionResult(reasons.Count == 0, reasons.AsReadOnly());
    }

    /// <summary>
    /// Pins an eligible plan as a tracked candidate. Selection time is the analysis event time
    /// (snapshot AsOfUtc), never wall-clock time, so replaying identical events reproduces the
    /// identical tracked identity.
    /// </summary>
    public static TrackedOpportunity CreateTrackedOpportunity(
        FeatureSnapshot snapshot,
        Opportunity opportunity,
        TradePlan plan)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(opportunity);
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Direction is not { } direction)
        {
            throw new ArgumentException("A tracked candidate requires a plan direction.", nameof(plan));
        }

        var selectedAtUtc = snapshot.AsOfUtc.ToUniversalTime();
        var id = DeterministicId.From(string.Join(
            "\u001f",
            SelectionDefinitionVersion,
            plan.Id.ToString("N"),
            opportunity.Id.ToString("N"),
            plan.Symbol,
            plan.SessionId,
            direction.ToString(),
            plan.AsOfUtc.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture),
            selectedAtUtc.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture)));
        return new TrackedOpportunity(
            id,
            plan.Id,
            opportunity.Id,
            plan.Symbol,
            plan.SessionId,
            direction,
            selectedAtUtc,
            plan.AsOfUtc,
            SelectionDefinitionVersion,
            TrackedOpportunityStatus.Active,
            "EligiblePlanSelected",
            "The newest eligible PlanDefined candidate plan was pinned for readiness tracking.",
            HasEstablishedEntryReady: false,
            TerminalAtUtc: null,
            TerminalReasonCode: null,
            TerminalReason: null);
    }

    /// <summary>
    /// Maps a terminal assessment to the tracked-candidate terminal status, or null when the
    /// assessment leaves the tracked candidate active. Feed/data-health suspension is never terminal.
    /// </summary>
    public static TrackedOpportunityStatus? TerminalStatus(PreTradeReadinessAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        return assessment.Reason switch
        {
            PreTradeReadinessReason.OpportunityExpired => TrackedOpportunityStatus.Expired,
            PreTradeReadinessReason.InvalidationCrossed => TrackedOpportunityStatus.Invalidated,
            PreTradeReadinessReason.TargetReachedBeforeEntry => TrackedOpportunityStatus.TargetReachedBeforeEntry,
            PreTradeReadinessReason.ExtendedBeyondEntry => TrackedOpportunityStatus.ExtendedBeyondEntry,
            PreTradeReadinessReason.SessionChanged => TrackedOpportunityStatus.SessionEnded,
            PreTradeReadinessReason.PlanUnavailable
                or PreTradeReadinessReason.PlanNotDefined
                or PreTradeReadinessReason.DirectionUnavailable
                or PreTradeReadinessReason.DirectionMismatch
                when assessment.State == PreTradeReadinessState.Unavailable =>
                    TrackedOpportunityStatus.PlanUnavailable,
            _ => null
        };
    }

    /// <summary>
    /// Computes the current readiness of a tracked candidate from market state at the snapshot's
    /// AsOfUtc. Price geometry uses the pinned immutable plan and market event time only;
    /// <paramref name="evaluatedAtUtc"/> controls current quote freshness and validity-period
    /// expiry (matching publication semantics), never analytical geometry.
    /// </summary>
    public PreTradeReadinessAssessment Evaluate(
        TrackedOpportunity tracked,
        TradePlan? plan,
        FeatureSnapshot snapshot,
        Opportunity currentOpportunity,
        PublicationDecision publication,
        PriorSessionContextView? priorContext,
        ScannerConfiguration configuration,
        DateTimeOffset evaluatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(tracked);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(currentOpportunity);
        ArgumentNullException.ThrowIfNull(publication);
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();

        var evidence = new List<PreTradeReadinessEvidence>();
        var completeness = priorContext?.Context.Completeness;
        var contextMode = ReadinessContextMode.CurrentSessionOnly;
        evidence.Add(new PreTradeReadinessEvidence(
            "CurrentSessionOnlyFallback",
            completeness is null
                ? "No prior-session context is attached; readiness used current-session analysis only."
                : $"Prior-session context is {completeness}, but its levels are not used by this readiness definition; readiness used current-session analysis only and did not treat partial history as confirmed support or resistance."));

        PreTradeReadinessState state;
        PreTradeReadinessReason reason;
        decimal? price = snapshot.IsPriceDataValid ? snapshot.ReferencePrice : null;
        decimal? distanceToEntry = null;
        decimal? distanceRiskMultiple = null;
        var insideEntryZone = false;

        if (tracked.Status != TrackedOpportunityStatus.Active)
        {
            reason = tracked.Status switch
            {
                TrackedOpportunityStatus.Expired => PreTradeReadinessReason.OpportunityExpired,
                TrackedOpportunityStatus.Invalidated => PreTradeReadinessReason.InvalidationCrossed,
                TrackedOpportunityStatus.TargetReachedBeforeEntry => PreTradeReadinessReason.TargetReachedBeforeEntry,
                TrackedOpportunityStatus.ExtendedBeyondEntry => PreTradeReadinessReason.ExtendedBeyondEntry,
                TrackedOpportunityStatus.SessionEnded => PreTradeReadinessReason.SessionChanged,
                TrackedOpportunityStatus.PlanUnavailable => PreTradeReadinessReason.PlanUnavailable,
                _ => throw new InvalidOperationException($"Unknown tracked lifecycle status: {tracked.Status}.")
            };
            state = tracked.Status == TrackedOpportunityStatus.PlanUnavailable
                ? PreTradeReadinessState.Unavailable
                : PreTradeReadinessState.NoLongerActionable;
            evidence.Add(new PreTradeReadinessEvidence(
                "TrackedCandidateTerminal",
                $"The tracked candidate ended at {tracked.TerminalAtUtc:O}: {tracked.TerminalReason}"));
        }
        else if (plan is null || plan.Id != tracked.PlanId)
        {
            state = PreTradeReadinessState.Unavailable;
            reason = PreTradeReadinessReason.PlanUnavailable;
            evidence.Add(new PreTradeReadinessEvidence(
                "TrackedPlanUnresolved",
                "The pinned immutable TradePlan could not be resolved for this tracked candidate."));
        }
        else if (!snapshot.SessionId.Equals(tracked.SessionId, StringComparison.Ordinal)
            || !snapshot.Symbol.Equals(tracked.Symbol, StringComparison.OrdinalIgnoreCase))
        {
            state = PreTradeReadinessState.NoLongerActionable;
            reason = PreTradeReadinessReason.SessionChanged;
            evidence.Add(new PreTradeReadinessEvidence(
                "SessionChanged",
                "The evaluated market state belongs to a different symbol or session than the tracked candidate."));
        }
        else if (plan.Status != TradePlanStatus.PlanDefined)
        {
            state = PreTradeReadinessState.Unavailable;
            reason = PreTradeReadinessReason.PlanNotDefined;
        }
        else if (plan.Direction is null)
        {
            state = PreTradeReadinessState.Unavailable;
            reason = PreTradeReadinessReason.DirectionUnavailable;
        }
        else if (plan.Direction != tracked.Direction)
        {
            state = PreTradeReadinessState.Unavailable;
            reason = PreTradeReadinessReason.DirectionMismatch;
            evidence.Add(new PreTradeReadinessEvidence(
                "TrackedDirectionMismatch",
                "The pinned plan direction no longer matches the tracked candidate direction."));
        }
        else if (!HasValidGeometry(plan, tracked.Direction))
        {
            state = PreTradeReadinessState.Unavailable;
            reason = PreTradeReadinessReason.PlanUnavailable;
            evidence.Add(new PreTradeReadinessEvidence(
                "InvalidPlanGeometry",
                "The pinned plan geometry does not satisfy the expected structural price ordering."));
        }
        else if (evaluatedAtUtc.ToUniversalTime() >= plan.ValidUntilUtc)
        {
            state = PreTradeReadinessState.NoLongerActionable;
            reason = PreTradeReadinessReason.OpportunityExpired;
            evidence.Add(new PreTradeReadinessEvidence(
                "OpportunityExpired",
                "The pinned candidate plan validity period has elapsed."));
        }
        else if (publication.State == OpportunityLifecycleState.Suspended)
        {
            state = PreTradeReadinessState.Suspended;
            reason = publication.ReasonCode switch
            {
                "MarketDataGapUnresolved" => PreTradeReadinessReason.DataGapUnresolved,
                "MarketDataStale" or "MarketDataOffline" or "MarketDataError" or "MarketDataConnecting" =>
                    PreTradeReadinessReason.FeedNotLive,
                _ => PreTradeReadinessReason.PublicationNotActive
            };
            evidence.Add(new PreTradeReadinessEvidence(
                publication.ReasonCode,
                $"Current data health is insufficient to treat the assessment as current: {publication.Reason}"));
        }
        else if (!snapshot.HasRequiredData)
        {
            state = PreTradeReadinessState.Suspended;
            reason = PreTradeReadinessReason.DataIncomplete;
            evidence.Add(new PreTradeReadinessEvidence(
                "DataIncomplete",
                "Required current market data is unavailable; readiness cannot be presented as current."));
        }
        else if (price is not > 0)
        {
            state = PreTradeReadinessState.Suspended;
            reason = PreTradeReadinessReason.InvalidCurrentPrice;
            evidence.Add(new PreTradeReadinessEvidence(
                "InvalidCurrentPrice",
                "No positive, valid current reference price is available to evaluate plan geometry."));
        }
        else
        {
            (state, reason, distanceToEntry, distanceRiskMultiple, insideEntryZone) = EvaluateGeometry(
                tracked,
                plan,
                snapshot,
                currentOpportunity,
                publication,
                configuration,
                price.Value,
                evidence,
                evaluatedAtUtc);
        }

        var id = CreateIdentity(
            tracked,
            snapshot,
            currentOpportunity,
            state,
            reason,
            contextMode,
            price,
            distanceToEntry,
            distanceRiskMultiple,
            insideEntryZone,
            evidence);
        return new PreTradeReadinessAssessment(
            id,
            tracked.Id,
            tracked.PlanId,
            currentOpportunity.Id,
            tracked.Symbol,
            tracked.SessionId,
            snapshot.AsOfUtc.ToUniversalTime(),
            evaluatedAtUtc.ToUniversalTime(),
            state,
            reason,
            tracked.Direction,
            contextMode,
            price,
            distanceToEntry,
            distanceRiskMultiple,
            insideEntryZone,
            DefinitionVersion,
            snapshot.FeatureSetVersion,
            currentOpportunity.ConfigurationVersion,
            snapshot.MarketData,
            evidence.AsReadOnly());
    }

    private static (
        PreTradeReadinessState State,
        PreTradeReadinessReason Reason,
        decimal? DistanceToEntry,
        decimal? DistanceRiskMultiple,
        bool InsideEntryZone) EvaluateGeometry(
        TrackedOpportunity tracked,
        TradePlan plan,
        FeatureSnapshot snapshot,
        Opportunity currentOpportunity,
        PublicationDecision publication,
        ScannerConfiguration configuration,
        decimal price,
        List<PreTradeReadinessEvidence> evidence,
        DateTimeOffset evaluatedAtUtc)
    {
        var zone = plan.EntryZone!;
        var invalidation = plan.InvalidationPrice!.Value;
        var target1 = plan.Target1!.Price;
        var risk = plan.RiskPerShare!.Value;
        var isLong = tracked.Direction == TradeDirection.Long;
        var invalidationCrossed = isLong ? price <= invalidation : price >= invalidation;
        if (invalidationCrossed)
        {
            evidence.Add(new PreTradeReadinessEvidence(
                "InvalidationCrossed",
                $"The current price {Format(price)} crossed the structural invalidation {Format(invalidation)}."));
            return (PreTradeReadinessState.NoLongerActionable, PreTradeReadinessReason.InvalidationCrossed, null, null, false);
        }

        var targetReached = isLong ? price >= target1 : price <= target1;
        if (targetReached && !tracked.HasEstablishedEntryReady)
        {
            evidence.Add(new PreTradeReadinessEvidence(
                "TargetReachedBeforeEntry",
                $"The current price {Format(price)} reached Target 1 {Format(target1)} before candidate entry conditions were observed aligned."));
            return (PreTradeReadinessState.NoLongerActionable, PreTradeReadinessReason.TargetReachedBeforeEntry, null, null, false);
        }

        var insideZone = price >= zone.Low && price <= zone.High;
        var beyondEntry = isLong ? price < zone.Low : price > zone.High;
        if (beyondEntry && !tracked.HasEstablishedEntryReady)
        {
            evidence.Add(new PreTradeReadinessEvidence(
                "ExtendedBeyondEntry",
                "The current price moved through the candidate entry area in the invalidating direction before entry conditions were observed aligned."));
            var beyondDistance = isLong ? zone.Low - price : price - zone.High;
            return (
                PreTradeReadinessState.NoLongerActionable,
                PreTradeReadinessReason.ExtendedBeyondEntry,
                beyondDistance,
                risk > 0 ? beyondDistance / risk : null,
                false);
        }

        if (insideZone)
        {
            var confirmationFailures = EvaluateConfirmation(
                snapshot, currentOpportunity, publication, configuration, tracked.Direction, evaluatedAtUtc);
            if (confirmationFailures.Count == 0)
            {
                evidence.Add(new PreTradeReadinessEvidence(
                    "EntryConditionsAligned",
                    "Candidate entry conditions are aligned: the current price is inside the pinned entry area and the approved analytical, data-quality, and publication conditions remain aligned."));
                return (PreTradeReadinessState.EntryReady, PreTradeReadinessReason.InsideEntryZone, 0m, 0m, true);
            }

            evidence.AddRange(confirmationFailures);
            return (PreTradeReadinessState.EntryZoneActive, PreTradeReadinessReason.ConfirmationNotAligned, 0m, 0m, true);
        }

        // Outside the entry zone on the approach side (or beyond entry after an established
        // entry-ready observation). Distance is normalized by the pinned plan's RiskPerShare.
        var distance = isLong
            ? (price > zone.High ? price - zone.High : zone.Low - price)
            : (price < zone.Low ? zone.Low - price : price - zone.High);
        var riskMultiple = distance / risk;
        var onApproachSide = isLong ? price > zone.High : price < zone.Low;
        if (onApproachSide && riskMultiple <= configuration.PreTrade.ApproachDistanceRiskMultiple)
        {
            evidence.Add(new PreTradeReadinessEvidence(
                "ApproachingEntry",
                $"The current price is within {Format(configuration.PreTrade.ApproachDistanceRiskMultiple)}x risk per share of the candidate entry area ({Format(riskMultiple)}x)."));
            return (PreTradeReadinessState.SetupForming, PreTradeReadinessReason.ApproachingEntry, distance, riskMultiple, false);
        }

        return (PreTradeReadinessState.Watching, PreTradeReadinessReason.None, distance, riskMultiple, false);
    }

    private static List<PreTradeReadinessEvidence> EvaluateConfirmation(
        FeatureSnapshot snapshot,
        Opportunity currentOpportunity,
        PublicationDecision publication,
        ScannerConfiguration configuration,
        TradeDirection direction,
        DateTimeOffset evaluatedAtUtc)
    {
        var failures = new List<PreTradeReadinessEvidence>();
        if (DirectionFromBias(currentOpportunity.Bias) != direction)
        {
            failures.Add(new PreTradeReadinessEvidence(
                "ConfirmationDirectionMismatch",
                "The current analytical opportunity direction does not match the pinned plan direction."));
        }

        var qualifyingClassification = direction == TradeDirection.Long
            ? OpportunityClassification.ProbableLong
            : OpportunityClassification.ProbableShort;
        if (currentOpportunity.Classification != qualifyingClassification)
        {
            failures.Add(new PreTradeReadinessEvidence(
                "ClassificationNotQualifying",
                $"The current classification {currentOpportunity.Classification} is not the qualifying directional classification {qualifyingClassification}."));
        }

        if (currentOpportunity.ActiveVetoes.Count > 0)
        {
            failures.Add(new PreTradeReadinessEvidence(
                "ActiveAnalyticalVetoes",
                $"Active analytical vetoes block entry confirmation: {string.Join(", ", currentOpportunity.ActiveVetoes)}."));
        }

        if (publication.State != OpportunityLifecycleState.Active)
        {
            failures.Add(new PreTradeReadinessEvidence(
                "PublicationNotActive",
                $"The current publication is {publication.State} ({publication.ReasonCode}), not Active."));
        }

        if (!snapshot.IsQuoteValid
            || snapshot.QuoteAge is not { } quoteAge
            || quoteAge < TimeSpan.Zero
            || evaluatedAtUtc - snapshot.AsOfUtc + quoteAge < TimeSpan.Zero
            || evaluatedAtUtc - snapshot.AsOfUtc + quoteAge
                > TimeSpan.FromSeconds(configuration.MaximumQuoteAgeSeconds))
        {
            failures.Add(new PreTradeReadinessEvidence(
                "InvalidQuote",
                "The current quote is invalid or not fresh."));
        }

        if (snapshot.SpreadBasisPoints is not { } spread)
        {
            failures.Add(new PreTradeReadinessEvidence(
                "SpreadUnavailable",
                "No current spread is available to validate against the approved spread limit."));
        }
        else if (spread > configuration.MaximumSpreadBasisPoints)
        {
            failures.Add(new PreTradeReadinessEvidence(
                "SpreadTooWide",
                $"The current spread {Format(spread)} bps exceeds the approved limit {Format(configuration.MaximumSpreadBasisPoints)} bps."));
        }

        return failures;
    }

    private static bool HasValidGeometry(TradePlan plan, TradeDirection direction)
    {
        if (plan.EntryZone is not { } zone
            || plan.InvalidationPrice is not { } invalidation
            || plan.Target1 is not { } target
            || plan.RiskPerShare is not > 0
            || zone.Low > zone.High)
        {
            return false;
        }

        return direction == TradeDirection.Long
            ? invalidation < zone.Low && zone.High < target.Price
            : target.Price < zone.Low && zone.High < invalidation;
    }

    private static TradeDirection? DirectionFromBias(SetupBias bias) => bias switch
    {
        SetupBias.Long => TradeDirection.Long,
        SetupBias.Short => TradeDirection.Short,
        _ => null
    };

    private static Guid CreateIdentity(
        TrackedOpportunity tracked,
        FeatureSnapshot snapshot,
        Opportunity currentOpportunity,
        PreTradeReadinessState state,
        PreTradeReadinessReason reason,
        ReadinessContextMode contextMode,
        decimal? price,
        decimal? distanceToEntry,
        decimal? distanceRiskMultiple,
        bool insideEntryZone,
        IReadOnlyList<PreTradeReadinessEvidence> evidence)
    {
        // EvaluatedAtUtc is audit timing and deliberately excluded from deterministic identity.
        var values = new List<string>
        {
            DefinitionVersion,
            tracked.Id.ToString("N"),
            tracked.PlanId.ToString("N"),
            currentOpportunity.Id.ToString("N"),
            tracked.Symbol,
            tracked.SessionId,
            snapshot.AsOfUtc.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture),
            state.ToString(),
            reason.ToString(),
            tracked.Direction.ToString(),
            contextMode.ToString(),
            Format(price),
            Format(distanceToEntry),
            Format(distanceRiskMultiple),
            insideEntryZone.ToString(CultureInfo.InvariantCulture),
            snapshot.FeatureSetVersion,
            currentOpportunity.ConfigurationVersion,
            snapshot.MarketData.Provider,
            snapshot.MarketData.Feed,
            snapshot.MarketData.Coverage.ToString(),
            snapshot.MarketData.IsRealTime.ToString(CultureInfo.InvariantCulture),
            snapshot.MarketData.SupportsTrades.ToString(CultureInfo.InvariantCulture),
            snapshot.MarketData.SupportsQuotes.ToString(CultureInfo.InvariantCulture)
        };
        foreach (var item in evidence)
        {
            values.Add(item.Code);
            values.Add(item.Description);
        }

        return DeterministicId.From(string.Join("\u001f", values));
    }

    private static string Format(decimal? value) =>
        value?.ToString("G29", CultureInfo.InvariantCulture) ?? "";
}
