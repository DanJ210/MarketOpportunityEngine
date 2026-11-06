using System.Text.Json;
using MarketOpportunity.Analytics;
using MarketOpportunity.Application;
using MarketOpportunity.Domain;
using MarketOpportunity.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MarketOpportunity.IntegrationTests;

public sealed class MarketReactionPersistenceTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 5, 14, 30, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Episode_round_trips_advances_lifecycle_and_rejects_mutation_and_regression()
    {
        var connectionString = IntegrationTestConfiguration.GetSqlTestConnectionString();
        var token = Guid.NewGuid().ToString("N")[..16];
        var symbol = $"R{token}";
        var sessionId = $"reaction-session-{token}";
        var evaluator = new MarketReactionEvaluator();
        var (episode, initial) = CreateEpisode(evaluator, symbol, sessionId);
        var schemaReady = false;
        try
        {
            await EnsureSchemaAsync(connectionString);
            schemaReady = true;
            await PersistAsync(connectionString, initial);
            await PersistAsync(connectionString, initial);

            var crossed = evaluator.Evaluate(
                episode,
                [Trade(symbol, sessionId, 1, 99.89m)],
                Start.AddSeconds(1));
            await PersistAsync(connectionString, crossed);
            var advanced = evaluator.Evaluate(
                crossed.Episode,
                [Trade(symbol, sessionId, 2, 99.95m)],
                Start.AddSeconds(2));
            await PersistAsync(connectionString, advanced);

            var restored = await RestoreActiveAsync(connectionString, sessionId, [symbol]);
            var restoredEpisode = Assert.Single(restored);
            Assert.Equal(advanced.Episode, restoredEpisode);
            Assert.True(restoredEpisode.HasCrossedOppositeZone);
            Assert.Equal(Start.AddSeconds(2), restoredEpisode.LastObservationAtUtc);
            Assert.Equal(99.95m, restoredEpisode.LastObservationPrice);
            var storedLatest = await GetLatestAsOfAsync(connectionString, episode.Id);
            Assert.True(storedLatest.EqualsExact(Start.AddSeconds(2)));

            var changedGeometry = advanced with
            {
                Episode = advanced.Episode with { ZoneLow = 99.8m }
            };
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PersistAsync(connectionString, changedGeometry));

            var regressedObservation = advanced.Episode with
            {
                LastObservationAtUtc = Start,
                LastObservationIdentity = episode.StartObservationIdentity,
                LastObservationProvider = episode.StartObservationProvider,
                LastObservationSequenceId = episode.StartObservationSequenceId,
                LastObservationPrice = episode.LastObservationPrice
            };
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PersistAsync(connectionString, new MarketReactionEvaluation(
                    regressedObservation,
                    advanced.Assessment with
                    {
                        LastObservationAtUtc = Start,
                        LastObservationIdentity = episode.StartObservationIdentity,
                        LastObservationPrice = episode.LastObservationPrice
                    })));

            var revertedCross = advanced.Episode with { HasCrossedOppositeZone = false };
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PersistAsync(connectionString, new MarketReactionEvaluation(
                    revertedCross,
                    advanced.Assessment with { HasCrossedOppositeZone = false })));

            var confirmed = evaluator.Evaluate(
                advanced.Episode,
                [Trade(symbol, sessionId, 3, 100.200100000000000000000001m)],
                Start.AddSeconds(3));
            await PersistAsync(connectionString, confirmed);
            await PersistAsync(connectionString, confirmed);
            Assert.Equal(MarketReactionEpisodeStatus.Confirmed, confirmed.Episode.Status);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PersistAsync(connectionString, advanced));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PersistAsync(connectionString, confirmed with
                {
                    Assessment = confirmed.Assessment with { Id = Guid.NewGuid() }
                }));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PersistAsync(connectionString, confirmed with
                {
                    Episode = confirmed.Episode with { ReactionPrice = 100.2001m }
                }));

            await using var verification = CreateContext(connectionString);
            var row = await verification.LevelInteractionEpisodes.AsNoTracking()
                .SingleAsync(item => item.Id == episode.Id);
            Assert.Equal(nameof(MarketReactionEpisodeStatus.Confirmed), row.Status);
            Assert.Equal(confirmed.Episode, JsonSerializer.Deserialize<LevelInteractionEpisode>(row.EpisodeJson, JsonOptions));
            Assert.True(row.StartedAtUtc.EqualsExact(episode.StartedAtUtc));
            Assert.Equal(100.200100000000000000000001m,
                JsonSerializer.Deserialize<LevelInteractionEpisode>(row.EpisodeJson, JsonOptions)!.ReactionPrice);
        }
        finally
        {
            if (schemaReady)
            {
                await CleanupAsync(connectionString, [symbol]);
            }
        }
    }

    [Theory]
    [InlineData(MarketReactionType.SupportHeld)]
    [InlineData(MarketReactionType.SupportBreakdown)]
    [InlineData(MarketReactionType.SupportReclaimed)]
    [InlineData(MarketReactionType.ResistanceRejected)]
    [InlineData(MarketReactionType.ResistanceBreakout)]
    [InlineData(MarketReactionType.ResistanceFailedBreakout)]
    public async Task Immutable_reaction_assessment_round_trips_idempotently_and_rejects_collision(
        MarketReactionType reactionType)
    {
        var connectionString = IntegrationTestConfiguration.GetSqlTestConnectionString();
        var token = Guid.NewGuid().ToString("N")[..16];
        var symbol = $"R{token}";
        var sessionId = $"reaction-assessment-{token}";
        var evaluator = new MarketReactionEvaluator();
        var (episode, _) = CreateEpisode(
            evaluator,
            symbol,
            sessionId,
            reactionType is MarketReactionType.SupportHeld
                or MarketReactionType.SupportBreakdown
                or MarketReactionType.SupportReclaimed
                ? ImportantPriceLevelRole.Support
                : ImportantPriceLevelRole.Resistance);
        var observations = ReactionObservations(reactionType, symbol, sessionId);
        var evaluation = evaluator.Evaluate(episode, observations, Start.AddSeconds(2));
        var schemaReady = false;
        try
        {
            await EnsureSchemaAsync(connectionString);
            schemaReady = true;
            Assert.Equal(reactionType, evaluation.Assessment.Reaction);
            await PersistAsync(connectionString, evaluation);
            await PersistAsync(connectionString, evaluation);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PersistAsync(connectionString, evaluation with
                {
                    Assessment = evaluation.Assessment with { AsOfUtc = evaluation.Assessment.AsOfUtc.AddSeconds(1) }
                }));

            await using var verification = CreateContext(connectionString);
            var row = await verification.MarketReactionAssessments.AsNoTracking()
                .SingleAsync(item => item.Id == evaluation.Assessment.Id);
            Assert.Equal(reactionType.ToString(), row.ReactionType);
            Assert.Equal(1, await verification.MarketReactionAssessments
                .CountAsync(item => item.Symbol == symbol && item.SessionId == sessionId));
            var restored = JsonSerializer.Deserialize<MarketReactionAssessment>(row.AssessmentJson, JsonOptions);
            Assert.Equal(evaluation.Assessment, restored);
            Assert.Equal(observations[^1] is TradeEvent lastTrade ? lastTrade.Price : null,
                restored!.ReactionPrice);
            Assert.True(evaluation.Assessment.AsOfUtc.EqualsExact(restored.AsOfUtc));
            Assert.True(evaluation.Assessment.StartedAtUtc.EqualsExact(restored.StartedAtUtc));
            Assert.True(evaluation.Assessment.ExpiresAtUtc.EqualsExact(restored.ExpiresAtUtc));
            Assert.True(evaluation.Assessment.ReactionAtUtc!.Value.EqualsExact(restored.ReactionAtUtc!.Value));
            Assert.True(evaluation.Assessment.LastObservationAtUtc!.Value.EqualsExact(
                restored.LastObservationAtUtc!.Value));
            Assert.Equal(evaluation.Assessment.DefinitionVersion, restored.DefinitionVersion);
            Assert.Equal(evaluation.Assessment.ConfigurationVersion, restored.ConfigurationVersion);

            var episodeRow = await verification.LevelInteractionEpisodes.AsNoTracking()
                .SingleAsync(item => item.Id == evaluation.Assessment.EpisodeId);
            var restoredEpisode = JsonSerializer.Deserialize<LevelInteractionEpisode>(
                episodeRow.EpisodeJson,
                JsonOptions);
            Assert.Equal(evaluation.Assessment.LevelId, restoredEpisode!.LevelId);
            Assert.Equal(99.9m, restoredEpisode.ZoneLow);
            Assert.Equal(100.1m, restoredEpisode.ZoneHigh);
        }
        finally
        {
            if (schemaReady)
            {
                await CleanupAsync(connectionString, [symbol]);
            }
        }
    }

    [Fact]
    public async Task Restart_simulation_marks_only_current_session_unresolved_episode_unknown()
    {
        var connectionString = IntegrationTestConfiguration.GetSqlTestConnectionString();
        var token = Guid.NewGuid().ToString("N")[..16];
        var symbol = $"R{token}";
        var sessionId = $"restart-session-{token}";
        var otherSession = $"other-session-{token}";
        var evaluator = new MarketReactionEvaluator();
        var (episode, initial) = CreateEpisode(evaluator, symbol, sessionId);
        var (otherEpisode, otherInitial) = CreateEpisode(evaluator, symbol, otherSession);
        var schemaReady = false;
        try
        {
            await EnsureSchemaAsync(connectionString);
            schemaReady = true;

            // Process/context A persists the unresolved episode and its latest observed state.
            var latest = evaluator.Evaluate(
                episode,
                [Trade(symbol, sessionId, 1, 99.89m)],
                Start.AddSeconds(1));
            await PersistAsync(connectionString, initial);
            await PersistAsync(connectionString, latest);
            await PersistAsync(connectionString, otherInitial);

            IReadOnlyList<MarketReactionEvaluation> afterRestart;
            // Process/context B restores only the explicit current session and records broken continuity.
            await using (var contextB = CreateContext(connectionString))
            {
                var storeB = new EfMarketReactionStore(contextB);
                var service = new MarketReactionContinuityService(storeB, evaluator);
                afterRestart = await service.RestoreAfterRestartAsync(
                    sessionId,
                    [symbol],
                    CancellationToken.None);
            }

            var terminal = Assert.Single(afterRestart);
            Assert.Equal(episode.Id, terminal.Episode.Id);
            Assert.Equal(MarketReactionEpisodeStatus.ContinuityUnknown, terminal.Episode.Status);
            Assert.Equal(MarketReactionStatus.ContinuityUnknown, terminal.Assessment.Status);
            Assert.Equal(latest.Episode.LastObservationAtUtc, terminal.Assessment.AsOfUtc);

            await using (var verification = CreateContext(connectionString))
            {
                var currentSessionRows = await verification.LevelInteractionEpisodes.AsNoTracking()
                    .Where(item => item.Symbol == symbol && item.SessionId == sessionId)
                    .ToListAsync();
                Assert.Equal(nameof(MarketReactionEpisodeStatus.ContinuityUnknown),
                    Assert.Single(currentSessionRows).Status);
                Assert.Empty(await new EfMarketReactionStore(verification)
                    .RestoreActiveEpisodesAsync(sessionId, [symbol], CancellationToken.None));
                Assert.Single(await new EfMarketReactionStore(verification)
                    .RestoreActiveEpisodesAsync(otherSession, [symbol], CancellationToken.None));
            }

            var afterOfflineGap = evaluator.Evaluate(
                terminal.Episode,
                [Trade(symbol, sessionId, 2, 100.21m)],
                Start.AddSeconds(2));
            Assert.Equal(MarketReactionEpisodeStatus.ContinuityUnknown, afterOfflineGap.Episode.Status);
            Assert.Null(afterOfflineGap.Episode.Reaction);
            await PersistAsync(connectionString, afterOfflineGap);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PersistAsync(connectionString, new MarketReactionEvaluation(
                    terminal.Episode with { Status = MarketReactionEpisodeStatus.Active },
                    terminal.Assessment with { Status = MarketReactionStatus.Unresolved })));

            var fresh = evaluator.StartEpisode(
                Interaction(episode.LevelId, symbol, sessionId, episode.Role),
                Trade(symbol, sessionId, 3, 100m, Start.AddSeconds(3)),
                new ScannerConfiguration());
            Assert.NotEqual(episode.Id, fresh.Id);
            Assert.Equal(MarketReactionEpisodeStatus.Active, fresh.Status);
        }
        finally
        {
            if (schemaReady)
            {
                await CleanupAsync(connectionString, [symbol]);
            }
        }
    }

    [Fact]
    public async Task Confirmed_and_expired_assessments_restore_as_terminal_history()
    {
        var connectionString = IntegrationTestConfiguration.GetSqlTestConnectionString();
        var token = Guid.NewGuid().ToString("N")[..16];
        var symbol = $"R{token}";
        var sessionId = $"terminal-session-{token}";
        var evaluator = new MarketReactionEvaluator();
        var (confirmedEpisode, _) = CreateEpisode(evaluator, symbol, sessionId);
        var confirmed = evaluator.Evaluate(
            confirmedEpisode,
            [Trade(symbol, sessionId, 1, 100.2001m)],
            Start.AddSeconds(1));
        var (expiredEpisode, _) = CreateEpisode(evaluator, symbol, sessionId, levelId: Guid.NewGuid());
        var expired = evaluator.Evaluate(expiredEpisode, [], expiredEpisode.ExpiresAtUtc);
        var (unknownEpisode, _) = CreateEpisode(evaluator, symbol, sessionId, levelId: Guid.NewGuid());
        var unknown = evaluator.MarkContinuityUnknown(unknownEpisode);
        var schemaReady = false;
        try
        {
            await EnsureSchemaAsync(connectionString);
            schemaReady = true;
            await PersistAsync(connectionString, confirmed);
            await PersistAsync(connectionString, expired);
            await PersistAsync(connectionString, unknown);

            await using var context = CreateContext(connectionString);
            var store = new EfMarketReactionStore(context);
            Assert.Empty(await store.RestoreActiveEpisodesAsync(sessionId, [symbol], CancellationToken.None));
            var confirmedAssessment = await context.MarketReactionAssessments.AsNoTracking()
                .SingleAsync(item => item.Id == confirmed.Assessment.Id);
            var expiredAssessment = await context.MarketReactionAssessments.AsNoTracking()
                .SingleAsync(item => item.Id == expired.Assessment.Id);
            var unknownAssessment = await context.MarketReactionAssessments.AsNoTracking()
                .SingleAsync(item => item.Id == unknown.Assessment.Id);
            Assert.Equal(confirmed.Assessment,
                JsonSerializer.Deserialize<MarketReactionAssessment>(confirmedAssessment.AssessmentJson, JsonOptions));
            Assert.Equal(expired.Assessment,
                JsonSerializer.Deserialize<MarketReactionAssessment>(expiredAssessment.AssessmentJson, JsonOptions));
            Assert.Equal(unknown.Assessment,
                JsonSerializer.Deserialize<MarketReactionAssessment>(unknownAssessment.AssessmentJson, JsonOptions));

            var repeatedConfirmed = evaluator.Evaluate(
                confirmed.Episode,
                [Trade(symbol, sessionId, 2, 99m)],
                Start.AddSeconds(2));
            Assert.Equal(confirmed.Episode, repeatedConfirmed.Episode);
            Assert.Equal(confirmed.Assessment, repeatedConfirmed.Assessment);
            var repeatedExpired = evaluator.Evaluate(
                expired.Episode,
                [Trade(symbol, sessionId, 3, 100.21m, expiredEpisode.ExpiresAtUtc.AddSeconds(1))],
                expiredEpisode.ExpiresAtUtc.AddSeconds(1));
            Assert.Equal(expired.Episode, repeatedExpired.Episode);
            Assert.Equal(expired.Assessment, repeatedExpired.Assessment);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PersistAsync(connectionString, new MarketReactionEvaluation(
                    confirmed.Episode with { Status = MarketReactionEpisodeStatus.Active },
                    confirmed.Assessment with { Status = MarketReactionStatus.Unresolved })));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PersistAsync(connectionString, new MarketReactionEvaluation(
                    expired.Episode with { Status = MarketReactionEpisodeStatus.Active },
                    expired.Assessment with { Status = MarketReactionStatus.Unresolved })));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PersistAsync(connectionString, new MarketReactionEvaluation(
                    unknown.Episode with { Status = MarketReactionEpisodeStatus.Active },
                    unknown.Assessment with { Status = MarketReactionStatus.Unresolved })));
        }
        finally
        {
            if (schemaReady)
            {
                await CleanupAsync(connectionString, [symbol]);
            }
        }
    }

    private static async Task EnsureSchemaAsync(string connectionString)
    {
        await using var context = CreateContext(connectionString);
        if (!await context.Database.EnsureCreatedAsync())
        {
            await context.Database.MigrateAsync();
        }
    }

    private static async Task PersistAsync(
        string connectionString,
        MarketReactionEvaluation evaluation)
    {
        await using var context = CreateContext(connectionString);
        await new EfMarketReactionStore(context).PersistAsync(evaluation, CancellationToken.None);
    }

    private static async Task<IReadOnlyList<LevelInteractionEpisode>> RestoreActiveAsync(
        string connectionString,
        string sessionId,
        IReadOnlyCollection<string> symbols)
    {
        await using var context = CreateContext(connectionString);
        return await new EfMarketReactionStore(context)
            .RestoreActiveEpisodesAsync(sessionId, symbols, CancellationToken.None);
    }

    private static async Task<DateTimeOffset> GetLatestAsOfAsync(string connectionString, Guid episodeId)
    {
        await using var context = CreateContext(connectionString);
        return await context.LevelInteractionEpisodes.AsNoTracking()
            .Where(item => item.Id == episodeId)
            .Select(item => item.LatestAsOfUtc)
            .SingleAsync();
    }

    private static MarketOpportunityDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<MarketOpportunityDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new MarketOpportunityDbContext(options);
    }

    private static async Task CleanupAsync(string connectionString, IReadOnlyCollection<string> symbols)
    {
        await using var context = CreateContext(connectionString);
        await context.MarketReactionAssessments.Where(item => symbols.Contains(item.Symbol)).ExecuteDeleteAsync();
        await context.LevelInteractionEpisodes.Where(item => symbols.Contains(item.Symbol)).ExecuteDeleteAsync();
    }

    private static (LevelInteractionEpisode Episode, MarketReactionEvaluation Initial) CreateEpisode(
        MarketReactionEvaluator evaluator,
        string symbol,
        string sessionId,
        ImportantPriceLevelRole role = ImportantPriceLevelRole.Support,
        DateTimeOffset? startedAtUtc = null,
        Guid? levelId = null)
    {
        var started = startedAtUtc ?? Start;
        var episode = evaluator.StartEpisode(
            Interaction(levelId ?? Guid.NewGuid(), symbol, sessionId, role),
            Trade(symbol, sessionId, 0, 100m, started),
            new ScannerConfiguration());
        return (episode, evaluator.Evaluate(episode, [], started));
    }

    private static MarketSituationInteraction Interaction(
        Guid levelId,
        string symbol,
        string sessionId,
        ImportantPriceLevelRole role)
    {
        var level = new ImportantPriceLevel(
            levelId,
            symbol,
            sessionId,
            sessionId,
            100m,
            role,
            role == ImportantPriceLevelRole.Support
                ? ImportantPriceLevelSource.CurrentSwingLow
                : ImportantPriceLevelSource.CurrentSwingHigh,
            Guid.NewGuid(),
            null,
            "source-event",
            "confirmation-event",
            Start.AddMinutes(-1),
            Start,
            MarketSituationContextMode.CurrentSessionOnly,
            "Complete current-session retained event history",
            "IntegrationFixture",
            null,
            null,
            "session-market-structure-v1",
            "features-v16",
            null);
        return new MarketSituationInteraction(
            role == ImportantPriceLevelRole.Support
                ? MarketSituationType.TestingSupport
                : MarketSituationType.TestingResistance,
            level,
            99.9m,
            100.1m,
            0m,
            0m,
            0m,
            0m,
            0m);
    }

    private static MarketEvent[] ReactionObservations(
        MarketReactionType reaction,
        string symbol,
        string sessionId)
    {
        var confirmingPrice = reaction switch
        {
            MarketReactionType.SupportHeld or MarketReactionType.SupportReclaimed
                or MarketReactionType.ResistanceBreakout => 100.200100000000000000000001m,
            MarketReactionType.SupportBreakdown or MarketReactionType.ResistanceRejected
                or MarketReactionType.ResistanceFailedBreakout => 99.8001m,
            _ => throw new ArgumentOutOfRangeException(nameof(reaction))
        };
        return reaction switch
        {
            MarketReactionType.SupportReclaimed => [
                Trade(symbol, sessionId, 1, 99.89m),
                Trade(symbol, sessionId, 2, confirmingPrice)],
            MarketReactionType.ResistanceFailedBreakout => [
                Trade(symbol, sessionId, 1, 100.11m),
                Trade(symbol, sessionId, 2, confirmingPrice)],
            _ => [Trade(symbol, sessionId, 1, confirmingPrice)]
        };
    }

    private static TradeEvent Trade(
        string symbol,
        string sessionId,
        int seconds,
        decimal price,
        DateTimeOffset? eventTimeUtc = null) =>
        new(
            symbol,
            sessionId,
            eventTimeUtc ?? Start.AddSeconds(seconds),
            (eventTimeUtc ?? Start.AddSeconds(seconds)).AddMilliseconds(1),
            "IntegrationFixture",
            $"trade-{Guid.NewGuid():N}",
            price,
            10);
}
