using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MarketOpportunity.Domain;

namespace MarketOpportunity.Application;

public sealed record MarketSituationReadModel(
    Guid Id,
    string? CandidateSetId,
    Guid FeatureSnapshotId,
    Guid? PriorSessionContextId,
    string Symbol,
    string SessionId,
    DateTimeOffset AsOfUtc,
    DateTimeOffset EvaluatedAtUtc,
    decimal? CurrentReferencePrice,
    MarketSituationStatus Status,
    string? UnavailableReason,
    MarketSituationContextMode ContextMode,
    ImportantPriceLevel? NearestSupportLevel,
    ImportantPriceLevel? NearestResistanceLevel,
    MarketSituationInteraction? SupportInteraction,
    MarketSituationInteraction? ResistanceInteraction,
    MarketSituationPrimary? PrimarySituation,
    IReadOnlyList<MarketSituationSelectionEvidence> SelectionEvidence,
    int SupportCandidateCount,
    int ResistanceCandidateCount,
    int TotalCandidateCount,
    decimal TestZonePercent,
    decimal ApproachDistancePercent,
    string DefinitionVersion,
    string FeatureSetVersion,
    string ConfigurationVersion,
    MarketDataCapabilities MarketData,
    DataQualityStatus DataQuality,
    MarketStructureStatus MarketStructureStatus,
    bool HasCompleteCurrentSessionData,
    string PersistenceContractVersion,
    bool IsLegacyEvidence);

public static class MarketSituationReadModelFactory
{
    public const int MaximumNormalSerializedBytes = 32 * 1024;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };

    public static MarketSituationReadModel From(
        CompactMarketSituationAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        var support = assessment.SupportInteraction?.WithLevel(
            assessment.NearestSupportLevel
            ?? throw new InvalidOperationException(
                $"Compact MarketSituation assessment '{assessment.Id}' has no selected support level."));
        var resistance = assessment.ResistanceInteraction?.WithLevel(
            assessment.NearestResistanceLevel
            ?? throw new InvalidOperationException(
                $"Compact MarketSituation assessment '{assessment.Id}' has no selected resistance level."));
        var result = new MarketSituationReadModel(
            assessment.Id,
            assessment.CandidateSetId,
            assessment.FeatureSnapshotId,
            assessment.PriorSessionContextId,
            assessment.Symbol,
            assessment.SessionId,
            assessment.AsOfUtc,
            assessment.EvaluatedAtUtc,
            assessment.CurrentReferencePrice,
            assessment.Status,
            assessment.UnavailableReason,
            assessment.ContextMode,
            assessment.NearestSupportLevel,
            assessment.NearestResistanceLevel,
            support,
            resistance,
            assessment.PrimarySituation,
            assessment.SelectionEvidence,
            assessment.SupportCandidateCount,
            assessment.ResistanceCandidateCount,
            assessment.TotalCandidateCount,
            assessment.TestZonePercent,
            assessment.ApproachDistancePercent,
            assessment.DefinitionVersion,
            assessment.FeatureSetVersion,
            assessment.ConfigurationVersion,
            assessment.MarketData,
            assessment.DataQuality,
            assessment.MarketStructureStatus,
            assessment.HasCompleteCurrentSessionData,
            assessment.PersistenceContractVersion,
            false);
        EnsureBounded(result);
        return result;
    }

    public static MarketSituationReadModel FromLegacy(
        MarketSituationAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        var result = new MarketSituationReadModel(
            assessment.Id,
            null,
            assessment.FeatureSnapshotId,
            assessment.PriorSessionContextId,
            assessment.Symbol,
            assessment.SessionId,
            assessment.AsOfUtc,
            assessment.EvaluatedAtUtc,
            assessment.CurrentReferencePrice,
            assessment.Status,
            assessment.UnavailableReason,
            assessment.ContextMode,
            assessment.NearestSupportLevel,
            assessment.NearestResistanceLevel,
            assessment.SupportInteraction,
            assessment.ResistanceInteraction,
            assessment.PrimarySituation,
            [],
            assessment.EligibleLevels.Count(
                item => item.Role == ImportantPriceLevelRole.Support),
            assessment.EligibleLevels.Count(
                item => item.Role == ImportantPriceLevelRole.Resistance),
            assessment.EligibleLevels.Count,
            assessment.TestZonePercent,
            assessment.ApproachDistancePercent,
            assessment.DefinitionVersion,
            assessment.FeatureSetVersion,
            assessment.ConfigurationVersion,
            assessment.MarketData,
            assessment.DataQuality,
            assessment.MarketStructureStatus,
            assessment.HasCompleteCurrentSessionData,
            "market-situation-persistence-v1",
            true);
        EnsureBounded(result);
        return result;
    }

    public static int SerializedBytes(MarketSituationReadModel readModel) =>
        Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(readModel, JsonOptions));

    private static void EnsureBounded(MarketSituationReadModel readModel)
    {
        var bytes = SerializedBytes(readModel);
        if (bytes > MaximumNormalSerializedBytes)
        {
            throw new InvalidOperationException(
                $"Compact MarketSituation read model is {bytes} bytes; the operational limit is " +
                $"{MaximumNormalSerializedBytes} bytes.");
        }
    }
}
