using System.Text.Json;
using MarketOpportunity.Application;
using MarketOpportunity.Domain;
using Microsoft.EntityFrameworkCore;

namespace MarketOpportunity.Infrastructure;

/// <summary>
/// Persists the tracked-candidate association/lifecycle and the immutable readiness history
/// (ADR-008). Tracked rows allow controlled lifecycle updates only; readiness rows are immutable,
/// idempotent on identical replay, and fail on a different payload for the same identity.
/// <c>EvaluatedAtUtc</c> is audit timing and is excluded from the material-content comparison.
/// </summary>
public sealed class EfPreTradeReadinessStore(MarketOpportunityDbContext dbContext) : IPreTradeReadinessStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<TrackedOpportunitySnapshot>> RestoreActiveAsync(
        CancellationToken cancellationToken)
    {
        var activeStatus = nameof(TrackedOpportunityStatus.Active);
        var activeEntities = await dbContext.TrackedOpportunities
            .AsNoTracking()
            .Where(item => item.Status == activeStatus)
            .ToListAsync(cancellationToken);
        if (activeEntities.Count == 0)
        {
            return [];
        }

        var planIds = activeEntities.Select(item => item.PlanId).Distinct().ToArray();
        var plans = await dbContext.TradePlans
            .AsNoTracking()
            .Where(item => planIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.TradePlanJson, cancellationToken);
        var snapshots = new List<TrackedOpportunitySnapshot>(activeEntities.Count);
        foreach (var entity in activeEntities)
        {
            TradePlan? plan = null;
            if (plans.TryGetValue(entity.PlanId, out var planJson))
            {
                plan = JsonSerializer.Deserialize<TradePlan>(planJson, JsonOptions);
            }

            snapshots.Add(new TrackedOpportunitySnapshot(ToTracked(entity), plan));
        }

        return snapshots;
    }

    public async Task PersistAsync(
        TrackedOpportunity tracked,
        PreTradeReadinessAssessment? assessment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tracked);
        var existing = await dbContext.TrackedOpportunities
            .SingleOrDefaultAsync(item => item.Id == tracked.Id, cancellationToken);
        if (existing is null)
        {
            dbContext.TrackedOpportunities.Add(ToEntity(tracked));
        }
        else
        {
            ApplyLifecycleUpdate(existing, tracked);
        }

        if (assessment is not null)
        {
            await PersistAssessmentAsync(assessment, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task PersistAssessmentAsync(
        PreTradeReadinessAssessment assessment,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(assessment, JsonOptions);
        var existing = await dbContext.PreTradeReadinessAssessments
            .SingleOrDefaultAsync(item => item.Id == assessment.Id, cancellationToken);
        if (existing is null)
        {
            dbContext.PreTradeReadinessAssessments.Add(new PreTradeReadinessAssessmentEntity
            {
                Id = assessment.Id,
                TrackedOpportunityId = assessment.TrackedOpportunityId,
                PlanId = assessment.PlanId,
                OpportunityId = assessment.OpportunityId,
                Symbol = assessment.Symbol,
                SessionId = assessment.SessionId,
                AsOfUtc = assessment.AsOfUtc,
                EvaluatedAtUtc = assessment.EvaluatedAtUtc,
                State = assessment.State.ToString(),
                Reason = assessment.Reason.ToString(),
                ContextMode = assessment.ContextMode.ToString(),
                ReadinessDefinitionVersion = assessment.ReadinessDefinitionVersion,
                AssessmentJson = json
            });
            return;
        }

        var stored = JsonSerializer.Deserialize<PreTradeReadinessAssessment>(
            existing.AssessmentJson, JsonOptions);
        var storedMaterial = stored is null
            ? existing.AssessmentJson
            : JsonSerializer.Serialize(
                stored with { EvaluatedAtUtc = assessment.EvaluatedAtUtc }, JsonOptions);
        if (!string.Equals(storedMaterial, json, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Pre-trade readiness assessment '{assessment.Id}' is immutable and already has different values.");
        }
    }

    private static void ApplyLifecycleUpdate(TrackedOpportunityEntity existing, TrackedOpportunity tracked)
    {
        if (existing.PlanId != tracked.PlanId
            || existing.OpportunityId != tracked.OpportunityId
            || !string.Equals(existing.Symbol, tracked.Symbol, StringComparison.Ordinal)
            || !string.Equals(existing.SessionId, tracked.SessionId, StringComparison.Ordinal)
            || !string.Equals(existing.Direction, tracked.Direction.ToString(), StringComparison.Ordinal)
            || existing.SelectedAtUtc != tracked.SelectedAtUtc
            || existing.PlanAsOfUtc != tracked.PlanAsOfUtc
            || !string.Equals(
                existing.SelectionDefinitionVersion,
                tracked.SelectionDefinitionVersion,
                StringComparison.Ordinal)
            || !string.Equals(existing.SelectionReasonCode, tracked.SelectionReasonCode, StringComparison.Ordinal)
            || !string.Equals(existing.SelectionReason, tracked.SelectionReason, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Tracked opportunity '{tracked.Id}' selection fields are immutable and already have different values.");
        }

        var existingStatus = Enum.Parse<TrackedOpportunityStatus>(existing.Status);
        if (existingStatus != TrackedOpportunityStatus.Active && existingStatus != tracked.Status)
        {
            throw new InvalidOperationException(
                $"Tracked opportunity '{tracked.Id}' is terminal ({existingStatus}) and cannot transition to {tracked.Status}.");
        }

        if (existing.HasEstablishedEntryReady && !tracked.HasEstablishedEntryReady)
        {
            throw new InvalidOperationException(
                $"Tracked opportunity '{tracked.Id}' has an established entry-ready observation that cannot be rewritten.");
        }

        existing.Status = tracked.Status.ToString();
        existing.HasEstablishedEntryReady = tracked.HasEstablishedEntryReady;
        existing.TerminalAtUtc = tracked.TerminalAtUtc;
        existing.TerminalReasonCode = tracked.TerminalReasonCode;
        existing.TerminalReason = tracked.TerminalReason;
    }

    private static TrackedOpportunityEntity ToEntity(TrackedOpportunity tracked) => new()
    {
        Id = tracked.Id,
        PlanId = tracked.PlanId,
        OpportunityId = tracked.OpportunityId,
        Symbol = tracked.Symbol,
        SessionId = tracked.SessionId,
        Direction = tracked.Direction.ToString(),
        SelectedAtUtc = tracked.SelectedAtUtc,
        PlanAsOfUtc = tracked.PlanAsOfUtc,
        SelectionDefinitionVersion = tracked.SelectionDefinitionVersion,
        Status = tracked.Status.ToString(),
        SelectionReasonCode = tracked.SelectionReasonCode,
        SelectionReason = tracked.SelectionReason,
        HasEstablishedEntryReady = tracked.HasEstablishedEntryReady,
        TerminalAtUtc = tracked.TerminalAtUtc,
        TerminalReasonCode = tracked.TerminalReasonCode,
        TerminalReason = tracked.TerminalReason
    };

    private static TrackedOpportunity ToTracked(TrackedOpportunityEntity entity) => new(
        entity.Id,
        entity.PlanId,
        entity.OpportunityId,
        entity.Symbol,
        entity.SessionId,
        Enum.Parse<TradeDirection>(entity.Direction),
        entity.SelectedAtUtc,
        entity.PlanAsOfUtc,
        entity.SelectionDefinitionVersion,
        Enum.Parse<TrackedOpportunityStatus>(entity.Status),
        entity.SelectionReasonCode,
        entity.SelectionReason,
        entity.HasEstablishedEntryReady,
        entity.TerminalAtUtc,
        entity.TerminalReasonCode,
        entity.TerminalReason);
}
