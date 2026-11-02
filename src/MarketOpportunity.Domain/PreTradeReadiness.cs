namespace MarketOpportunity.Domain;

/// <summary>
/// Lifecycle of a tracked candidate. <see cref="Active"/> is the only non-terminal value;
/// every other value is terminal and the tracked candidate is never reactivated or replaced in place.
/// </summary>
public enum TrackedOpportunityStatus
{
    Active,
    Expired,
    Invalidated,
    TargetReachedBeforeEntry,
    ExtendedBeyondEntry,
    SessionEnded,
    PlanUnavailable
}

/// <summary>
/// Pins one immutable TradePlan for a symbol/session so readiness observes later market state
/// against stable geometry. References the immutable plan by identity; it does not copy plan geometry.
/// Only <see cref="Status"/>, <see cref="HasEstablishedEntryReady"/>, and the terminal fields may change
/// after selection; every other field is immutable.
/// </summary>
public sealed record TrackedOpportunity(
    Guid Id,
    Guid PlanId,
    Guid OpportunityId,
    string Symbol,
    string SessionId,
    TradeDirection Direction,
    DateTimeOffset SelectedAtUtc,
    DateTimeOffset PlanAsOfUtc,
    string SelectionDefinitionVersion,
    TrackedOpportunityStatus Status,
    string SelectionReasonCode,
    string SelectionReason,
    bool HasEstablishedEntryReady,
    DateTimeOffset? TerminalAtUtc,
    string? TerminalReasonCode,
    string? TerminalReason);

/// <summary>
/// Market-condition readiness around a tracked candidate opportunity.
/// These states describe market conditions, never the user's holdings or executions.
/// </summary>
public enum PreTradeReadinessState
{
    Unavailable,
    Suspended,
    Watching,
    SetupForming,
    EntryZoneActive,
    EntryReady,
    NoLongerActionable
}

public enum PreTradeReadinessReason
{
    None,

    NoTrackedOpportunity,
    PlanUnavailable,
    PlanNotDefined,
    DirectionUnavailable,
    DirectionMismatch,

    PublicationNotActive,
    FeedNotLive,
    DataGapUnresolved,
    DataIncomplete,
    InvalidCurrentPrice,
    InvalidQuote,
    SpreadTooWide,

    OpportunityExpired,
    InvalidationCrossed,
    TargetReachedBeforeEntry,
    ExtendedBeyondEntry,
    SessionChanged,

    ConfirmationNotAligned,
    ApproachingEntry,
    InsideEntryZone
}

/// <summary>
/// Which prior-session context participated in a readiness assessment.
/// <see cref="PriorSessionEnhanced"/> requires complete prior-session context;
/// partial, unavailable, or invalid prior context falls back to <see cref="CurrentSessionOnly"/>.
/// </summary>
public enum ReadinessContextMode
{
    CurrentSessionOnly,
    PriorSessionEnhanced
}

public sealed record PreTradeReadinessEvidence(string Code, string Description);

/// <summary>
/// Immutable, versioned readiness assessment of a tracked candidate against market state at
/// <paramref name="AsOfUtc"/>. Geometry depends only on market event time; <paramref name="EvaluatedAtUtc"/>
/// records audit/publication timing separately and does not participate in deterministic identity.
/// </summary>
public sealed record PreTradeReadinessAssessment(
    Guid Id,
    Guid TrackedOpportunityId,
    Guid PlanId,
    Guid OpportunityId,
    string Symbol,
    string SessionId,
    DateTimeOffset AsOfUtc,
    DateTimeOffset EvaluatedAtUtc,
    PreTradeReadinessState State,
    PreTradeReadinessReason Reason,
    TradeDirection Direction,
    ReadinessContextMode ContextMode,
    decimal? CurrentReferencePrice,
    decimal? DistanceToEntry,
    decimal? DistanceToEntryRiskMultiple,
    bool IsInsideEntryZone,
    string ReadinessDefinitionVersion,
    string FeatureSetVersion,
    string ConfigurationVersion,
    MarketDataCapabilities MarketData,
    IReadOnlyList<PreTradeReadinessEvidence> Evidence);
