namespace MarketOpportunity.Domain;

public enum MarketReactionType
{
    SupportHeld,
    SupportBreakdown,
    SupportReclaimed,
    ResistanceRejected,
    ResistanceBreakout,
    ResistanceFailedBreakout
}

public enum MarketReactionEpisodeStatus
{
    Active,
    Confirmed,
    Expired
}

public enum MarketReactionStatus
{
    Unresolved,
    Confirmed,
    UnresolvedExpired
}

public sealed record LevelInteractionEpisode(
    Guid Id,
    Guid LevelId,
    string Symbol,
    string SessionId,
    ImportantPriceLevelRole Role,
    MarketSituationType InitiatingSituation,
    decimal LevelPrice,
    decimal ZoneLow,
    decimal ZoneHigh,
    DateTimeOffset StartedAtUtc,
    string StartObservationIdentity,
    string StartObservationProvider,
    string? StartObservationSequenceId,
    DateTimeOffset ExpiresAtUtc,
    decimal ConfirmationDistancePercent,
    int MaxEpisodeDurationSeconds,
    string DefinitionVersion,
    string ConfigurationVersion,
    bool HasCrossedOppositeZone,
    MarketReactionEpisodeStatus Status,
    MarketReactionType? Reaction,
    DateTimeOffset? ReactionAtUtc,
    string? ReactionEventIdentity,
    decimal? ReactionPrice,
    DateTimeOffset? LastObservationAtUtc,
    string? LastObservationIdentity,
    string? LastObservationProvider,
    string? LastObservationSequenceId,
    decimal? LastObservationPrice);

public sealed record MarketReactionAssessment(
    Guid Id,
    Guid EpisodeId,
    Guid LevelId,
    string Symbol,
    string SessionId,
    DateTimeOffset AsOfUtc,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    MarketReactionStatus Status,
    MarketReactionType? Reaction,
    DateTimeOffset? ReactionAtUtc,
    string? ReactionEventIdentity,
    decimal? ReactionPrice,
    DateTimeOffset? LastObservationAtUtc,
    string? LastObservationIdentity,
    decimal? LastObservationPrice,
    bool HasCrossedOppositeZone,
    decimal ConfirmationDistancePercent,
    int MaxEpisodeDurationSeconds,
    string DefinitionVersion,
    string ConfigurationVersion);

public sealed record MarketReactionEvaluation(
    LevelInteractionEpisode Episode,
    MarketReactionAssessment Assessment);

/// <summary>
/// Provisional development policy for mechanical, event-time reaction confirmation. Defaults
/// are engineering choices only; they are not market calibrated and make no profitability claim.
/// </summary>
public sealed record MarketReactionConfiguration
{
    /// <summary>
    /// Provisional, not market calibrated. Confirmation must clear the pinned zone edge by this
    /// additional percentage of the edge price.
    /// </summary>
    public decimal ConfirmationDistancePercent { get; init; } = 0.10m;

    /// <summary>
    /// Provisional, not market calibrated. Unresolved episodes expire by event time after this
    /// many seconds.
    /// </summary>
    public int MaxEpisodeDurationSeconds { get; init; } = 300;
}
