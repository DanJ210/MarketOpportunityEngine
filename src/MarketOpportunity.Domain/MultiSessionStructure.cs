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

public readonly record struct CompactStructuralPivotCandidate(
    Guid SourceSwingId,
    StructuralRole Role,
    decimal Price,
    DateTimeOffset CandidateEventTimeUtc,
    DateTimeOffset? ConfirmedAtEventTimeUtc,
    string OriginEventIdentity,
    string? ConfirmationEventIdentity,
    int FeatureSetVersionIndex,
    int StructureAlgorithmVersionIndex);

public sealed record CompactStructuralPivotSet(
    IReadOnlyList<CompactStructuralPivotCandidate> Pivots,
    IReadOnlyList<string> FeatureSetVersions,
    IReadOnlyList<string> StructureAlgorithmVersions)
{
    public long EstimatedBytes
    {
        get
        {
            const long fixedBytesPerPivot = 96;
            long bytes = fixedBytesPerPivot * Pivots.Count;
            foreach (var pivot in Pivots)
            {
                bytes += 2L * pivot.OriginEventIdentity.Length;
                bytes += 2L * (pivot.ConfirmationEventIdentity?.Length ?? 0);
            }

            bytes += FeatureSetVersions.Sum(item => 2L * item.Length);
            bytes += StructureAlgorithmVersions.Sum(item => 2L * item.Length);
            return bytes;
        }
    }

    public static CompactStructuralPivotSet Create(
        string symbol,
        string sessionId,
        IReadOnlyList<StructuralPivotCandidate> candidates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(candidates);

        var featureVersions = new List<string>();
        var algorithmVersions = new List<string>();
        var pivots = new CompactStructuralPivotCandidate[candidates.Count];
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (!candidate.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)
                || !candidate.SessionId.Equals(sessionId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Compact pivots must belong to their containing symbol and session.",
                    nameof(candidates));
            }

            var featureVersionIndex = featureVersions.FindIndex(item =>
                item.Equals(candidate.FeatureSetVersion, StringComparison.Ordinal));
            if (featureVersionIndex < 0)
            {
                featureVersionIndex = featureVersions.Count;
                featureVersions.Add(candidate.FeatureSetVersion);
            }

            var algorithmVersionIndex = algorithmVersions.FindIndex(item =>
                item.Equals(candidate.AlgorithmVersion, StringComparison.Ordinal));
            if (algorithmVersionIndex < 0)
            {
                algorithmVersionIndex = algorithmVersions.Count;
                algorithmVersions.Add(candidate.AlgorithmVersion);
            }

            pivots[index] = new CompactStructuralPivotCandidate(
                candidate.SourceSwingId,
                candidate.Role,
                candidate.Price,
                candidate.CandidateEventTimeUtc,
                candidate.ConfirmedAtEventTimeUtc,
                candidate.OriginEventIdentity,
                candidate.ConfirmationEventIdentity,
                featureVersionIndex,
                algorithmVersionIndex);
        }

        return new CompactStructuralPivotSet(
            pivots,
            featureVersions.ToArray(),
            algorithmVersions.ToArray());
    }
}

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
    IReadOnlyList<StructuralPivotCandidate> ConfirmablePivots,
    CompactStructuralPivotSet? CompactPivots = null);

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
