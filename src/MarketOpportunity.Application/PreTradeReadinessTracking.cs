using MarketOpportunity.Analytics;
using MarketOpportunity.Domain;

namespace MarketOpportunity.Application;

/// <summary>A restored tracked candidate and its resolved pinned immutable plan, when available.</summary>
public sealed record TrackedOpportunitySnapshot(TrackedOpportunity Tracked, TradePlan? Plan);

public interface IPreTradeReadinessStore
{
    Task<IReadOnlyList<TrackedOpportunitySnapshot>> RestoreActiveAsync(CancellationToken cancellationToken);

    Task PersistAsync(
        TrackedOpportunity tracked,
        PreTradeReadinessAssessment? assessment,
        CancellationToken cancellationToken);
}

/// <summary>
/// Current in-memory readiness projection for one symbol/session analysis. The pinned plan is the
/// immutable TradePlan referenced by the tracked candidate; it is never the newest recalculated plan.
/// </summary>
public sealed record PreTradeReadinessProjection(
    TrackedOpportunity Tracked,
    TradePlan? Plan,
    PreTradeReadinessAssessment Assessment);

/// <summary>Result of one tracker evaluation, including what must be persisted.</summary>
public sealed record PreTradeReadinessEvaluation(
    PreTradeReadinessProjection? Projection,
    TrackedOpportunity? PersistTracked,
    PreTradeReadinessAssessment? PersistAssessment);

/// <summary>
/// Owns the active tracked candidate per symbol/session. One eligible PlanDefined TradePlan is
/// pinned and deliberately NOT replaced when newer plans are calculated; the pinned candidate is
/// followed until a terminal lifecycle condition, after which a subsequent evaluation may select
/// the newest eligible candidate. Feed/data-health suspension is never terminal.
/// </summary>
public sealed class PreTradeReadinessTracker
{
    private readonly object _sync = new();
    private readonly PreTradeReadinessEvaluator _evaluator = new();
    private readonly Dictionary<(string Symbol, string SessionId), (TrackedOpportunity Tracked, TradePlan? Plan)> _active = [];

    /// <summary>
    /// Restores persisted active tracked candidates after a restart. Prior readiness states are not
    /// restored; readiness is recomputed from the next trustworthy current market state.
    /// </summary>
    public void Restore(IReadOnlyList<TrackedOpportunitySnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        lock (_sync)
        {
            foreach (var snapshot in snapshots)
            {
                if (snapshot.Tracked.Status != TrackedOpportunityStatus.Active)
                {
                    continue;
                }

                var key = Key(snapshot.Tracked.Symbol, snapshot.Tracked.SessionId);
                _active.TryAdd(key, (snapshot.Tracked, snapshot.Plan));
            }
        }
    }

    /// <summary>
    /// Evaluates readiness for one analysis. Selects a new tracked candidate only when no active
    /// candidate exists for the symbol/session and the selection policy is satisfied; otherwise the
    /// previously pinned plan is evaluated against the current market state.
    /// </summary>
    public PreTradeReadinessEvaluation Evaluate(
        FeatureSnapshot snapshot,
        Opportunity opportunity,
        TradePlan currentPlan,
        PublicationDecision publication,
        PriorSessionContextView? priorContext,
        ScannerConfiguration configuration,
        DateTimeOffset evaluatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(opportunity);
        ArgumentNullException.ThrowIfNull(currentPlan);
        ArgumentNullException.ThrowIfNull(publication);
        ArgumentNullException.ThrowIfNull(configuration);
        lock (_sync)
        {
            var key = Key(snapshot.Symbol, snapshot.SessionId);
            if (!_active.TryGetValue(key, out var active))
            {
                var selection = PreTradeReadinessEvaluator.EvaluateSelection(
                    snapshot, opportunity, currentPlan, publication);
                if (!selection.IsEligible)
                {
                    return new PreTradeReadinessEvaluation(null, null, null);
                }

                var selected = PreTradeReadinessEvaluator.CreateTrackedOpportunity(
                    snapshot, opportunity, currentPlan);
                active = (selected, currentPlan);
                _active[key] = active;
            }

            var assessment = _evaluator.Evaluate(
                active.Tracked,
                active.Plan,
                snapshot,
                opportunity,
                publication,
                priorContext,
                configuration,
                evaluatedAtUtc);
            var tracked = active.Tracked;
            if (PreTradeReadinessEvaluator.TerminalStatus(assessment) is { } terminalStatus)
            {
                tracked = tracked with
                {
                    Status = terminalStatus,
                    TerminalAtUtc = assessment.AsOfUtc,
                    TerminalReasonCode = assessment.Reason.ToString(),
                    TerminalReason =
                        "The tracked candidate reached a terminal readiness condition; a subsequent evaluation may select the newest eligible candidate."
                };
                _active.Remove(key);
            }
            else if (assessment.State == PreTradeReadinessState.EntryReady
                && !tracked.HasEstablishedEntryReady)
            {
                tracked = tracked with { HasEstablishedEntryReady = true };
                _active[key] = (tracked, active.Plan);
            }

            return new PreTradeReadinessEvaluation(
                new PreTradeReadinessProjection(tracked, active.Plan, assessment),
                tracked,
                assessment);
        }
    }

    private static (string Symbol, string SessionId) Key(string symbol, string sessionId) =>
        (symbol.ToUpperInvariant(), sessionId);
}

/// <summary>
/// Read model for the current readiness of a tracked candidate. Readiness states describe market
/// conditions around the candidate; they are never execution instructions or position state.
/// </summary>
public sealed record PreTradeReadinessReadModel(
    Guid TrackedOpportunityId,
    Guid PlanId,
    Guid AssessmentId,
    TrackedOpportunityStatus TrackedStatus,
    PreTradeReadinessState State,
    PreTradeReadinessReason Reason,
    TradeDirection Direction,
    ReadinessContextMode ContextMode,
    decimal? CurrentPrice,
    PriceZone? EntryZone,
    decimal? DistanceToEntry,
    decimal? DistanceToEntryRiskMultiple,
    bool IsInsideEntryZone,
    decimal? InvalidationPrice,
    decimal? Target1Price,
    decimal? RewardRiskRatioTarget1,
    DateTimeOffset AsOfUtc,
    string ReadinessDefinitionVersion,
    PriorSessionContextStatus? PriorContextCompleteness,
    IReadOnlyList<PreTradeReadinessEvidence> Evidence)
{
    public static PreTradeReadinessReadModel? From(ScannerAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Readiness is not { } readiness)
        {
            return null;
        }

        var assessment = readiness.Assessment;
        return new PreTradeReadinessReadModel(
            readiness.Tracked.Id,
            readiness.Tracked.PlanId,
            assessment.Id,
            readiness.Tracked.Status,
            assessment.State,
            assessment.Reason,
            assessment.Direction,
            assessment.ContextMode,
            assessment.CurrentReferencePrice,
            readiness.Plan?.EntryZone,
            assessment.DistanceToEntry,
            assessment.DistanceToEntryRiskMultiple,
            assessment.IsInsideEntryZone,
            readiness.Plan?.InvalidationPrice,
            readiness.Plan?.Target1?.Price,
            readiness.Plan?.RewardRiskRatioTarget1,
            assessment.AsOfUtc,
            assessment.ReadinessDefinitionVersion,
            result.PriorSessionContext?.Context.Completeness,
            assessment.Evidence);
    }
}
