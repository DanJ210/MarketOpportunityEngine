namespace MarketOpportunity.Domain;

public enum ConfirmedSwingType
{
    ConfirmedSwingHigh,
    ConfirmedSwingLow
}

public enum MarketStructureStatus
{
    NotComputed,
    Insufficient,
    InvalidPriceData,
    Available
}

public enum MarketStructureState
{
    NotComputed,
    Insufficient,
    Bullish,
    Bearish,
    Mixed,
    Range
}

public enum SwingRelationship
{
    NotComputed,
    Insufficient,
    Higher,
    Lower,
    Equal
}

public enum StructuralLevelType
{
    Support,
    Resistance
}

public sealed record ConfirmedSwing(
    Guid Id,
    string Symbol,
    string SessionId,
    decimal Price,
    DateTimeOffset EventTimeUtc,
    DateTimeOffset ConfirmedAtEventTimeUtc,
    string OriginEventIdentity,
    string ConfirmationEventIdentity,
    ConfirmedSwingType Type,
    string FeatureSetVersion,
    string AlgorithmVersion);

public sealed record StructuralLevelCandidate(
    Guid Id,
    StructuralLevelType Type,
    string Symbol,
    string SessionId,
    decimal Price,
    DateTimeOffset EventTimeUtc,
    DateTimeOffset ConfirmedAtEventTimeUtc,
    Guid SourceSwingId,
    string OriginEventIdentity,
    string ConfirmationEventIdentity,
    string FeatureSetVersion,
    string AlgorithmVersion);
