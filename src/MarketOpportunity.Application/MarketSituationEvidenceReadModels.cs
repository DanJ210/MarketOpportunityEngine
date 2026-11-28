using MarketOpportunity.Domain;

namespace MarketOpportunity.Application;

public sealed record MarketSituationEvidenceReadOptions
{
    public int DefaultPageSize { get; init; } = 64;
    public int MaximumPageSize { get; init; } = 64;
    public int MaximumSerializedPageBytes { get; init; } = 64 * 1024;

    public void Validate()
    {
        if (DefaultPageSize is < 1 or > 5_000
            || MaximumPageSize is < 1 or > 5_000
            || DefaultPageSize > MaximumPageSize)
        {
            throw new InvalidOperationException(
                "Operations:MarketSituationEvidence page sizes must be between 1 and 5000, " +
                "and the default cannot exceed the maximum.");
        }

        if (MaximumSerializedPageBytes is < 1 * 1024 or > 1_048_576)
        {
            throw new InvalidOperationException(
                "Operations:MarketSituationEvidence:MaximumSerializedPageBytes must be between " +
                "1024 and 1048576.");
        }
    }
}

public sealed record MarketSituationCandidateSourceReadModel(
    string Id,
    Guid? SourceContextId,
    string SourceSessionId,
    string Symbol,
    string CurrentSessionId,
    string SourceKind,
    string ContextMode,
    string SourceCompleteness,
    string? HistoricalSource,
    DateTimeOffset? SourceCoverageStartUtc,
    DateTimeOffset? SourceCoverageEndUtc,
    string? StructureAlgorithmVersion,
    string? FeatureSetVersion,
    string? ContextDefinitionVersion,
    string? HistoricalProvider,
    string? HistoricalFeed);

public sealed record MarketSituationCandidateMemberReadModel(
    int Ordinal,
    Guid LevelId,
    ImportantPriceLevelRole Role,
    decimal Price,
    string PriceCanonical,
    Guid SourceStructuralIdentity,
    string? SourceEventIdentity,
    string? ConfirmationEventIdentity,
    DateTimeOffset? SourceEventTimeUtc,
    DateTimeOffset? ConfirmedAtEventTimeUtc,
    string CandidateSourceId);

public sealed record MarketSituationCandidatePageReadModel(
    Guid AssessmentId,
    string CandidateSetId,
    string CanonicalizationVersion,
    int TotalCandidateCount,
    int ReturnedCount,
    int? FirstOrdinal,
    int? LastOrdinal,
    int AfterOrdinal,
    int? NextAfterOrdinal,
    bool HasMore,
    IReadOnlyList<MarketSituationCandidateMemberReadModel> Members,
    IReadOnlyDictionary<string, MarketSituationCandidateSourceReadModel> Sources,
    string ReadModelVersion);

public sealed record MarketSituationSelectedCandidateReadModel(
    string CandidateSetId,
    MarketSituationCandidateMemberReadModel Member,
    MarketSituationCandidateSourceReadModel Source);

public enum MarketSituationEvidenceReadFailure
{
    LegacyCandidateEvidenceUnavailable,
    CandidateSetIncomplete
}

public sealed class MarketSituationEvidenceReadException(
    MarketSituationEvidenceReadFailure failure,
    string message)
    : InvalidOperationException(message)
{
    public MarketSituationEvidenceReadFailure Failure { get; } = failure;
}

public interface IMarketSituationEvidenceStore
{
    Task<MarketSituationCandidatePageReadModel?> ReadCandidatePageAsync(
        Guid assessmentId,
        int afterOrdinal,
        int pageSize,
        CancellationToken cancellationToken);

    Task<MarketSituationSelectedCandidateReadModel?> ReadSelectedCandidateAsync(
        string candidateSetId,
        Guid levelId,
        CancellationToken cancellationToken);
}
