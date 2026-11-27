using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MarketOpportunity.Domain;
using Microsoft.Extensions.Configuration;

namespace MarketOpportunity.Infrastructure;

public sealed record MarketSituationPersistenceOptions
{
    public const int CompactAssessmentMaximumBytes = 16 * 1024;

    public int CandidateMemberBatchSize { get; init; } = 1_000;

    public void Validate()
    {
        if (CandidateMemberBatchSize is < 1 or > 5_000)
        {
            throw new InvalidOperationException(
                "MarketData:Persistence:MarketSituationCandidateMemberBatchSize must be between 1 and 5000.");
        }
    }

    public static MarketSituationPersistenceOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var configured = configuration[
            "MarketData:Persistence:MarketSituationCandidateMemberBatchSize"];
        var options = new MarketSituationPersistenceOptions
        {
            CandidateMemberBatchSize = configured is null
                ? 1_000
                : int.TryParse(
                    configured,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var value)
                    ? value
                    : throw new InvalidOperationException(
                        "MarketSituation candidate member batch size must contain an integer.")
        };
        options.Validate();
        return options;
    }
}

public interface IMarketSituationPersistenceFaultInjector
{
    Task AfterMemberBatchAsync(
        int batchNumber,
        int persistedMemberCount,
        CancellationToken cancellationToken);
}

public enum MarketSituationPersistenceFailure
{
    CandidateSetConflict,
    CandidateSourceConflict,
    CandidateMemberConflict,
    CandidateCountMismatch,
    CandidateDigestMismatch,
    CandidateSetIncomplete,
    CompactAssessmentTooLarge,
    CompactAssessmentConflict,
    TransactionFailed,
    DatabaseTimeout,
    Cancelled
}

public sealed class MarketSituationPersistenceException(
    MarketSituationPersistenceFailure failure,
    string message,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    public MarketSituationPersistenceFailure Failure { get; } = failure;
}

public sealed record MarketSituationPersistenceWriteResult(
    bool CandidateSetCreated,
    bool CandidateSourcesCreated,
    bool AssessmentCreated,
    int CandidateCount,
    int MemberBatchCount,
    int CompactAssessmentBytes,
    int LargestCompactParameterBytes);

public sealed record MarketSituationCandidateSetReadModel(
    string CandidateSetId,
    string CanonicalizationVersion,
    Guid FeatureSnapshotId,
    Guid? PriorSessionContextId,
    string Symbol,
    string SessionId,
    DateTimeOffset AsOfUtc,
    int SupportCandidateCount,
    int ResistanceCandidateCount,
    int TotalCandidateCount,
    bool IsComplete);

public sealed record MarketSituationStoredAssessmentReadModel(
    Guid Id,
    string PersistenceVersion,
    MarketSituationAssessment? LegacyAssessment,
    CompactMarketSituationAssessment? CompactAssessment,
    MarketSituationCandidateSetReadModel? CandidateSet);

