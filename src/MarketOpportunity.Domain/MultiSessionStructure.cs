namespace MarketOpportunity.Domain;

public enum StructuralRole
{
    Support,
    Resistance
}

public enum StructuralObservationKind
{
    CompleteSessionHigh,
    CompleteSessionLow,
    RecurringConfirmedSwingHigh,
    RecurringConfirmedSwingLow
}

public enum StructuralEligibilityReason
{
    CompleteSessionExtreme,
    RecurringConfirmedSwingAcrossDistinctSessions
}

public enum StructuralSessionCompleteness
{
    Available,
    Insufficient,
    Partial,
    Invalid
}

public enum StructuralIntervalTrend
{
    Bullish,
    Bearish,
    Range,
    Conflicting
}

public enum MultiSessionStructuralTrendState
{
    Bullish,
    Bearish,
    Range,
    Conflicting,
    Transitioning,
    Insufficient
}

public enum StructuralAssessmentCompleteness
{
    Available,
    Insufficient,
    Partial,
    Invalid
}

public sealed record StructuralPivotCandidate(
    Guid SourceSwingId,
    string Symbol,
    string SessionId,
    StructuralRole Role,
    decimal Price,
    DateTimeOffset CandidateEventTimeUtc,
    DateTimeOffset? ConfirmedAtEventTimeUtc,
    string OriginEventIdentity,
    string? ConfirmationEventIdentity,
    string FeatureSetVersion,
    string AlgorithmVersion);

public sealed record StructuralSessionSummary(
    string Symbol,
    string SessionId,
    string? PreviousSessionId,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    StructuralSessionCompleteness Completeness,
    bool DeclaresCompleteCoverage,
    bool HasKnownGaps,
    decimal? High,
    decimal? Low,
    string? HighSourceEventIdentity,
    string? LowSourceEventIdentity,
    DateTimeOffset? HighEventTimeUtc,
    DateTimeOffset? LowEventTimeUtc,
    DateTimeOffset? EvidenceKnownAtUtc,
    string HistoricalProvider,
    string HistoricalFeed,
    string HistoricalSourceKind,
    IReadOnlyList<StructuralPivotCandidate> ConfirmablePivots);

public sealed record StructuralAssessmentInput(
    string Symbol,
    string CurrentSessionId,
    string ImmediatePriorSessionId,
    DateTimeOffset AssessmentAsOfUtc,
    IReadOnlyList<StructuralSessionSummary> HistoricalSessions);

public sealed record StructuralObservation(
    Guid Id,
    string Symbol,
    string SourceSessionId,
    StructuralRole Role,
    StructuralObservationKind Kind,
    StructuralEligibilityReason EligibilityReason,
    decimal Price,
    Guid? SourceSwingId,
    string? OriginEventIdentity,
    string? ConfirmationEventIdentity,
    DateTimeOffset? CandidateEventTimeUtc,
    DateTimeOffset? ConfirmedAtEventTimeUtc,
    DateTimeOffset EvidenceKnownAtUtc,
    string HistoricalProvider,
    string HistoricalFeed,
    string HistoricalSourceKind,
    string? FeatureSetVersion,
    string? StructureAlgorithmVersion);

public sealed record StructuralZone(
    Guid Id,
    StructuralRole Role,
    decimal LowerBound,
    decimal UpperBound,
    DateTimeOffset EvidenceKnownAtUtc,
    IReadOnlyList<string> SourceSessionIds,
    IReadOnlyList<StructuralObservation> RepresentativeObservations,
    IReadOnlyList<StructuralEligibilityReason> EligibilityReasons,
    int DistinctSessionCount,
    int TotalObservationCount,
    int OmittedObservationCount,
    string DefinitionVersion,
    string ConfigurationVersion);

public sealed record StructuralTrendInterval(
    string EarlierSessionId,
    string LaterSessionId,
    StructuralIntervalTrend State,
    decimal EarlierHigh,
    decimal LaterHigh,
    decimal EarlierLow,
    decimal LaterLow);

public sealed record MultiSessionStructuralTrend(
    MultiSessionStructuralTrendState State,
    IReadOnlyList<StructuralTrendInterval> Intervals,
    DateTimeOffset? EvidenceKnownAtUtc);

public sealed record MultiSessionStructuralCompleteness(
    StructuralAssessmentCompleteness Status,
    bool IsContiguous,
    bool HasUniformProvenance,
    IReadOnlyList<string> RequiredSessionIds,
    string Reason);

public sealed record MultiSessionStructuralAssessment(
    Guid Id,
    string Symbol,
    string CurrentSessionId,
    string ImmediatePriorSessionId,
    DateTimeOffset AssessmentAsOfUtc,
    DateTimeOffset? EvidenceKnownAtUtc,
    string DefinitionVersion,
    string ConfigurationVersion,
    IReadOnlyList<string> HistoricalSessionIds,
    MultiSessionStructuralCompleteness Completeness,
    MultiSessionStructuralTrend Trend,
    IReadOnlyList<StructuralZone> SupportZones,
    IReadOnlyList<StructuralZone> ResistanceZones,
    int OmittedSupportZoneCount,
    int OmittedResistanceZoneCount);

/// <summary>
/// Material multi-session structural-analysis thresholds. The percentage defaults are
/// PROVISIONAL / UNCALIBRATED development values and make no profitability claim.
/// Maximum projected-zone values are presentation/resource limits, not significance.
/// </summary>
public sealed record MultiSessionStructureConfiguration
{
    public int HistoricalSessionCount { get; init; } = 3;
    public int MinimumDistinctSessionsForRecurringZone { get; init; } = 2;
    public decimal ZoneMergeDistancePercent { get; init; } = 0.10m;
    public decimal StructuralEqualityTolerancePercent { get; init; } = 0.10m;
    public int MaximumProjectedSupportZones { get; init; } = 3;
    public int MaximumProjectedResistanceZones { get; init; } = 3;
    public int MaximumRepresentativesPerSessionPerZone { get; init; } = 3;
}
