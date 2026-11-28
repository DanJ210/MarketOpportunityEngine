using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MarketOpportunity.Analytics;
using MarketOpportunity.Application;
using MarketOpportunity.Domain;
using MarketOpportunity.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MarketOpportunity.IntegrationTests;

public sealed class MarketSituationV2PersistenceTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 11, 27, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Normalized_candidate_set_and_compact_assessment_round_trip_idempotently()
    {
        var (connectionString, databaseName) = GetTestConnection();
        var token = Guid.NewGuid().ToString("N")[..12];
        var evaluation = Evaluation($"V2{token}", $"session-{token}", 8);
        var schemaReady = false;
        try
        {
            await PrepareSchemaAsync(connectionString, databaseName);
            schemaReady = true;
            await using (var context = CreateContext(connectionString))
            {
                var store = new EfMarketSituationStore(
                    context,
                    new MarketSituationPersistenceOptions { CandidateMemberBatchSize = 3 },
                    TimeProvider.System);
                var first = await store.PersistV2Async(evaluation, CancellationToken.None);
                var replay = await store.PersistV2Async(
                    evaluation with
                    {
                        Assessment = evaluation.Assessment with
                        {
                            EvaluatedAtUtc = evaluation.Assessment.EvaluatedAtUtc.AddMinutes(1)
                        }
                    },
                    CancellationToken.None);

                Assert.True(first.CandidateSetCreated);
                Assert.True(first.CandidateSourcesCreated);
                Assert.True(first.AssessmentCreated);
                Assert.Equal(3, first.MemberBatchCount);
                Assert.Equal(evaluation.CandidateSet.TotalCandidateCount, first.CandidateCount);
                Assert.True(first.CompactAssessmentBytes <= 16 * 1024);
                Assert.False(replay.CandidateSetCreated);
                Assert.False(replay.AssessmentCreated);
                Assert.Equal(0, replay.MemberBatchCount);

                Assert.Equal(
                    evaluation.CandidateSet.TotalCandidateCount,
                    await context.MarketSituationCandidateMembers.CountAsync(
                        item => item.CandidateSetId == evaluation.CandidateSet.CandidateSetId));
                Assert.Equal(
                    1,
                    await context.MarketSituationCandidateSets.CountAsync(
                        item => item.Id == evaluation.CandidateSet.CandidateSetId && item.IsComplete));
                Assert.Equal(
                    1,
                    await context.MarketSituationAssessments.CountAsync(
                        item => item.Id == evaluation.Assessment.Id));
                Assert.DoesNotContain(
                    "EligibleLevels",
                    await context.MarketSituationAssessments
                        .Where(item => item.Id == evaluation.Assessment.Id)
                        .Select(item => item.AssessmentJson)
                        .SingleAsync(),
                    StringComparison.OrdinalIgnoreCase);
            }

            await using (var readContext = CreateContext(connectionString))
            {
                var store = new EfMarketSituationStore(readContext);
                var read = await store.ReadAsync(
                    evaluation.Assessment.Id,
                    CancellationToken.None);
                Assert.NotNull(read);
                Assert.Equal(
                    CompactMarketSituationAssessment.CurrentPersistenceContractVersion,
                    read.PersistenceVersion);
                Assert.Null(read.LegacyAssessment);
                Assert.NotNull(read.CompactAssessment);
                Assert.Equal(
                    evaluation.Assessment.CandidateSetId,
                    read.CompactAssessment.CandidateSetId);
                Assert.Equal(
                    evaluation.CandidateSet.TotalCandidateCount,
                    read.CandidateSet!.TotalCandidateCount);
                Assert.True(read.CandidateSet.IsComplete);
                var supportInteraction = read.CompactAssessment.SupportInteraction!;
                var pinned = supportInteraction.WithLevel(
                    read.CompactAssessment.NearestSupportLevel!);
                var observation = Trade(
                    evaluation.Assessment.Symbol,
                    evaluation.Assessment.SessionId,
                    100m,
                    1);
                var episode = new MarketReactionEvaluator().StartEpisode(
                    pinned,
                    observation,
                    new ScannerConfiguration());
                Assert.Equal(pinned.Level.Id, episode.LevelId);
                Assert.Equal(pinned.Level.Role, episode.Role);
                Assert.Equal(pinned.Level.Price, episode.LevelPrice);
                Assert.Equal(pinned.Level.SourceSessionId, episode.LevelSourceSessionId);
                Assert.Equal(pinned.Level.HistoricalProvider, episode.HistoricalProvider);
                Assert.Equal(pinned.Level.HistoricalFeed, episode.HistoricalFeed);
            }
        }
        finally
        {
            if (schemaReady)
            {
                await CleanupAsync(connectionString, evaluation);
            }
        }
    }

    [Fact]
    public async Task Candidate_evidence_pages_in_canonical_order_and_resolves_selected_members()
    {
        var (connectionString, databaseName) = GetTestConnection();
        var token = Guid.NewGuid().ToString("N")[..12];
        var evaluation = Evaluation($"PG{token}", $"session-{token}", 17);
        var schemaReady = false;
        try
        {
            await PrepareSchemaAsync(connectionString, databaseName);
            schemaReady = true;
            await using (var context = CreateContext(connectionString))
            {
                var store = new EfMarketSituationStore(context);
                await store.PersistV2Async(evaluation, CancellationToken.None);
                var ordinals = new List<int>();
                var afterOrdinal = -1;
                do
                {
                    var page = await store.ReadCandidatePageAsync(
                        evaluation.Assessment.Id,
                        afterOrdinal,
                        4,
                        CancellationToken.None);
                    Assert.NotNull(page);
                    Assert.Equal(evaluation.CandidateSet.CandidateSetId, page.CandidateSetId);
                    Assert.Equal(17, page.TotalCandidateCount);
                    Assert.All(
                        page.Members,
                        member => Assert.Contains(member.CandidateSourceId, page.Sources.Keys));
                    ordinals.AddRange(page.Members.Select(item => item.Ordinal));
                    afterOrdinal = page.NextAfterOrdinal ?? afterOrdinal;
                    if (!page.HasMore)
                    {
                        break;
                    }
                } while (true);

                Assert.Equal(Enumerable.Range(0, 17), ordinals);
                Assert.Null(await store.ReadCandidatePageAsync(
                    Guid.NewGuid(),
                    -1,
                    4,
                    CancellationToken.None));
                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                    store.ReadCandidatePageAsync(
                        evaluation.Assessment.Id,
                        -1,
                        65,
                        CancellationToken.None));
                await Assert.ThrowsAsync<ArgumentException>(() =>
                    store.ReadSelectedCandidateAsync(
                        "not-a-candidate-set",
                        Guid.NewGuid(),
                        CancellationToken.None));

                var selected = await store.ReadSelectedCandidateAsync(
                    evaluation.CandidateSet.CandidateSetId,
                    evaluation.Assessment.NearestSupportLevel!.Id,
                    CancellationToken.None);
                Assert.NotNull(selected);
                Assert.Equal(
                    evaluation.Assessment.NearestSupportLevel.Id,
                    selected.Member.LevelId);
                Assert.Equal(selected.Source.Id, selected.Member.CandidateSourceId);
                Assert.Equal(
                    evaluation.Assessment.NearestSupportLevel.Price,
                    selected.Member.Price);
                Assert.Null(await store.ReadSelectedCandidateAsync(
                    evaluation.CandidateSet.CandidateSetId,
                    Guid.NewGuid(),
                    CancellationToken.None));
            }
        }
        finally
        {
            if (schemaReady)
            {
                await CleanupAsync(connectionString, evaluation);
            }
        }
    }

    [Fact]
    public async Task Legacy_candidate_evidence_reports_explicitly_without_normalized_backfill()
    {
        var (connectionString, databaseName) = GetTestConnection();
        var token = Guid.NewGuid().ToString("N")[..12];
        var evaluation = Evaluation($"LV{token}", $"session-{token}", 3);
        var legacy = evaluation.ProjectLegacy();
        var schemaReady = false;
        try
        {
            await PrepareSchemaAsync(connectionString, databaseName);
            schemaReady = true;
            await using (var context = CreateContext(connectionString))
            {
                await new EfMarketSituationStore(context).PersistAsync(
                    legacy,
                    CancellationToken.None);
            }

            await using (var readContext = CreateContext(connectionString))
            {
                var store = new EfMarketSituationStore(readContext);
                var exception = await Assert.ThrowsAsync<MarketSituationEvidenceReadException>(
                    () => store.ReadCandidatePageAsync(
                        legacy.Id,
                        -1,
                        4,
                        CancellationToken.None));
                Assert.Equal(
                    MarketSituationEvidenceReadFailure.LegacyCandidateEvidenceUnavailable,
                    exception.Failure);
                Assert.Equal(
                    0,
                    await readContext.MarketSituationCandidateSets
                        .CountAsync(
                            item => item.Id == evaluation.CandidateSet.CandidateSetId,
                            CancellationToken.None));
                Assert.Equal(
                    0,
                    await readContext.MarketSituationCandidateMembers
                        .CountAsync(
                            item => item.CandidateSetId == evaluation.CandidateSet.CandidateSetId,
                            CancellationToken.None));
            }
        }
        finally
        {
            if (schemaReady)
            {
                await CleanupAsync(connectionString, evaluation);
            }
        }
    }

    [Fact]
    public async Task Partial_member_batch_failure_rolls_back_and_retries_safely()
    {
        var (connectionString, databaseName) = GetTestConnection();
        var token = Guid.NewGuid().ToString("N")[..12];
        var evaluation = Evaluation($"BF{token}", $"session-{token}", 7);
        var schemaReady = false;
        try
        {
            await PrepareSchemaAsync(connectionString, databaseName);
            schemaReady = true;
            await using (var failedContext = CreateContext(connectionString))
            {
                var store = new EfMarketSituationStore(
                    failedContext,
                    new MarketSituationPersistenceOptions { CandidateMemberBatchSize = 3 },
                    TimeProvider.System,
                    new FailAfterBatch(2));
                var exception = await Assert.ThrowsAsync<MarketSituationPersistenceException>(() =>
                    store.PersistV2Async(evaluation, CancellationToken.None));
                Assert.Equal(
                    MarketSituationPersistenceFailure.TransactionFailed,
                    exception.Failure);
            }

            await using (var verifyContext = CreateContext(connectionString))
            {
                Assert.Equal(
                    0,
                    await verifyContext.MarketSituationAssessments.CountAsync(
                        item => item.Id == evaluation.Assessment.Id));
                Assert.Equal(
                    0,
                    await verifyContext.MarketSituationCandidateSets.CountAsync(
                        item => item.Id == evaluation.CandidateSet.CandidateSetId));
                Assert.Equal(
                    0,
                    await verifyContext.MarketSituationCandidateMembers.CountAsync(
                        item => item.CandidateSetId == evaluation.CandidateSet.CandidateSetId));
            }

            await using (var cancelledContext = CreateContext(connectionString))
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    new EfMarketSituationStore(
                        cancelledContext,
                        new MarketSituationPersistenceOptions { CandidateMemberBatchSize = 3 },
                        TimeProvider.System,
                        new CancelAfterBatch(1))
                        .PersistV2Async(evaluation, CancellationToken.None));
            }

            await using (var cancelledVerifyContext = CreateContext(connectionString))
            {
                Assert.Empty(await cancelledVerifyContext.MarketSituationCandidateSets
                    .Where(item => item.Id == evaluation.CandidateSet.CandidateSetId)
                    .ToArrayAsync());
            }

            await using (var retryContext = CreateContext(connectionString))
            {
                var retry = await new EfMarketSituationStore(
                    retryContext,
                    new MarketSituationPersistenceOptions { CandidateMemberBatchSize = 3 },
                    TimeProvider.System).PersistV2Async(evaluation, CancellationToken.None);
                Assert.True(retry.CandidateSetCreated);
                Assert.True(retry.AssessmentCreated);
                Assert.Equal(3, retry.MemberBatchCount);
            }
        }
        finally
        {
            if (schemaReady)
            {
                await CleanupAsync(connectionString, evaluation);
            }
        }
    }

    [Fact]
    public async Task Persisted_conflicts_are_detected_without_overwrite()
    {
        var (connectionString, databaseName) = GetTestConnection();
        var token = Guid.NewGuid().ToString("N")[..12];
        var evaluation = Evaluation($"CF{token}", $"session-{token}", 4);
        var schemaReady = false;
        try
        {
            await PrepareSchemaAsync(connectionString, databaseName);
            schemaReady = true;
            await using (var context = CreateContext(connectionString))
            {
                var store = new EfMarketSituationStore(
                    context,
                    new MarketSituationPersistenceOptions { CandidateMemberBatchSize = 2 },
                    TimeProvider.System);
                await store.PersistV2Async(evaluation, CancellationToken.None);

                var source = await context.MarketSituationCandidateSources
                    .FirstAsync(item => item.Symbol == evaluation.Assessment.Symbol);
                source.SourceCompleteness = "conflicting-completeness";
                await context.SaveChangesAsync();
                var sourceConflict = await Assert.ThrowsAsync<MarketSituationPersistenceException>(() =>
                    store.PersistV2Async(evaluation, CancellationToken.None));
                Assert.Equal(
                    MarketSituationPersistenceFailure.CandidateSourceConflict,
                    sourceConflict.Failure);
                source.SourceCompleteness = "Complete prior-session history";
                await context.SaveChangesAsync();

                var member = await context.MarketSituationCandidateMembers
                    .OrderBy(item => item.Ordinal)
                    .FirstAsync(item => item.CandidateSetId == evaluation.CandidateSet.CandidateSetId);
                var originalPrice = member.PriceCanonical;
                member.PriceCanonical = "999";
                await context.SaveChangesAsync();
                var memberConflict = await Assert.ThrowsAsync<MarketSituationPersistenceException>(() =>
                    store.PersistV2Async(evaluation, CancellationToken.None));
                Assert.Equal(
                    MarketSituationPersistenceFailure.CandidateMemberConflict,
                    memberConflict.Failure);
                member.PriceCanonical = originalPrice;
                await context.SaveChangesAsync();

                var header = await context.MarketSituationCandidateSets
                    .SingleAsync(item => item.Id == evaluation.CandidateSet.CandidateSetId);
                header.TotalCandidateCount++;
                await context.SaveChangesAsync();
                var digestConflict = await Assert.ThrowsAsync<MarketSituationPersistenceException>(() =>
                    store.PersistV2Async(evaluation, CancellationToken.None));
                Assert.Equal(
                    MarketSituationPersistenceFailure.CandidateDigestMismatch,
                    digestConflict.Failure);
                header.TotalCandidateCount--;
                await context.SaveChangesAsync();

                var assessment = await context.MarketSituationAssessments
                    .SingleAsync(item => item.Id == evaluation.Assessment.Id);
                var stored = JsonSerializer.Deserialize<CompactMarketSituationAssessment>(
                    assessment.AssessmentJson)!;
                assessment.AssessmentJson = JsonSerializer.Serialize(
                    stored with { UnavailableReason = "conflicting reason" },
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                await context.SaveChangesAsync();
                var assessmentConflict = await Assert.ThrowsAsync<MarketSituationPersistenceException>(() =>
                    store.PersistV2Async(evaluation, CancellationToken.None));
                Assert.Equal(
                    MarketSituationPersistenceFailure.CompactAssessmentConflict,
                    assessmentConflict.Failure);
            }
        }
        finally
        {
            if (schemaReady)
            {
                await CleanupAsync(connectionString, evaluation);
            }
        }
    }

    [Fact]
    public async Task Large_candidate_set_uses_bounded_batches_and_keeps_compact_json_small()
    {
        var (connectionString, databaseName) = GetTestConnection();
        var token = Guid.NewGuid().ToString("N")[..12];
        var evaluation = Evaluation($"LG{token}", $"session-{token}", 100_005);
        var schemaReady = false;
        try
        {
            await PrepareSchemaAsync(connectionString, databaseName);
            schemaReady = true;
            MarketSituationPersistenceWriteResult result;
            MarketSituationPersistenceWriteResult replay;
            TimeSpan firstElapsed;
            TimeSpan reuseElapsed;
            await using (var context = CreateContext(connectionString))
            {
                var store = new EfMarketSituationStore(
                    context,
                    new MarketSituationPersistenceOptions { CandidateMemberBatchSize = 1_000 },
                    TimeProvider.System);
                var firstStopwatch = Stopwatch.StartNew();
                result = await store.PersistV2Async(evaluation, CancellationToken.None);
                firstElapsed = firstStopwatch.Elapsed;
                Assert.Equal(                100_005, await context.MarketSituationCandidateMembers.CountAsync(
                    item => item.CandidateSetId == evaluation.CandidateSet.CandidateSetId));
                var replayStopwatch = Stopwatch.StartNew();
                replay = await store.PersistV2Async(evaluation, CancellationToken.None);
                reuseElapsed = replayStopwatch.Elapsed;
                var page = await store.ReadCandidatePageAsync(
                    evaluation.Assessment.Id,
                    -1,
                    64,
                    CancellationToken.None);
                Assert.NotNull(page);
                Assert.Equal(64, page.ReturnedCount);
                Assert.True(page.HasMore);
                var pageBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(page));
                Assert.True(pageBytes <= 64 * 1024);
                Console.WriteLine(
                    $"Large MarketSituation forensic page: {page.ReturnedCount} members, " +
                    $"{pageBytes} bytes.");
            }

            Assert.True(result.CandidateSetCreated);
            Assert.Equal(101, result.MemberBatchCount);
            Assert.Equal(100_005, result.CandidateCount);
            Assert.True(result.CompactAssessmentBytes < 16 * 1024);
            Assert.Equal(result.CompactAssessmentBytes, result.LargestCompactParameterBytes);
            Assert.False(replay.CandidateSetCreated);
            Assert.False(replay.AssessmentCreated);
            Assert.Equal(0, replay.MemberBatchCount);
            Assert.True(firstElapsed < TimeSpan.FromMinutes(2));
            Assert.True(reuseElapsed < TimeSpan.FromMinutes(2));
            Console.WriteLine(
                $"Large MarketSituation SQL persistence: {result.CandidateCount} candidates, " +
                $"{result.MemberBatchCount} batches, first={firstElapsed.TotalSeconds:F3}s, " +
                $"reuse={reuseElapsed.TotalSeconds:F3}s, " +
                $"{result.CompactAssessmentBytes} compact bytes.");
        }
        finally
        {
            if (schemaReady)
            {
                await CleanupAsync(connectionString, evaluation);
            }
        }
    }

    private static (string ConnectionString, string DatabaseName) GetTestConnection()
    {
        var connectionString = IntegrationTestConfiguration.GetSqlTestConnectionString();
        return (
            connectionString,
            new SqlConnectionStringBuilder(connectionString).InitialCatalog);
    }

    private static async Task PrepareSchemaAsync(
        string connectionString,
        string expectedDatabaseName)
    {
        await using var verification = new SqlConnection(connectionString);
        await verification.OpenAsync();
        await using var databaseCommand = verification.CreateCommand();
        databaseCommand.CommandText = "SELECT DB_NAME();";
        Assert.Equal(
            expectedDatabaseName,
            Convert.ToString(await databaseCommand.ExecuteScalarAsync()));
        await using var context = CreateContext(connectionString);
        if (!await context.Database.EnsureCreatedAsync())
        {
            await context.Database.MigrateAsync();
        }
    }

    private static MarketOpportunityDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<MarketOpportunityDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new MarketOpportunityDbContext(options);
    }

    private static MarketSituationEvaluation Evaluation(
        string symbol,
        string sessionId,
        int candidateCount)
    {
        var configuration = new ScannerConfiguration();
        var events = new MarketEvent[] { Trade(symbol, sessionId, 100m, 1) };
        var state = new SymbolEventState(symbol, sessionId, configuration.MaximumStateEvents);
        state.Add(events[0]);
        var snapshot = new FeatureCalculator().Calculate(
            state,
            events[0].EventTimeUtc,
            configuration,
            new MarketDataCapabilities(
                "IntegrationFixture",
                "Synthetic",
                MarketDataCoverage.Synthetic,
                false,
                true,
                true));
        var supports = Enumerable.Range(1, candidateCount - 1)
            .Select(index => Candidate(
                symbol,
                StructuralLevelType.Support,
                index == 1 ? 99.95m : 200m + index * 0.01m,
                index))
            .ToArray();
        var context = new PriorSessionContextBuilder(new FeatureCalculator())
            .BuildUnavailable(symbol, sessionId, "Integration prior context unavailable.") with
            {
                PriorSessionId = "prior-session",
                PriorSessionStartUtc = Start.AddDays(-1),
                PriorSessionEndUtc = Start.AddDays(-1).AddHours(6),
                PriorSessionHigh = null,
                PriorSessionLow = null,
                SupportCandidates = supports,
                ResistanceCandidates =
                [
                    Candidate(symbol, StructuralLevelType.Resistance, 100.2m, candidateCount)
                ],
                Completeness = PriorSessionContextStatus.Available,
                CompletenessReason = "Complete integration prior-session history.",
                MarketData = new MarketDataCapabilities(
                    "IntegrationHistory",
                    "sip",
                    MarketDataCoverage.ProviderDefined,
                    false,
                    true,
                    false),
                HistoricalSource = "integration-history",
                CoverageStartUtc = Start.AddDays(-1),
                CoverageEndUtc = Start.AddDays(-1).AddHours(6),
                IsSessionStartCovered = true,
                IsSessionEndCovered = true,
                HasKnownGaps = false
            };
        var view = new PriorSessionContextView(
            context,
            snapshot.ReferencePrice,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
        return _evaluator.EvaluateWithEvidence(
            snapshot,
            events,
            view,
            configuration,
            snapshot.AsOfUtc);
    }

    private static StructuralLevelCandidate Candidate(
        string symbol,
        StructuralLevelType type,
        decimal price,
        int id)
    {
        var swingId = new Guid(id, 0, 0, new byte[8]);
        return new StructuralLevelCandidate(
            swingId,
            type,
            symbol,
            "prior-session",
            price,
            Start.AddDays(-1).AddMinutes(id % 10_000),
            Start.AddDays(-1).AddMinutes(id % 10_000 + 2),
            swingId,
            $"origin-{id}",
            $"confirmation-{id}",
            "features-v16",
            "session-swing-fractal-v1");
    }

    private static TradeEvent Trade(
        string symbol,
        string sessionId,
        decimal price,
        int index) =>
        new(
            symbol,
            sessionId,
            Start.AddSeconds(index),
            Start.AddSeconds(index + 1),
            "IntegrationFixture",
            $"trade-{index:D3}",
            price,
            10);

    private static async Task CleanupAsync(
        string connectionString,
        MarketSituationEvaluation evaluation)
    {
        await using var context = CreateContext(connectionString);
        var sourceIds = MarketSituationPersistenceMapper.Sources(evaluation.CandidateSet)
            .Select(source => source.CandidateSourceId)
            .ToArray();
        await context.MarketSituationAssessments
            .Where(item => item.Id == evaluation.Assessment.Id)
            .ExecuteDeleteAsync();
        await context.MarketSituationCandidateMembers
            .Where(item => item.CandidateSetId == evaluation.CandidateSet.CandidateSetId)
            .ExecuteDeleteAsync();
        await context.MarketSituationCandidateSets
            .Where(item => item.Id == evaluation.CandidateSet.CandidateSetId)
            .ExecuteDeleteAsync();
        await context.MarketSituationCandidateSources
            .Where(item => sourceIds.Contains(item.Id))
            .ExecuteDeleteAsync();
    }

    private static readonly MarketSituationEvaluator _evaluator = new();

    private sealed class FailAfterBatch(int failAt) : IMarketSituationPersistenceFaultInjector
    {
        public Task AfterMemberBatchAsync(
            int batchNumber,
            int persistedMemberCount,
            CancellationToken cancellationToken)
        {
            if (batchNumber == failAt)
            {
                throw new InvalidOperationException(
                    $"Injected member batch failure after {persistedMemberCount} members.");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class CancelAfterBatch(int cancelAt) : IMarketSituationPersistenceFaultInjector
    {
        public Task AfterMemberBatchAsync(
            int batchNumber,
            int persistedMemberCount,
            CancellationToken cancellationToken)
        {
            if (batchNumber == cancelAt)
            {
                throw new OperationCanceledException(
                    $"Injected cancellation after {persistedMemberCount} members.");
            }

            return Task.CompletedTask;
        }
    }
}