public sealed record CandidateSourcePersistenceMaterial(
    string CandidateSourceId,
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

public sealed record CandidateMemberPersistenceMaterial(
    string CandidateSetId,
    int Ordinal,
    Guid LevelId,
    string CandidateSourceId,
    string Role,
    decimal Price,
    string PriceCanonical,
    Guid SourceStructuralIdentity,
    string? SourceEventIdentity,
    string? ConfirmationEventIdentity,
    DateTimeOffset? SourceEventTimeUtc,
    DateTimeOffset? ConfirmedAtEventTimeUtc);

public static class MarketSituationPersistenceMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<CandidateSourcePersistenceMaterial> Sources(
        MarketSituationCandidateSet candidateSet)
    {
        ArgumentNullException.ThrowIfNull(candidateSet);
        var groups = candidateSet.EligibleLevels
            .Select(Source)
            .GroupBy(item => item.CandidateSourceId, StringComparer.Ordinal)
            .ToArray();
        foreach (var group in groups)
        {
            if (group.Distinct().Skip(1).Any())
            {
                throw new MarketSituationPersistenceException(
                    MarketSituationPersistenceFailure.CandidateSourceConflict,
                    $"Candidate source '{group.Key}' has conflicting material.");
            }
        }

        return groups
            .Select(group => group.First())
            .OrderBy(item => item.CandidateSourceId, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<CandidateMemberPersistenceMaterial> Members(
        MarketSituationCandidateSet candidateSet)
    {
        ArgumentNullException.ThrowIfNull(candidateSet);
        var members = candidateSet.EligibleLevels
            .Select((level, ordinal) => new CandidateMemberPersistenceMaterial(
                candidateSet.CandidateSetId,
                ordinal,
                level.Id,
                Source(level).CandidateSourceId,
                level.Role.ToString(),
                level.Price,
                CanonicalDecimal(level.Price),
                level.SourceStructuralIdentity,
                level.SourceEventIdentity,
                level.ConfirmationEventIdentity,
                level.SourceEventTimeUtc?.ToUniversalTime(),
                level.ConfirmedAtEventTimeUtc?.ToUniversalTime()))
            .ToArray();
        if (members.GroupBy(item => item.LevelId).Any(group => group.Skip(1).Any()))
        {
            throw new MarketSituationPersistenceException(
                MarketSituationPersistenceFailure.CandidateMemberConflict,
                "A MarketSituation candidate set contains duplicate LevelId values.");
        }

        return members;
    }

    public static string CompactMaterialDigest(CompactMarketSituationAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        var material = assessment with { EvaluatedAtUtc = DateTimeOffset.UnixEpoch };
        return Sha256(JsonSerializer.Serialize(material, JsonOptions));
    }

    public static string HeaderMaterialDigest(MarketSituationCandidateSet candidateSet)
    {
        ArgumentNullException.ThrowIfNull(candidateSet);
        var material = new
        {
            candidateSet.CandidateSetId,
            candidateSet.CanonicalizationVersion,
            candidateSet.FeatureSnapshotId,
            candidateSet.PriorSessionContextId,
            candidateSet.Symbol,
            candidateSet.SessionId,
            candidateSet.AsOfUtc,
            candidateSet.ContextMode,
            candidateSet.SupportCandidateCount,
            candidateSet.ResistanceCandidateCount,
            candidateSet.TotalCandidateCount,
            candidateSet.TestZonePercent,
            candidateSet.ApproachDistancePercent,
            candidateSet.DefinitionVersion,
            candidateSet.FeatureSetVersion,
            candidateSet.ConfigurationVersion,
            candidateSet.MarketData,
            candidateSet.DataQuality,
            candidateSet.MarketStructureStatus,
            candidateSet.HasCompleteCurrentSessionData
        };
        return Sha256(JsonSerializer.Serialize(material, JsonOptions));
    }

    public static string CompactJson(
        CompactMarketSituationAssessment assessment,
        out int byteCount)
    {
        var json = JsonSerializer.Serialize(assessment, JsonOptions);
        byteCount = Encoding.UTF8.GetByteCount(json);
        if (byteCount > MarketSituationPersistenceOptions.CompactAssessmentMaximumBytes)
        {
            throw new MarketSituationPersistenceException(
                MarketSituationPersistenceFailure.CompactAssessmentTooLarge,
                $"Compact MarketSituation assessment is {byteCount} bytes; the operational limit is " +
                $"{MarketSituationPersistenceOptions.CompactAssessmentMaximumBytes} bytes.");
        }

        if (json.Contains("\"eligibleLevels\"", StringComparison.OrdinalIgnoreCase))
        {
            throw new MarketSituationPersistenceException(
                MarketSituationPersistenceFailure.CompactAssessmentConflict,
                "Compact MarketSituation persistence cannot contain EligibleLevels.");
        }

        return json;
    }

    public static string CanonicalDecimal(decimal value) =>
        value.ToString("G29", CultureInfo.InvariantCulture);

    private static CandidateSourcePersistenceMaterial Source(ImportantPriceLevel level)
    {
        var material = new
        {
            level.Symbol,
            level.CurrentSessionId,
            level.SourceContextId,
            level.SourceSessionId,
            level.Source,
            level.SourceContextMode,
            level.SourceCompleteness,
            level.HistoricalSource,
            level.SourceCoverageStartUtc,
            level.SourceCoverageEndUtc,
            level.StructureAlgorithmVersion,
            level.FeatureSetVersion,
            level.ContextDefinitionVersion,
            level.HistoricalProvider,
            level.HistoricalFeed
        };
        return new CandidateSourcePersistenceMaterial(
            Sha256(JsonSerializer.Serialize(material, JsonOptions)),
            level.SourceContextId,
            level.SourceSessionId,
            level.Symbol,
            level.CurrentSessionId,
            level.Source.ToString(),
            level.SourceContextMode.ToString(),
            level.SourceCompleteness,
            level.HistoricalSource,
            level.SourceCoverageStartUtc?.ToUniversalTime(),
            level.SourceCoverageEndUtc?.ToUniversalTime(),
            level.StructureAlgorithmVersion,
            level.FeatureSetVersion,
            level.ContextDefinitionVersion,
            level.HistoricalProvider,
            level.HistoricalFeed);
    }

    public static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
