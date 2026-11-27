using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MarketOpportunity.Domain;

public enum ImportantPriceLevelRole
{
    Support,
    Resistance
}

public enum ImportantPriceLevelSource
{
    PriorSwingHigh,
    PriorSwingLow,
    PriorSessionHigh,
    PriorSessionLow,
    CurrentSwingHigh,
    CurrentSwingLow
}

public enum MarketSituationContextMode
{
    CurrentSessionOnly,
    PriorSessionEnhanced
}

public enum MarketSituationStatus
{
    Available,
    NoEligibleLevels,
    NotComputable
}

public enum MarketSituationType
{
    ApproachingSupport,
    TestingSupport,
    CrossedSupport,
    ApproachingResistance,
    TestingResistance,
    CrossedResistance
}

public enum MarketSituationSide
{
    Support,
    Resistance
}

public sealed record ImportantPriceLevel(
    Guid Id,
    string Symbol,
    string CurrentSessionId,
    string SourceSessionId,
    decimal Price,
    ImportantPriceLevelRole Role,
    ImportantPriceLevelSource Source,
    Guid SourceStructuralIdentity,
    Guid? SourceContextId,
    string? SourceEventIdentity,
    string? ConfirmationEventIdentity,
    DateTimeOffset? SourceEventTimeUtc,
    DateTimeOffset? ConfirmedAtEventTimeUtc,
    MarketSituationContextMode SourceContextMode,
    string SourceCompleteness,
    string? HistoricalSource,
    DateTimeOffset? SourceCoverageStartUtc,
    DateTimeOffset? SourceCoverageEndUtc,
    string? StructureAlgorithmVersion,
    string? FeatureSetVersion,
    string? ContextDefinitionVersion,
    string? HistoricalProvider = null,
    string? HistoricalFeed = null);

public sealed record MarketSituationInteraction(
    MarketSituationType SituationType,
    ImportantPriceLevel Level,
    decimal ZoneLow,
    decimal ZoneHigh,
    decimal SignedDistanceFromLevel,
    decimal AbsoluteDistanceFromLevel,
    decimal PercentDistanceFromReference,
    decimal DistanceToActiveZone,
    decimal DistanceToActiveZonePercent);

public sealed record MarketSituationPrimary(
    MarketSituationSide Side,
    MarketSituationType SituationType,
    Guid LevelId,
    decimal DistanceToActiveZone);

public sealed record MarketSituationAssessment(
    Guid Id,
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
    IReadOnlyList<ImportantPriceLevel> EligibleLevels,
    decimal TestZonePercent,
    decimal ApproachDistancePercent,
    string DefinitionVersion,
    string FeatureSetVersion,
    string ConfigurationVersion,
    MarketDataCapabilities MarketData,
    DataQualityStatus DataQuality,
    MarketStructureStatus MarketStructureStatus,
    bool HasCompleteCurrentSessionData);

public sealed record CompactMarketSituationInteraction(
    MarketSituationType SituationType,
    Guid LevelId,
    decimal ZoneLow,
    decimal ZoneHigh,
    decimal SignedDistanceFromLevel,
    decimal AbsoluteDistanceFromLevel,
    decimal PercentDistanceFromReference,
    decimal DistanceToActiveZone,
    decimal DistanceToActiveZonePercent)
{
    public MarketSituationInteraction WithLevel(ImportantPriceLevel level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (level.Id != LevelId)
        {
            throw new InvalidOperationException(
                $"Compact MarketSituation interaction '{LevelId}' cannot use level '{level.Id}'.");
        }

        return new MarketSituationInteraction(
            SituationType,
            level,
            ZoneLow,
            ZoneHigh,
            SignedDistanceFromLevel,
            AbsoluteDistanceFromLevel,
            PercentDistanceFromReference,
            DistanceToActiveZone,
            DistanceToActiveZonePercent);
    }

    public static CompactMarketSituationInteraction From(MarketSituationInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        return new CompactMarketSituationInteraction(
            interaction.SituationType,
            interaction.Level.Id,
            interaction.ZoneLow,
            interaction.ZoneHigh,
            interaction.SignedDistanceFromLevel,
            interaction.AbsoluteDistanceFromLevel,
            interaction.PercentDistanceFromReference,
            interaction.DistanceToActiveZone,
            interaction.DistanceToActiveZonePercent);
    }
}

