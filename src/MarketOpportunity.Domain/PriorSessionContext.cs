namespace MarketOpportunity.Domain;

public enum PriorSessionContextStatus
{
    Available,
    Partial,
    Unavailable,
    Invalid
}

public enum PriorSessionCloseSemantics
{
    Unavailable,
    LastEligibleTradeAtOrBeforeSessionEnd
}

public enum MarketSessionType
{
    Regular
}

public enum MarketSessionValidity
{
    Valid,
    Invalid
}

public enum MarketSessionCalendarStatus
{
    Success,
    NoPreviousSession,
    InvalidReference,
    LimitExceeded,
    Cancelled,
    AuthenticationFailure,
    EntitlementFailure,
    ProviderFailure,
    MalformedResponse
}

public enum HistoricalRetrievalStatus
{
    SuccessPaginationExhausted,
    LimitExceeded,
    Cancelled,
    AuthenticationFailure,
    EntitlementFailure,
    ProviderFailure,
    MalformedResponse
}

public sealed record MarketSession(
    string SessionId,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc);

public sealed record MarketSessionDefinition(
    string SessionId,
    DateOnly TradingDate,
    DateTimeOffset OpenAtUtc,
    DateTimeOffset CloseAtUtc,
    string Provider,
    string SourceKind,
    string CalendarVersion,
    MarketSessionType SessionType,
    MarketSessionValidity Validity)
{
    public DateTimeOffset StartUtc => OpenAtUtc;
    public DateTimeOffset EndUtc => CloseAtUtc;

    public MarketSession ToMarketSession() =>
        new(SessionId, OpenAtUtc, CloseAtUtc);
}

public sealed record HistoricalDataRequest(
    string Symbol,
    string SessionId,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc);

public sealed record HistoricalDataProvenance(
    string Provider,
    string Feed,
    string SourceKind,
    DateTimeOffset RetrievedAtUtc,
    int PagesRetrieved,
    int DuplicateRecordsIgnored);

public sealed record HistoricalMarketSessionData(
    IReadOnlyList<MarketEvent> Events,
    MarketDataCapabilities Capabilities,
    string Source,
    DateTimeOffset? CoverageStartUtc,
    DateTimeOffset? CoverageEndUtc,
    bool DeclaresCompleteCoverage,
    bool HasKnownGaps,
    HistoricalDataRequest? Request = null,
    HistoricalRetrievalStatus RetrievalStatus = HistoricalRetrievalStatus.SuccessPaginationExhausted,
    HistoricalDataProvenance? Provenance = null,
    bool PaginationExhausted = true,
    string? StatusMessage = null);

public sealed record PriorSessionContext(
    Guid ContextId,
    string Symbol,
    string CurrentSessionId,
    string? PriorSessionId,
    DateTimeOffset? PriorSessionStartUtc,
    DateTimeOffset? PriorSessionEndUtc,
    decimal? PriorSessionHigh,
    decimal? PriorSessionLow,
    decimal? PriorSessionClose,
    DateTimeOffset? PriorSessionCloseEventTimeUtc,
    PriorSessionCloseSemantics CloseSemantics,
    decimal? ObservedHigh,
    decimal? ObservedLow,
    decimal? LastObservedTrade,
    DateTimeOffset? LastObservedTradeEventTimeUtc,
    ConfirmedSwing? LatestConfirmedSwingHigh,
    ConfirmedSwing? LatestConfirmedSwingLow,
    ConfirmedSwing? PreviousConfirmedSwingHigh,
    ConfirmedSwing? PreviousConfirmedSwingLow,
    MarketStructureState StructureState,
    IReadOnlyList<StructuralLevelCandidate> SupportCandidates,
    IReadOnlyList<StructuralLevelCandidate> ResistanceCandidates,
    PriorSessionContextStatus Completeness,
    string CompletenessReason,
    MarketDataCapabilities? MarketData,
    string? HistoricalSource,
    DateTimeOffset? CoverageStartUtc,
    DateTimeOffset? CoverageEndUtc,
    bool IsSessionStartCovered,
    bool IsSessionEndCovered,
    bool HasKnownGaps,
    string ContextDefinitionVersion);

public sealed record PriceDistance(
    decimal ReferencePrice,
    decimal LevelPrice,
    decimal AbsolutePriceDistance,
    decimal? PercentDistance);

public sealed record PriorSessionContextView(
    PriorSessionContext Context,
    decimal? CurrentReferencePrice,
    PriceDistance? DistanceToPriorHigh,
    PriceDistance? DistanceToPriorLow,
    PriceDistance? DistanceToObservedHigh,
    PriceDistance? DistanceToObservedLow,
    PriceDistance? DistanceToLatestSwingHigh,
    PriceDistance? DistanceToLatestSwingLow,
    StructuralLevelCandidate? NearestSupport,
    PriceDistance? DistanceToNearestSupport,
    StructuralLevelCandidate? NearestResistance,
    PriceDistance? DistanceToNearestResistance);
