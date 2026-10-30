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
