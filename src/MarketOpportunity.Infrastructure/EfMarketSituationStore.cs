using System.Text.Json;
using MarketOpportunity.Application;
using MarketOpportunity.Domain;
using Microsoft.EntityFrameworkCore;

namespace MarketOpportunity.Infrastructure;

/// <summary>
/// Persists immutable market-situation history. Identical replay is accepted; a conflicting
/// material payload for an existing deterministic assessment identity fails.
/// </summary>
public sealed class EfMarketSituationStore(MarketOpportunityDbContext dbContext) : IMarketSituationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task PersistAsync(
        MarketSituationAssessment assessment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        var json = JsonSerializer.Serialize(assessment, JsonOptions);
        var existing = await dbContext.MarketSituationAssessments
            .SingleOrDefaultAsync(item => item.Id == assessment.Id, cancellationToken);
        if (existing is null)
        {
            dbContext.MarketSituationAssessments.Add(new MarketSituationAssessmentEntity
            {
                Id = assessment.Id,
                FeatureSnapshotId = assessment.FeatureSnapshotId,
                PriorSessionContextId = assessment.PriorSessionContextId,
                Symbol = assessment.Symbol,
                SessionId = assessment.SessionId,
                AsOfUtc = assessment.AsOfUtc,
                EvaluatedAtUtc = assessment.EvaluatedAtUtc,
                Status = assessment.Status.ToString(),
                ContextMode = assessment.ContextMode.ToString(),
                PrimarySituationType = assessment.PrimarySituation?.SituationType.ToString(),
                SupportSituationType = assessment.SupportInteraction?.SituationType.ToString(),
                ResistanceSituationType = assessment.ResistanceInteraction?.SituationType.ToString(),
                SupportLevelSource = assessment.SupportInteraction?.Level.Source.ToString(),
                ResistanceLevelSource = assessment.ResistanceInteraction?.Level.Source.ToString(),
                DefinitionVersion = assessment.DefinitionVersion,
                ConfigurationVersion = assessment.ConfigurationVersion,
                AssessmentJson = json
            });
        }
        else
        {
            var stored = JsonSerializer.Deserialize<MarketSituationAssessment>(
                existing.AssessmentJson,
                JsonOptions);
            var storedMaterial = stored is null
                ? existing.AssessmentJson
                : JsonSerializer.Serialize(
                    stored with { EvaluatedAtUtc = assessment.EvaluatedAtUtc },
                    JsonOptions);
            if (!string.Equals(storedMaterial, json, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Market situation assessment '{assessment.Id}' is immutable and already has different values.");
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
