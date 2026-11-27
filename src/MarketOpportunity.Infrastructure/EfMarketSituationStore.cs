using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using MarketOpportunity.Application;
using MarketOpportunity.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MarketOpportunity.Infrastructure;

/// <summary>
/// Persists legacy v1 assessments and the normalized immutable v2 MarketSituation evidence contract.
/// </summary>
public sealed class EfMarketSituationStore
    : IMarketSituationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly MarketOpportunityDbContext _dbContext;
    private readonly MarketSituationPersistenceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly IMarketSituationPersistenceFaultInjector? _faultInjector;

    public EfMarketSituationStore(MarketOpportunityDbContext dbContext)
        : this(dbContext, new MarketSituationPersistenceOptions(), TimeProvider.System)
    {
    }

    public EfMarketSituationStore(
        MarketOpportunityDbContext dbContext,
        MarketSituationPersistenceOptions options,
        TimeProvider timeProvider,
        IMarketSituationPersistenceFaultInjector? faultInjector = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        options.Validate();
        _dbContext = dbContext;
        _options = options;
        _timeProvider = timeProvider;
        _faultInjector = faultInjector;
    }

    public async Task PersistAsync(
        MarketSituationAssessment assessment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        var json = JsonSerializer.Serialize(assessment, JsonOptions);
        var existing = await _dbContext.MarketSituationAssessments
            .SingleOrDefaultAsync(item => item.Id == assessment.Id, cancellationToken);
        if (existing is null)
        {
            _dbContext.MarketSituationAssessments.Add(ToLegacyEntity(assessment, json));
        }
        else
        {
            if (existing.PersistenceVersion is not null)
            {
                throw new InvalidOperationException(
                    $"Market situation assessment '{assessment.Id}' is a compact v2 row, not legacy v1.");
            }

            var stored = JsonSerializer.Deserialize<MarketSituationAssessment>(
                existing.AssessmentJson,
                JsonOptions);
            var storedMaterial = stored is null
                ? existing.AssessmentJson
                : JsonSerializer.Serialize(
                    stored with { EvaluatedAtUtc = assessment.EvaluatedAtUtc },
                    JsonOptions);
            if (!string.Equals(storedMaterial, json, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Market situation assessment '{assessment.Id}' is immutable and already has different values.");
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task PersistAsync(
        MarketSituationEvaluation evaluation,
        CancellationToken cancellationToken) =>
        PersistV2Async(evaluation, cancellationToken);

    public async Task<MarketSituationPersistenceWriteResult> PersistV2Async(
        MarketSituationEvaluation evaluation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        evaluation.CandidateSet.EnsureIdentityMatches();
        evaluation.ProjectLegacy();

        var compactJson = MarketSituationPersistenceMapper.CompactJson(
            evaluation.Assessment,
            out var compactBytes);
        var assessmentDigest = MarketSituationPersistenceMapper.CompactMaterialDigest(
            evaluation.Assessment);
        var sources = MarketSituationPersistenceMapper.Sources(evaluation.CandidateSet);
        var members = MarketSituationPersistenceMapper.Members(evaluation.CandidateSet);
        if (sources.Count == 0 && members.Count != 0)
        {
            throw new MarketSituationPersistenceException(
                MarketSituationPersistenceFailure.CandidateMemberConflict,
                "Candidate members cannot be persisted without normalized candidate sources.");
        }

        await _dbContext.Database.OpenConnectionAsync(cancellationToken);
        var connection = (SqlConnection)_dbContext.Database.GetDbConnection();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var assessmentCreated = false;
        var candidateSetCreated = false;
        var candidateSourcesCreated = false;
        var batchCount = 0;
        try
        {
            var existingAssessment = await ReadAssessmentEntityAsync(
                transaction,
                evaluation.Assessment.Id,
                cancellationToken);
            if (existingAssessment is not null)
            {
                await VerifyExistingAssessmentAsync(
                    transaction,
                    existingAssessment,
                    evaluation.Assessment,
                    compactJson,
                    assessmentDigest,
                    cancellationToken);
                await VerifyExistingCandidateSetAsync(
                    transaction,
                    evaluation.CandidateSet,
                    sources,
                    members,
                    cancellationToken);
            }
            else
            {
                var created = await EnsureCandidateSetAsync(
                    transaction,
                    evaluation.CandidateSet,
                    sources,
                    members,
                    cancellationToken);
                candidateSetCreated = created.SetCreated;
                candidateSourcesCreated = created.SourcesCreated;
                batchCount = created.BatchCount;
                assessmentCreated = await InsertAssessmentAsync(
                    transaction,
                    evaluation.Assessment,
                    compactJson,
                    assessmentDigest,
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new MarketSituationPersistenceWriteResult(
                candidateSetCreated,
                candidateSourcesCreated,
                assessmentCreated,
                evaluation.CandidateSet.TotalCandidateCount,
                batchCount,
                compactBytes,
                compactBytes);
        }
        catch (MarketSituationPersistenceException)
        {
            await RollbackAsync(transaction, cancellationToken);
            throw;
        }
        catch (OperationCanceledException)
        {
            await RollbackAsync(transaction, CancellationToken.None);
            throw;
        }
        catch (SqlException exception) when (exception.Number == -2)
        {
            await RollbackAsync(transaction, CancellationToken.None);
            throw new MarketSituationPersistenceException(
                MarketSituationPersistenceFailure.DatabaseTimeout,
                "MarketSituation v2 persistence timed out.",
                exception);
        }
        catch (Exception exception) when (exception is SqlException or DbException or InvalidOperationException)
        {
            await RollbackAsync(transaction, CancellationToken.None);
            throw new MarketSituationPersistenceException(
                MarketSituationPersistenceFailure.TransactionFailed,
                "MarketSituation v2 persistence transaction failed and was rolled back.",
                exception);
        }
    }

    public async Task<MarketSituationStoredAssessmentReadModel?> ReadAsync(
        Guid assessmentId,
        CancellationToken cancellationToken)
    {
        await _dbContext.Database.OpenConnectionAsync(cancellationToken);
        var connection = (SqlConnection)_dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT PersistenceVersion, CandidateSetId, AssessmentJson
            FROM dbo.market_situation_assessment
            WHERE Id = @assessmentId;
            """;
        command.Parameters.Add("@assessmentId", SqlDbType.UniqueIdentifier).Value = assessmentId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var persistenceVersion = reader.IsDBNull(0)
            ? null
            : reader.GetString(0);
        var candidateSetId = reader.IsDBNull(1)
            ? null
            : reader.GetString(1);
        var json = reader.GetString(2);
        await reader.DisposeAsync();

        if (persistenceVersion is null)
        {
            var legacy = JsonSerializer.Deserialize<MarketSituationAssessment>(json, JsonOptions)
                ?? throw new JsonException(
                    $"Legacy MarketSituation assessment '{assessmentId}' could not be read.");
            return new MarketSituationStoredAssessmentReadModel(
                assessmentId,
                "market-situation-persistence-v1",
                legacy,
                null,
                null);
        }

        if (!string.Equals(
                persistenceVersion,
                CompactMarketSituationAssessment.CurrentPersistenceContractVersion,
                StringComparison.Ordinal)
            || candidateSetId is null)
        {
            throw new MarketSituationPersistenceException(
                MarketSituationPersistenceFailure.CompactAssessmentConflict,
                $"MarketSituation assessment '{assessmentId}' has an unsupported persistence envelope.");
        }

        var compact = JsonSerializer.Deserialize<CompactMarketSituationAssessment>(json, JsonOptions)
            ?? throw new JsonException(
                $"Compact MarketSituation assessment '{assessmentId}' could not be read.");
        await using var setCommand = connection.CreateCommand();
        setCommand.CommandText =
            """
            SELECT Id, CanonicalizationVersion, FeatureSnapshotId, PriorSessionContextId,
                   Symbol, SessionId, AsOfUtc, SupportCandidateCount, ResistanceCandidateCount,
                   TotalCandidateCount, IsComplete
            FROM dbo.market_situation_candidate_set
            WHERE Id = @candidateSetId;
            """;
        setCommand.Parameters.Add("@candidateSetId", SqlDbType.Char, 64).Value = candidateSetId;
        await using var setReader = await setCommand.ExecuteReaderAsync(cancellationToken);
        if (!await setReader.ReadAsync(cancellationToken))
        {
            throw new MarketSituationPersistenceException(
                MarketSituationPersistenceFailure.CandidateSetIncomplete,
                $"Compact MarketSituation assessment '{assessmentId}' references missing candidate set '{candidateSetId}'.");
        }

        var candidateSet = new MarketSituationCandidateSetReadModel(
            setReader.GetString(0),
            setReader.GetString(1),
            setReader.GetGuid(2),
            setReader.IsDBNull(3) ? null : setReader.GetGuid(3),
            setReader.GetString(4),
            setReader.GetString(5),
            setReader.GetFieldValue<DateTimeOffset>(6).ToUniversalTime(),
            setReader.GetInt32(7),
            setReader.GetInt32(8),
            setReader.GetInt32(9),
            setReader.GetBoolean(10));
        if (!candidateSet.IsComplete)
        {
            throw new MarketSituationPersistenceException(
                MarketSituationPersistenceFailure.CandidateSetIncomplete,
                $"Candidate set '{candidateSetId}' is not complete.");
        }

        return new MarketSituationStoredAssessmentReadModel(
            assessmentId,
            persistenceVersion,
            null,
            compact,
            candidateSet);
    }

    private async Task<CandidateSetWriteResult> EnsureCandidateSetAsync(
        SqlTransaction transaction,
        MarketSituationCandidateSet candidateSet,
        IReadOnlyList<CandidateSourcePersistenceMaterial> sources,
        IReadOnlyList<CandidateMemberPersistenceMaterial> members,
        CancellationToken cancellationToken)
    {
        var existing = await ReadCandidateSetAsync(
            transaction,
            candidateSet.CandidateSetId,
            cancellationToken);
        if (existing is not null)
        {
            await VerifyExistingCandidateSetAsync(
                transaction,
                candidateSet,
                sources,
                members,
                cancellationToken);
            return new CandidateSetWriteResult(false, false, 0);
        }

        await InsertCandidateSetAsync(transaction, candidateSet, cancellationToken);
        var sourcesCreated = false;
        foreach (var source in sources)
        {
            sourcesCreated |= await EnsureSourceAsync(
                transaction,
                source,
                cancellationToken);
        }

        var batchCount = 0;
        foreach (var batch in members.Chunk(_options.CandidateMemberBatchSize))
        {
            batchCount++;
            await InsertMemberBatchAsync(transaction, batch, cancellationToken);
            if (_faultInjector is not null)
            {
                await _faultInjector.AfterMemberBatchAsync(
                    batchCount,
                    Math.Min(batchCount * _options.CandidateMemberBatchSize, members.Count),
                    cancellationToken);
            }
        }

        var persistedCount = await CountMembersAsync(
            transaction,
            candidateSet.CandidateSetId,
            cancellationToken);
        if (persistedCount != candidateSet.TotalCandidateCount)
        {
            throw new MarketSituationPersistenceException(
                MarketSituationPersistenceFailure.CandidateCountMismatch,
                $"Candidate set '{candidateSet.CandidateSetId}' expected {candidateSet.TotalCandidateCount} " +
                $"members but persisted {persistedCount}.");
        }

        await MarkCandidateSetCompleteAsync(transaction, candidateSet.CandidateSetId, cancellationToken);
        return new CandidateSetWriteResult(true, sourcesCreated, batchCount);
    }

    private async Task VerifyExistingAssessmentAsync(
        SqlTransaction transaction,
        AssessmentEntitySnapshot existing,
        CompactMarketSituationAssessment assessment,
        string compactJson,
        string assessmentDigest,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                existing.PersistenceVersion,
                CompactMarketSituationAssessment.CurrentPersistenceContractVersion,
                StringComparison.Ordinal)
            || !string.Equals(existing.CandidateSetId, assessment.CandidateSetId, StringComparison.Ordinal)
            || !string.Equals(existing.AssessmentMaterialDigest, assessmentDigest, StringComparison.Ordinal))
        {
            throw new MarketSituationPersistenceException(
                MarketSituationPersistenceFailure.CompactAssessmentConflict,
                $"Compact MarketSituation assessment '{assessment.Id}' has conflicting material.");
        }

        var stored = JsonSerializer.Deserialize<CompactMarketSituationAssessment>(
            existing.AssessmentJson,
            JsonOptions);
        if (stored is null
            || JsonSerializer.Serialize(
                stored with { EvaluatedAtUtc = assessment.EvaluatedAtUtc },
                JsonOptions) != compactJson)
        {
            throw new MarketSituationPersistenceException(
                MarketSituationPersistenceFailure.CompactAssessmentConflict,
                $"Compact MarketSituation assessment '{assessment.Id}' has conflicting serialized material.");
        }

        var complete = await ReadCandidateSetAsync(
            transaction,
            assessment.CandidateSetId,
            cancellationToken);
        if (complete is null || !complete.IsComplete)
        {
            throw new MarketSituationPersistenceException(
                MarketSituationPersistenceFailure.CandidateSetIncomplete,
                $"Assessment '{assessment.Id}' references an incomplete candidate set.");
        }
    }

    private async Task VerifyExistingCandidateSetAsync(
        SqlTransaction transaction,
        MarketSituationCandidateSet candidateSet,
        IReadOnlyList<CandidateSourcePersistenceMaterial> sources,
        IReadOnlyList<CandidateMemberPersistenceMaterial> members,
        CancellationToken cancellationToken)
    {
        var existing = await ReadCandidateSetAsync(
            transaction,
            candidateSet.CandidateSetId,
            cancellationToken);
        if (existing is null)
        {
            throw new MarketSituationPersistenceException(
                MarketSituationPersistenceFailure.CandidateSetConflict,
                $"Candidate set '{candidateSet.CandidateSetId}' is missing.");
        }

        if (!existing.IsComplete)
        {
            throw new MarketSituationPersistenceException(
                MarketSituationPersistenceFailure.CandidateSetIncomplete,
                $"Candidate set '{candidateSet.CandidateSetId}' is incomplete and cannot be referenced.");
        }

        var headerDigest = MarketSituationPersistenceMapper.HeaderMaterialDigest(candidateSet);
        if (existing.SupportCount != candidateSet.SupportCandidateCount
            || existing.ResistanceCount != candidateSet.ResistanceCandidateCount
            || existing.TotalCount != candidateSet.TotalCandidateCount
            || !string.Equals(existing.MaterialDigest, headerDigest, StringComparison.Ordinal)
            || !string.Equals(
                existing.CandidateMaterialDigest,
                candidateSet.CandidateSetId,
                StringComparison.Ordinal))
        {
            throw new MarketSituationPersistenceException(
                MarketSituationPersistenceFailure.CandidateDigestMismatch,
                $"Candidate set '{candidateSet.CandidateSetId}' has conflicting header material.");
        }

        foreach (var source in sources)
        {
            await VerifySourceAsync(transaction, source, cancellationToken);
        }

        await VerifyMembersAsync(
            transaction,
            candidateSet.CandidateSetId,
            members,
            cancellationToken);
    }

    private async Task VerifySourceAsync(
        SqlTransaction transaction,
        CandidateSourcePersistenceMaterial source,
        CancellationToken cancellationToken)
    {
        await using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT SourceContextId, SourceSessionId, Symbol, CurrentSessionId, SourceKind,
                   ContextMode, SourceCompleteness, HistoricalSource, SourceCoverageStartUtc,
                   SourceCoverageEndUtc, StructureAlgorithmVersion, FeatureSetVersion,
                   ContextDefinitionVersion, HistoricalProvider, HistoricalFeed, MaterialDigest
            FROM dbo.market_situation_candidate_source
            WHERE Id = @sourceId;
            """;
        command.Parameters.Add("@sourceId", SqlDbType.Char, 64).Value = source.CandidateSourceId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new MarketSituationPersistenceException(
                MarketSituationPersistenceFailure.CandidateSourceConflict,
                $"Candidate source '{source.CandidateSourceId}' is missing.");
        }

        var stored = new CandidateSourcePersistenceMaterial(
            source.CandidateSourceId,
            reader.IsDBNull(0) ? null : reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8).ToUniversalTime(),
            reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9).ToUniversalTime(),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.IsDBNull(14) ? null : reader.GetString(14));
        if (stored != source
            || !string.Equals(reader.GetString(15), source.CandidateSourceId, StringComparison.Ordinal))
        {
            throw new MarketSituationPersistenceException(
                MarketSituationPersistenceFailure.CandidateSourceConflict,
                $"Candidate source '{source.CandidateSourceId}' has conflicting material.");
        }
    }

    private static async Task VerifyMembersAsync(
        SqlTransaction transaction,
        string candidateSetId,
        IReadOnlyList<CandidateMemberPersistenceMaterial> expected,
        CancellationToken cancellationToken)
    {
        await using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT Ordinal, LevelId, CandidateSourceId, Role, Price, PriceCanonical,
                   SourceStructuralIdentity, SourceEventIdentity, ConfirmationEventIdentity,
                   SourceEventTimeUtc, ConfirmedAtEventTimeUtc
            FROM dbo.market_situation_candidate_member
            WHERE CandidateSetId = @candidateSetId
            ORDER BY Ordinal;
            """;
        command.Parameters.Add("@candidateSetId", SqlDbType.Char, 64).Value = candidateSetId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var index = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (index >= expected.Count)
            {
                throw new MarketSituationPersistenceException(
                    MarketSituationPersistenceFailure.CandidateCountMismatch,
                    "Stored candidate set has extra members.");
            }

            var priceCanonical = reader.GetString(5);
            var item = new CandidateMemberPersistenceMaterial(
                expected[index].CandidateSetId,
                reader.GetInt32(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                decimal.Parse(
                    priceCanonical,
                    NumberStyles.AllowLeadingSign
                        | NumberStyles.AllowDecimalPoint
                        | NumberStyles.AllowExponent,
                    CultureInfo.InvariantCulture),
                priceCanonical,
                reader.GetGuid(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9).ToUniversalTime(),
                reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10).ToUniversalTime());
            if (item != expected[index])
            {
                throw new MarketSituationPersistenceException(
                    MarketSituationPersistenceFailure.CandidateMemberConflict,
                    $"Candidate set member ordinal {index} has conflicting material.");
            }

            index++;
        }

        if (index != expected.Count)
        {
            throw new MarketSituationPersistenceException(
                MarketSituationPersistenceFailure.CandidateCountMismatch,
                $"Candidate set expected {expected.Count} members but found {index}.");
        }
    }

    private async Task<bool> EnsureSourceAsync(
        SqlTransaction transaction,
        CandidateSourcePersistenceMaterial source,
        CancellationToken cancellationToken)
    {
        await using var lookup = transaction.Connection!.CreateCommand();
        lookup.Transaction = transaction;
        lookup.CommandText =
            """
            SELECT COUNT(*)
            FROM dbo.market_situation_candidate_source
            WHERE Id = @sourceId;
            """;
        lookup.Parameters.Add("@sourceId", SqlDbType.Char, 64).Value = source.CandidateSourceId;
        if (Convert.ToInt32(await lookup.ExecuteScalarAsync(cancellationToken)) > 0)
        {
            await VerifySourceAsync(transaction, source, cancellationToken);
            return false;
        }

        await using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO dbo.market_situation_candidate_source
                (Id, SourceContextId, SourceSessionId, Symbol, CurrentSessionId, SourceKind,
                 ContextMode, SourceCompleteness, HistoricalSource, SourceCoverageStartUtc,
                 SourceCoverageEndUtc, StructureAlgorithmVersion, FeatureSetVersion,
                 ContextDefinitionVersion, HistoricalProvider, HistoricalFeed, MaterialDigest,
                 CreatedAtUtc)
            VALUES
                (@id, @sourceContextId, @sourceSessionId, @symbol, @currentSessionId, @sourceKind,
                 @contextMode, @sourceCompleteness, @historicalSource, @coverageStart,
                 @coverageEnd, @structureVersion, @featureVersion, @contextVersion,
                 @historicalProvider, @historicalFeed, @materialDigest, @createdAt);
            """;
        Add(command, "@id", SqlDbType.Char, 64, source.CandidateSourceId);
        Add(command, "@sourceContextId", SqlDbType.UniqueIdentifier, source.SourceContextId);
        Add(command, "@sourceSessionId", SqlDbType.NVarChar, 64, source.SourceSessionId);
        Add(command, "@symbol", SqlDbType.NVarChar, 32, source.Symbol);
        Add(command, "@currentSessionId", SqlDbType.NVarChar, 64, source.CurrentSessionId);
        Add(command, "@sourceKind", SqlDbType.NVarChar, 32, source.SourceKind);
        Add(command, "@contextMode", SqlDbType.NVarChar, 32, source.ContextMode);
        Add(command, "@sourceCompleteness", SqlDbType.NVarChar, 256, source.SourceCompleteness);
        Add(command, "@historicalSource", SqlDbType.NVarChar, 128, source.HistoricalSource);
        Add(command, "@coverageStart", SqlDbType.DateTimeOffset, 7, source.SourceCoverageStartUtc);
        Add(command, "@coverageEnd", SqlDbType.DateTimeOffset, 7, source.SourceCoverageEndUtc);
        Add(command, "@structureVersion", SqlDbType.NVarChar, 64, source.StructureAlgorithmVersion);
        Add(command, "@featureVersion", SqlDbType.NVarChar, 64, source.FeatureSetVersion);
        Add(command, "@contextVersion", SqlDbType.NVarChar, 64, source.ContextDefinitionVersion);
        Add(command, "@historicalProvider", SqlDbType.NVarChar, 64, source.HistoricalProvider);
        Add(command, "@historicalFeed", SqlDbType.NVarChar, 64, source.HistoricalFeed);
        Add(command, "@materialDigest", SqlDbType.Char, 64, source.CandidateSourceId);
        Add(command, "@createdAt", SqlDbType.DateTimeOffset, 7, _timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }

    private async Task InsertCandidateSetAsync(
        SqlTransaction transaction,
        MarketSituationCandidateSet candidateSet,
        CancellationToken cancellationToken)
    {
        await using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO dbo.market_situation_candidate_set
                (Id, CanonicalizationVersion, FeatureSnapshotId, PriorSessionContextId, Symbol,
                 SessionId, AsOfUtc, ContextMode, SupportCandidateCount, ResistanceCandidateCount,
                 TotalCandidateCount, TestZonePercent, ApproachDistancePercent, DefinitionVersion,
                 FeatureSetVersion, ConfigurationVersion, MarketDataProvider, MarketDataFeed,
                 MarketDataCoverage, MarketDataIsRealTime, MarketDataSupportsTrades,
                 MarketDataSupportsQuotes, DataQuality, MarketStructureStatus,
                 HasCompleteCurrentSessionData, MaterialDigest, CandidateMaterialDigest,
                 IsComplete, CreatedAtUtc, CompletedAtUtc)
            VALUES
                (@id, @canonicalVersion, @featureId, @priorContextId, @symbol, @sessionId,
                 @asOf, @contextMode, @supportCount, @resistanceCount, @totalCount,
                 @testZone, @approachDistance, @definitionVersion, @featureVersion,
                 @configurationVersion, @provider, @feed, @coverage, @isRealTime,
                 @supportsTrades, @supportsQuotes, @dataQuality, @structureStatus,
                 @hasCompleteData, @materialDigest, @candidateDigest, 0, @createdAt, NULL);
            """;
        Add(command, "@id", SqlDbType.Char, 64, candidateSet.CandidateSetId);
        Add(command, "@canonicalVersion", SqlDbType.NVarChar, 64, candidateSet.CanonicalizationVersion);
        Add(command, "@featureId", SqlDbType.UniqueIdentifier, candidateSet.FeatureSnapshotId);
        Add(command, "@priorContextId", SqlDbType.UniqueIdentifier, candidateSet.PriorSessionContextId);
        Add(command, "@symbol", SqlDbType.NVarChar, 32, candidateSet.Symbol);
        Add(command, "@sessionId", SqlDbType.NVarChar, 64, candidateSet.SessionId);
        Add(command, "@asOf", SqlDbType.DateTimeOffset, 7, candidateSet.AsOfUtc);
        Add(command, "@contextMode", SqlDbType.NVarChar, 32, candidateSet.ContextMode.ToString());
        Add(command, "@supportCount", SqlDbType.Int, candidateSet.SupportCandidateCount);
        Add(command, "@resistanceCount", SqlDbType.Int, candidateSet.ResistanceCandidateCount);
        Add(command, "@totalCount", SqlDbType.Int, candidateSet.TotalCandidateCount);
        Add(command, "@testZone", SqlDbType.Decimal, 38, 18, candidateSet.TestZonePercent);
        Add(command, "@approachDistance", SqlDbType.Decimal, 38, 18, candidateSet.ApproachDistancePercent);
        Add(command, "@definitionVersion", SqlDbType.NVarChar, 64, candidateSet.DefinitionVersion);
        Add(command, "@featureVersion", SqlDbType.NVarChar, 64, candidateSet.FeatureSetVersion);
        Add(command, "@configurationVersion", SqlDbType.NVarChar, 64, candidateSet.ConfigurationVersion);
        Add(command, "@provider", SqlDbType.NVarChar, 64, candidateSet.MarketData.Provider);
        Add(command, "@feed", SqlDbType.NVarChar, 64, candidateSet.MarketData.Feed);
        Add(command, "@coverage", SqlDbType.NVarChar, 32, candidateSet.MarketData.Coverage.ToString());
        Add(command, "@isRealTime", SqlDbType.Bit, candidateSet.MarketData.IsRealTime);
        Add(command, "@supportsTrades", SqlDbType.Bit, candidateSet.MarketData.SupportsTrades);
        Add(command, "@supportsQuotes", SqlDbType.Bit, candidateSet.MarketData.SupportsQuotes);
        Add(command, "@dataQuality", SqlDbType.NVarChar, 32, candidateSet.DataQuality.ToString());
        Add(command, "@structureStatus", SqlDbType.NVarChar, 32, candidateSet.MarketStructureStatus.ToString());
        Add(command, "@hasCompleteData", SqlDbType.Bit, candidateSet.HasCompleteCurrentSessionData);
        Add(
            command,
            "@materialDigest",
            SqlDbType.Char,
            64,
            MarketSituationPersistenceMapper.HeaderMaterialDigest(candidateSet));
        Add(command, "@candidateDigest", SqlDbType.Char, 64, candidateSet.CandidateSetId);
        Add(command, "@createdAt", SqlDbType.DateTimeOffset, 7, _timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertMemberBatchAsync(
        SqlTransaction transaction,
        IReadOnlyList<CandidateMemberPersistenceMaterial> members,
        CancellationToken cancellationToken)
    {
        var table = CreateMemberDataTable();
        foreach (var member in members)
        {
            table.Rows.Add(
                member.CandidateSetId,
                member.Ordinal,
                member.LevelId,
                member.CandidateSourceId,
                member.Role,
                member.Price,
                member.PriceCanonical,
                member.SourceStructuralIdentity,
                (object?)member.SourceEventIdentity ?? DBNull.Value,
                (object?)member.ConfirmationEventIdentity ?? DBNull.Value,
                member.SourceEventTimeUtc.HasValue ? member.SourceEventTimeUtc.Value : DBNull.Value,
                member.ConfirmedAtEventTimeUtc.HasValue ? member.ConfirmedAtEventTimeUtc.Value : DBNull.Value);
        }

        using var bulk = new SqlBulkCopy(transaction.Connection, SqlBulkCopyOptions.CheckConstraints, transaction)
        {
            DestinationTableName = "dbo.market_situation_candidate_member",
            BatchSize = members.Count,
            EnableStreaming = true
        };
        foreach (DataColumn column in table.Columns)
        {
            bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await bulk.WriteToServerAsync(table, cancellationToken);
    }

    private static DataTable CreateMemberDataTable()
    {
        var table = new DataTable("market_situation_candidate_member");
        table.Columns.Add("CandidateSetId", typeof(string));
        table.Columns.Add("Ordinal", typeof(int));
        table.Columns.Add("LevelId", typeof(Guid));
        table.Columns.Add("CandidateSourceId", typeof(string));
        table.Columns.Add("Role", typeof(string));
        table.Columns.Add("Price", typeof(decimal));
        table.Columns.Add("PriceCanonical", typeof(string));
        table.Columns.Add("SourceStructuralIdentity", typeof(Guid));
        table.Columns.Add("SourceEventIdentity", typeof(string));
        table.Columns.Add("ConfirmationEventIdentity", typeof(string));
        table.Columns.Add("SourceEventTimeUtc", typeof(DateTimeOffset));
        table.Columns.Add("ConfirmedAtEventTimeUtc", typeof(DateTimeOffset));
        return table;
    }

    private async Task MarkCandidateSetCompleteAsync(
        SqlTransaction transaction,
        string candidateSetId,
        CancellationToken cancellationToken)
    {
        await using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE dbo.market_situation_candidate_set
            SET IsComplete = 1,
                CompletedAtUtc = @completedAt
            WHERE Id = @id;
            """;
        Add(command, "@id", SqlDbType.Char, 64, candidateSetId);
        Add(command, "@completedAt", SqlDbType.DateTimeOffset, 7, _timeProvider.GetUtcNow());
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected != 1)
        {
            throw new MarketSituationPersistenceException(
                MarketSituationPersistenceFailure.CandidateSetIncomplete,
                $"Candidate set '{candidateSetId}' could not be completed.");
        }
    }

    private async Task<bool> InsertAssessmentAsync(
        SqlTransaction transaction,
        CompactMarketSituationAssessment assessment,
        string compactJson,
        string assessmentDigest,
        CancellationToken cancellationToken)
    {
        var existing = await ReadAssessmentEntityAsync(transaction, assessment.Id, cancellationToken);
        if (existing is not null)
        {
            return false;
        }

        await using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO dbo.market_situation_assessment
                (Id, FeatureSnapshotId, PriorSessionContextId, Symbol, SessionId, AsOfUtc,
                 EvaluatedAtUtc, Status, ContextMode, PrimarySituationType, SupportSituationType,
                 ResistanceSituationType, SupportLevelSource, ResistanceLevelSource,
                 DefinitionVersion, ConfigurationVersion, AssessmentJson, PersistenceVersion,
                 CandidateSetId, AssessmentMaterialDigest, SupportLevelId, ResistanceLevelId)
            VALUES
                (@id, @featureId, @priorContextId, @symbol, @sessionId, @asOf, @evaluatedAt,
                 @status, @contextMode, @primaryType, @supportType, @resistanceType,
                 @supportSource, @resistanceSource, @definitionVersion, @configurationVersion,
                 @assessmentJson, @persistenceVersion, @candidateSetId, @materialDigest,
                 @supportLevelId, @resistanceLevelId);
            """;
        Add(command, "@id", SqlDbType.UniqueIdentifier, assessment.Id);
        Add(command, "@featureId", SqlDbType.UniqueIdentifier, assessment.FeatureSnapshotId);
        Add(command, "@priorContextId", SqlDbType.UniqueIdentifier, assessment.PriorSessionContextId);
        Add(command, "@symbol", SqlDbType.NVarChar, 32, assessment.Symbol);
        Add(command, "@sessionId", SqlDbType.NVarChar, 64, assessment.SessionId);
        Add(command, "@asOf", SqlDbType.DateTimeOffset, 7, assessment.AsOfUtc);
        Add(command, "@evaluatedAt", SqlDbType.DateTimeOffset, 7, assessment.EvaluatedAtUtc);
        Add(command, "@status", SqlDbType.NVarChar, 32, assessment.Status.ToString());
        Add(command, "@contextMode", SqlDbType.NVarChar, 32, assessment.ContextMode.ToString());
        Add(
            command,
            "@primaryType",
            SqlDbType.NVarChar,
            32,
            assessment.PrimarySituation?.SituationType.ToString());
        Add(
            command,
            "@supportType",
            SqlDbType.NVarChar,
            32,
            assessment.SupportInteraction?.SituationType.ToString());
        Add(
            command,
            "@resistanceType",
            SqlDbType.NVarChar,
            32,
            assessment.ResistanceInteraction?.SituationType.ToString());
        Add(
            command,
            "@supportSource",
            SqlDbType.NVarChar,
            32,
            assessment.NearestSupportLevel?.Source.ToString());
        Add(
            command,
            "@resistanceSource",
            SqlDbType.NVarChar,
            32,
            assessment.NearestResistanceLevel?.Source.ToString());
        Add(command, "@definitionVersion", SqlDbType.NVarChar, 64, assessment.DefinitionVersion);
        Add(command, "@configurationVersion", SqlDbType.NVarChar, 64, assessment.ConfigurationVersion);
        Add(command, "@assessmentJson", SqlDbType.NVarChar, compactJson.Length, compactJson);
        Add(
            command,
            "@persistenceVersion",
            SqlDbType.NVarChar,
            64,
            assessment.PersistenceContractVersion);
        Add(command, "@candidateSetId", SqlDbType.Char, 64, assessment.CandidateSetId);
        Add(command, "@materialDigest", SqlDbType.Char, 64, assessmentDigest);
        Add(
            command,
            "@supportLevelId",
            SqlDbType.UniqueIdentifier,
            assessment.NearestSupportLevel?.Id);
        Add(
            command,
            "@resistanceLevelId",
            SqlDbType.UniqueIdentifier,
            assessment.NearestResistanceLevel?.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }

    private static async Task<AssessmentEntitySnapshot?> ReadAssessmentEntityAsync(
        SqlTransaction transaction,
        Guid assessmentId,
        CancellationToken cancellationToken)
    {
        await using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT PersistenceVersion, CandidateSetId, AssessmentMaterialDigest, AssessmentJson
            FROM dbo.market_situation_assessment
            WHERE Id = @assessmentId;
            """;
        command.Parameters.Add("@assessmentId", SqlDbType.UniqueIdentifier).Value = assessmentId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return !await reader.ReadAsync(cancellationToken)
            ? null
            : new AssessmentEntitySnapshot(
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3));
    }

    private static async Task<CandidateSetSnapshot?> ReadCandidateSetAsync(
        SqlTransaction transaction,
        string candidateSetId,
        CancellationToken cancellationToken)
    {
        await using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT IsComplete, SupportCandidateCount, ResistanceCandidateCount,
                   TotalCandidateCount, MaterialDigest, CandidateMaterialDigest
            FROM dbo.market_situation_candidate_set
            WHERE Id = @candidateSetId;
            """;
        command.Parameters.Add("@candidateSetId", SqlDbType.Char, 64).Value = candidateSetId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return !await reader.ReadAsync(cancellationToken)
            ? null
            : new CandidateSetSnapshot(
                reader.GetBoolean(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetString(5));
    }

    private static async Task<int> CountMembersAsync(
        SqlTransaction transaction,
        string candidateSetId,
        CancellationToken cancellationToken)
    {
        await using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM dbo.market_situation_candidate_member
            WHERE CandidateSetId = @candidateSetId;
            """;
        command.Parameters.Add("@candidateSetId", SqlDbType.Char, 64).Value = candidateSetId;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static MarketSituationAssessmentEntity ToLegacyEntity(
        MarketSituationAssessment assessment,
        string json) =>
        new()
        {
            Id = assessment.Id,
            FeatureSnapshotId = assessment.FeatureSnapshotId,
            PriorSessionContextId = assessment.PriorSessionContextId,
            Symbol = assessment.Symbol,
            SessionId = assessment.SessionId,
            AsOfUtc = assessment.AsOfUtc,
            EvaluatedAtUtc = assessment.EvaluatedAtUtc,
            Status = assessment.Status.ToString(),
            ContextMode = assessment.ContextMode.ToString(),
            PrimarySituationType = assessment.PrimarySituation?.SituationType.ToString(),
            SupportSituationType = assessment.SupportInteraction?.SituationType.ToString(),
            ResistanceSituationType = assessment.ResistanceInteraction?.SituationType.ToString(),
            SupportLevelSource = assessment.SupportInteraction?.Level.Source.ToString(),
            ResistanceLevelSource = assessment.ResistanceInteraction?.Level.Source.ToString(),
            DefinitionVersion = assessment.DefinitionVersion,
            ConfigurationVersion = assessment.ConfigurationVersion,
            AssessmentJson = json
        };

    private static void Add(
        SqlCommand command,
        string name,
        SqlDbType type,
        object? value)
    {
        command.Parameters.Add(name, type).Value = value ?? DBNull.Value;
    }

    private static void Add(
        SqlCommand command,
        string name,
        SqlDbType type,
        int size,
        object? value)
    {
        command.Parameters.Add(name, type, size).Value = value ?? DBNull.Value;
    }

    private static void Add(
        SqlCommand command,
        string name,
        SqlDbType type,
        byte precision,
        byte scale,
        decimal value)
    {
        var parameter = command.Parameters.Add(name, type);
        parameter.Precision = precision;
        parameter.Scale = scale;
        parameter.Value = value;
    }

    private static async Task RollbackAsync(
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private sealed record CandidateSetWriteResult(
        bool SetCreated,
        bool SourcesCreated,
        int BatchCount);

    private sealed record AssessmentEntitySnapshot(
        string? PersistenceVersion,
        string? CandidateSetId,
        string? AssessmentMaterialDigest,
        string AssessmentJson);

    private sealed record CandidateSetSnapshot(
        bool IsComplete,
        int SupportCount,
        int ResistanceCount,
        int TotalCount,
        string MaterialDigest,
        string CandidateMaterialDigest);
}