public sealed record MarketSituationSelectionEvidence(
    ImportantPriceLevelRole Role,
    Guid LevelId,
    decimal DistanceFromReferencePrice,
    int SourcePrecedence,
    string SelectionRule);

public sealed record MarketSituationCandidateSet(
    string CandidateSetId,
    Guid FeatureSnapshotId,
    Guid? PriorSessionContextId,
    string Symbol,
    string SessionId,
    DateTimeOffset AsOfUtc,
    MarketSituationContextMode ContextMode,
    IReadOnlyList<ImportantPriceLevel> EligibleLevels,
    decimal TestZonePercent,
    decimal ApproachDistancePercent,
    string DefinitionVersion,
    string FeatureSetVersion,
    string ConfigurationVersion,
    MarketDataCapabilities MarketData,
    DataQualityStatus DataQuality,
    MarketStructureStatus MarketStructureStatus,
    bool HasCompleteCurrentSessionData)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public const string CurrentCanonicalizationVersion = "market-situation-candidate-set-v1";

    public string CanonicalizationVersion { get; init; } = CurrentCanonicalizationVersion;

    public int SupportCandidateCount =>
        EligibleLevels.Count(item => item.Role == ImportantPriceLevelRole.Support);

    public int ResistanceCandidateCount =>
        EligibleLevels.Count(item => item.Role == ImportantPriceLevelRole.Resistance);

    public int TotalCandidateCount => EligibleLevels.Count;

    public void EnsureIdentityMatches()
    {
        var expected = ComputeId(
            FeatureSnapshotId,
            PriorSessionContextId,
            Symbol,
            SessionId,
            AsOfUtc,
            ContextMode,
            Canonicalize(EligibleLevels),
            TestZonePercent,
            ApproachDistancePercent,
            DefinitionVersion,
            FeatureSetVersion,
            ConfigurationVersion,
            MarketData,
            DataQuality,
            MarketStructureStatus,
            HasCompleteCurrentSessionData);
        if (!string.Equals(CanonicalizationVersion, CurrentCanonicalizationVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unsupported MarketSituation candidate canonicalization '{CanonicalizationVersion}'.");
        }
        if (!string.Equals(CandidateSetId, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"MarketSituation candidate set '{CandidateSetId}' has conflicting canonical content.");
        }
    }

    public static MarketSituationCandidateSet Create(
        Guid featureSnapshotId,
        Guid? priorSessionContextId,
        string symbol,
        string sessionId,
        DateTimeOffset asOfUtc,
        MarketSituationContextMode contextMode,
        IReadOnlyList<ImportantPriceLevel> eligibleLevels,
        decimal testZonePercent,
        decimal approachDistancePercent,
        string definitionVersion,
        string featureSetVersion,
        string configurationVersion,
        MarketDataCapabilities marketData,
        DataQualityStatus dataQuality,
        MarketStructureStatus marketStructureStatus,
        bool hasCompleteCurrentSessionData)
    {
        ArgumentNullException.ThrowIfNull(eligibleLevels);
        var canonicalLevels = Canonicalize(eligibleLevels);
        var candidateSetId = ComputeId(
            featureSnapshotId,
            priorSessionContextId,
            symbol,
            sessionId,
            asOfUtc,
            contextMode,
            canonicalLevels,
            testZonePercent,
            approachDistancePercent,
            definitionVersion,
            featureSetVersion,
            configurationVersion,
            marketData,
            dataQuality,
            marketStructureStatus,
            hasCompleteCurrentSessionData);
        return new MarketSituationCandidateSet(
            candidateSetId,
            featureSnapshotId,
            priorSessionContextId,
            symbol,
            sessionId,
            asOfUtc.ToUniversalTime(),
            contextMode,
            canonicalLevels,
            testZonePercent,
            approachDistancePercent,
            definitionVersion,
            featureSetVersion,
            configurationVersion,
            marketData,
            dataQuality,
            marketStructureStatus,
            hasCompleteCurrentSessionData);
    }

    public static IReadOnlyList<ImportantPriceLevel> Canonicalize(
        IReadOnlyList<ImportantPriceLevel> eligibleLevels)
    {
        ArgumentNullException.ThrowIfNull(eligibleLevels);
        return eligibleLevels
            .OrderBy(item => item.Role)
            .ThenBy(item => item.Price)
            .ThenBy(item => ImportantPriceLevelIdentity.SourcePrecedence(item.Source))
            .ThenBy(item => item.Id)
            .ThenBy(item => JsonSerializer.Serialize(item, JsonOptions), StringComparer.Ordinal)
            .ToArray();
    }

    private static string ComputeId(
        Guid featureSnapshotId,
        Guid? priorSessionContextId,
        string symbol,
        string sessionId,
        DateTimeOffset asOfUtc,
        MarketSituationContextMode contextMode,
        IReadOnlyList<ImportantPriceLevel> eligibleLevels,
        decimal testZonePercent,
        decimal approachDistancePercent,
        string definitionVersion,
        string featureSetVersion,
        string configurationVersion,
        MarketDataCapabilities marketData,
        DataQualityStatus dataQuality,
        MarketStructureStatus marketStructureStatus,
        bool hasCompleteCurrentSessionData)
    {
        var canonical = new
        {
            CanonicalizationVersion = CurrentCanonicalizationVersion,
            featureSnapshotId,
            priorSessionContextId,
            symbol,
            sessionId,
            AsOfUtc = asOfUtc.ToUniversalTime(),
            contextMode,
            testZonePercent,
            approachDistancePercent,
            definitionVersion,
            featureSetVersion,
            configurationVersion,
            marketData,
            dataQuality,
            marketStructureStatus,
            hasCompleteCurrentSessionData
        };
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, JsonSerializer.Serialize(canonical, JsonOptions));
        foreach (var level in eligibleLevels)
        {
            Append(hash, JsonSerializer.Serialize(level, JsonOptions));
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}

public sealed record CompactMarketSituationAssessment(
    Guid Id,
    string CandidateSetId,
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
    CompactMarketSituationInteraction? SupportInteraction,
    CompactMarketSituationInteraction? ResistanceInteraction,
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
    bool HasCompleteCurrentSessionData)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public const string CurrentPersistenceContractVersion = "market-situation-persistence-v2";

    public string PersistenceContractVersion { get; init; } = CurrentPersistenceContractVersion;

    public MarketSituationAssessment ProjectLegacy(MarketSituationCandidateSet candidateSet)
    {
        ArgumentNullException.ThrowIfNull(candidateSet);
        candidateSet.EnsureIdentityMatches();
        EnsureIdentityMatches();
        EnsureCandidateSetMatches(candidateSet);
        if (!string.Equals(CandidateSetId, candidateSet.CandidateSetId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Compact MarketSituation assessment references candidate set '{CandidateSetId}', not '{candidateSet.CandidateSetId}'.");
        }

        EnsureSelectedLevel(candidateSet, NearestSupportLevel);
        EnsureSelectedLevel(candidateSet, NearestResistanceLevel);
        var support = SupportInteraction is null
            ? null
            : SupportInteraction.WithLevel(NearestSupportLevel
                ?? throw new InvalidOperationException(
                    "Compact support interaction requires direct selected-level provenance."));
        var resistance = ResistanceInteraction is null
            ? null
            : ResistanceInteraction.WithLevel(NearestResistanceLevel
                ?? throw new InvalidOperationException(
                    "Compact resistance interaction requires direct selected-level provenance."));

        return new MarketSituationAssessment(
            Id,
            FeatureSnapshotId,
            PriorSessionContextId,
            Symbol,
            SessionId,
            AsOfUtc,
            EvaluatedAtUtc,
            CurrentReferencePrice,
            Status,
            UnavailableReason,
            ContextMode,
            NearestSupportLevel,
            NearestResistanceLevel,
            support,
            resistance,
            PrimarySituation,
            candidateSet.EligibleLevels,
            TestZonePercent,
            ApproachDistancePercent,
            DefinitionVersion,
            FeatureSetVersion,
            ConfigurationVersion,
            MarketData,
            DataQuality,
            MarketStructureStatus,
            HasCompleteCurrentSessionData);
    }

    private void EnsureCandidateSetMatches(MarketSituationCandidateSet candidateSet)
    {
        if (!string.Equals(CandidateSetId, candidateSet.CandidateSetId, StringComparison.Ordinal)
            || FeatureSnapshotId != candidateSet.FeatureSnapshotId
            || PriorSessionContextId != candidateSet.PriorSessionContextId
            || !Symbol.Equals(candidateSet.Symbol, StringComparison.Ordinal)
            || !SessionId.Equals(candidateSet.SessionId, StringComparison.Ordinal)
            || AsOfUtc != candidateSet.AsOfUtc
            || ContextMode != candidateSet.ContextMode
            || TestZonePercent != candidateSet.TestZonePercent
            || ApproachDistancePercent != candidateSet.ApproachDistancePercent
            || !DefinitionVersion.Equals(candidateSet.DefinitionVersion, StringComparison.Ordinal)
            || !FeatureSetVersion.Equals(candidateSet.FeatureSetVersion, StringComparison.Ordinal)
            || !ConfigurationVersion.Equals(candidateSet.ConfigurationVersion, StringComparison.Ordinal)
            || MarketData != candidateSet.MarketData
            || DataQuality != candidateSet.DataQuality
            || MarketStructureStatus != candidateSet.MarketStructureStatus
            || HasCompleteCurrentSessionData != candidateSet.HasCompleteCurrentSessionData
            || SupportCandidateCount != candidateSet.SupportCandidateCount
            || ResistanceCandidateCount != candidateSet.ResistanceCandidateCount
            || TotalCandidateCount != candidateSet.TotalCandidateCount)
        {
            throw new InvalidOperationException(
                $"Compact MarketSituation assessment '{Id}' has conflicting candidate-set metadata.");
        }
    }

    private void EnsureIdentityMatches()
    {
        if (!string.Equals(
                PersistenceContractVersion,
                CurrentPersistenceContractVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unsupported MarketSituation persistence contract '{PersistenceContractVersion}'.");
        }

        var material = new
        {
            FeatureSnapshotId,
            PriorSessionContextId,
            CandidateSetId,
            Symbol,
            SessionId,
            AsOfUtc,
            CurrentReferencePrice,
            Status,
            UnavailableReason,
            ContextMode,
            NearestSupportLevel,
            NearestResistanceLevel,
            SupportInteraction,
            ResistanceInteraction,
            PrimarySituation,
            SelectionEvidence,
            SupportCandidateCount,
            ResistanceCandidateCount,
            TotalCandidateCount,
            TestZonePercent,
            ApproachDistancePercent,
            DefinitionVersion,
            FeatureSetVersion,
            ConfigurationVersion,
            MarketData,
            DataQuality,
            MarketStructureStatus,
            HasCompleteCurrentSessionData
        };
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(material, JsonOptions)));
        var expected = new Guid(hash.AsSpan(0, 16));
        if (Id != expected)
        {
            throw new InvalidOperationException(
                $"Compact MarketSituation assessment '{Id}' has conflicting analytical content.");
        }
    }

    private static void EnsureSelectedLevel(
        MarketSituationCandidateSet candidateSet,
        ImportantPriceLevel? selectedLevel)
    {
        if (selectedLevel is not null && !candidateSet.EligibleLevels.Contains(selectedLevel))
        {
            throw new InvalidOperationException(
                $"Selected MarketSituation level '{selectedLevel.Id}' is not canonical candidate-set evidence.");
        }
    }
}

