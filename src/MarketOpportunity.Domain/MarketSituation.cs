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
    string? ContextDefinitionVersion);

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
