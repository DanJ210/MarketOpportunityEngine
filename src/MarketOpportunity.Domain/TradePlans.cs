namespace MarketOpportunity.Domain;

public enum TradeDirection
{
    Long,
    Short
}

public enum TradePlanStatus
{
    InsufficientStructure,
    PlanForming,
    PlanDefined,
    Rejected
}

public sealed record PriceZone(decimal Low, decimal High);

public sealed record TradeTarget(
    decimal Price,
    Guid SourceSwingId,
    string OriginEventIdentity,
    string ConfirmationEventIdentity,
    string StructureAlgorithmVersion);

public sealed record TradePlanEvidence(
    string Code,
    string Description,
    Guid? SourceSwingId = null,
    decimal? Price = null,
    string? OriginEventIdentity = null,
    string? ConfirmationEventIdentity = null);

public sealed record TradePlan(
    Guid Id,
    string Symbol,
    string SessionId,
    DateTimeOffset AsOfUtc,
    DateTimeOffset ValidUntilUtc,
    TradeDirection? Direction,
    TradePlanStatus Status,
    PriceZone? EntryZone,
    decimal? EntryReferencePrice,
    decimal? InvalidationPrice,
    TradeTarget? Target1,
    TradeTarget? Target2,
    decimal? RiskPerShare,
    decimal? RewardPerShareToTarget1,
    decimal? RewardRiskRatioTarget1,
    string SetupType,
    Guid OpportunityId,
    string MarketStructureVersion,
    string FeatureSetVersion,
    string SetupDefinitionVersion,
    string ConfigurationVersion,
    string PlanDefinitionVersion,
    MarketDataCapabilities MarketData,
    IReadOnlyList<TradePlanEvidence> Evidence);