public sealed record MarketSituationEvaluation(
    MarketSituationCandidateSet CandidateSet,
    CompactMarketSituationAssessment Assessment)
{
    public MarketSituationAssessment ProjectLegacy() =>
        Assessment.ProjectLegacy(CandidateSet);
}

public static class ImportantPriceLevelIdentity
{
    public static int SourcePrecedence(ImportantPriceLevelSource source) => source switch
    {
        ImportantPriceLevelSource.PriorSwingHigh or ImportantPriceLevelSource.PriorSwingLow => 0,
        ImportantPriceLevelSource.CurrentSwingHigh or ImportantPriceLevelSource.CurrentSwingLow => 1,
        ImportantPriceLevelSource.PriorSessionHigh or ImportantPriceLevelSource.PriorSessionLow => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown important price level source.")
    };
}

public sealed record MarketSituationConfiguration
{
    /// <summary>
    /// Provisional development value, not market calibrated and making no profitability claim.
    /// </summary>
    public decimal TestZonePercent { get; init; } = 0.10m;

    /// <summary>
    /// Provisional development value, not market calibrated and making no profitability claim.
    /// The same distance bounds approach and crossed-level relevance from a test-zone edge.
    /// </summary>
    public decimal ApproachDistancePercent { get; init; } = 0.25m;
}
