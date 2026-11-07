using MarketOpportunity.Analytics;
using MarketOpportunity.Application;
using MarketOpportunity.Domain;

namespace MarketOpportunity.UnitTests;

public sealed class MarketReactionTrackerTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 6, 14, 30, 0, TimeSpan.Zero);
    private readonly MarketReactionEvaluator _evaluator = new();
    private readonly ScannerConfiguration _configuration = new();

    [Fact]
    public async Task Tracks_simultaneous_support_and_resistance_episodes_and_captures_non_gating_context()
    {
        var store = new InMemoryReactionStore();
        var tracker = new MarketReactionTracker(_evaluator, store);
        var initialTrade = Trade("session-one", 0, 100.05m);
        var initialSituation = Situation(
            "session-one",
            Start,
            SupportInteraction(100m, 99.9m, 100.1m),
            ResistanceInteraction(100.2m, 100m, 100.3m));

        var unresolved = await tracker.ObserveAsync(
            initialSituation,
            [initialTrade],
            _configuration,
            CaptureContext,
            CancellationToken.None);

        Assert.Equal(2, unresolved.Count);
        Assert.All(unresolved, item => Assert.Equal(MarketReactionStatus.Unresolved, item.Assessment.Status));
        Assert.All(unresolved, item => Assert.NotNull(item.ResearchContext));

        var confirmed = await tracker.ObserveAsync(
            Situation(
                "session-one",
                Start.AddSeconds(2),
                initialSituation.SupportInteraction,
                initialSituation.ResistanceInteraction),
            [Trade("session-one", 1, 100.5m)],
            _configuration,
            CaptureContext,
            CancellationToken.None);

        Assert.Equal(2, confirmed.Count);
        Assert.Contains(confirmed, item => item.Assessment.Reaction == MarketReactionType.SupportHeld);
        Assert.Contains(confirmed, item => item.Assessment.Reaction == MarketReactionType.ResistanceBreakout);
        Assert.All(confirmed, item => Assert.Equal(MarketReactionStatus.Confirmed, item.Assessment.Status));
        Assert.Empty(tracker.Read("SYM", "another-session"));
        Assert.Equal(4, store.Persisted.Count);
    }

    [Fact]
    public async Task Restart_marks_only_matching_session_episodes_unknown_and_never_confirms_them()
    {
        var store = new InMemoryReactionStore();
        var firstTracker = new MarketReactionTracker(_evaluator, store);
        var situation = Situation("session-one", Start, SupportInteraction(100m, 99.9m, 100.1m), null);
        await firstTracker.ObserveAsync(
            situation,
            [Trade("session-one", 0, 100.05m)],
            _configuration,
            CaptureContext,
            CancellationToken.None);
        var started = Assert.Single(firstTracker.Read("SYM", "session-one"));

        var restartedTracker = new MarketReactionTracker(_evaluator, store);
        await restartedTracker.RestoreAfterRestartAsync(
            "session-one",
            "SYM",
            CancellationToken.None);
        await restartedTracker.RestoreAfterRestartAsync(
            "session-two",
            "SYM",
            CancellationToken.None);

        var restored = Assert.Single(restartedTracker.Read("SYM", "session-one"));
        Assert.Equal(MarketReactionStatus.ContinuityUnknown, restored.Assessment.Status);
        Assert.Equal(started.Episode.Id, restored.Episode.Id);

        var noInteraction = Situation("session-one", Start.AddSeconds(3), null, null);
        var afterRestart = await restartedTracker.ObserveAsync(
            noInteraction,
            [Trade("session-one", 2, 99.7m)],
            _configuration,
            CaptureContext,
            CancellationToken.None);

        var terminal = Assert.Single(afterRestart);
        Assert.Equal(MarketReactionStatus.ContinuityUnknown, terminal.Assessment.Status);
        Assert.Null(terminal.Assessment.Reaction);
        Assert.Empty(restartedTracker.Read("SYM", "session-two"));
    }

    [Fact]
    public void Health_publication_ticks_do_not_advance_the_latest_reaction_assessment()
    {
        var interaction = SupportInteraction(100m, 99.9m, 100.1m);
        var episode = _evaluator.StartEpisode(
            interaction,
            Trade("session-one", 0, 100.05m),
            _configuration);
        var evaluation = _evaluator.Evaluate(episode, [], Start);
        var reaction = new MarketReactionReadModel(
            episode,
            evaluation.Assessment,
            CaptureContext(evaluation.Assessment));
        var opportunity = new Opportunity(
            Guid.NewGuid(),
            "SYM",
            "session-one",
            Start,
            Start.AddMinutes(5),
            "test",
            SetupBias.Neutral,
            OpportunityClassification.Watch,
            50,
            [],
            [],
            [],
            "test",
            100m,
            "features-v16",
            "setup-v1",
            _configuration.Version,
            new MarketDataCapabilities("UnitTest", "Recorded", MarketDataCoverage.Synthetic, false, true, true));
        var state = new ScannerState();
        state.SetResults(
            new ScannerStatus(MarketDataHealthStatus.Recorded, "UnitTest", 1, Start, null),
            [new ScannerAnalysisResult(
                null!,
                opportunity,
                new PublicationDecision(
                    OpportunityLifecycleState.Active,
                    "PublicationAllowed",
                    "healthy",
                    Start),
                null!,
                MarketReactions: [reaction])]);

        state.SetStatus(
            new ScannerStatus(MarketDataHealthStatus.Offline, "UnitTest", 1, Start, "offline"),
            Start.AddMinutes(1));

        var unchanged = Assert.Single(Assert.Single(state.Read().Results).MarketReactions!);
        Assert.Equal(reaction.Assessment, unchanged.Assessment);
        Assert.Equal(MarketReactionStatus.Unresolved, unchanged.Assessment.Status);
    }

    private static MarketReactionResearchContext CaptureContext(MarketReactionAssessment assessment) =>
        new(
            assessment.AsOfUtc,
            Guid.NewGuid(),
            "features-v16",
            100.05m,
            100m,
            0.1m,
            0.05m,
            10,
            1_000m,
            ShortHorizonDollarVolumeStatus.Available,
            2m,
            TrendDirectionContext.Bullish,
            PriceStructure.Mixed,
            DataQualityStatus.Good,
            true,
            true,
            OpportunityClassification.AvoidNoTrade,
            15,
            MarketSituationStatus.Available,
            MarketSituationContextMode.CurrentSessionOnly,
            "UnitTest",
            "Recorded");

    private static MarketSituationAssessment Situation(
        string sessionId,
        DateTimeOffset asOfUtc,
        MarketSituationInteraction? support,
        MarketSituationInteraction? resistance) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "SYM",
            sessionId,
            asOfUtc,
            asOfUtc,
            100.05m,
            MarketSituationStatus.Available,
            null,
            MarketSituationContextMode.CurrentSessionOnly,
            support?.Level,
            resistance?.Level,
            support,
            resistance,
            null,
            [.. new[] { support?.Level, resistance?.Level }.OfType<ImportantPriceLevel>()],
            0.1m,
            0.25m,
            "market-situation-v1",
            "features-v16",
            "scanner-config-v19",
            new MarketDataCapabilities("UnitTest", "Recorded", MarketDataCoverage.Synthetic, false, true, true),
            DataQualityStatus.Good,
            MarketStructureStatus.Available,
            true);

    private static MarketSituationInteraction SupportInteraction(
        decimal price,
        decimal zoneLow,
        decimal zoneHigh) =>
        Interaction(ImportantPriceLevelRole.Support, MarketSituationType.TestingSupport, price, zoneLow, zoneHigh);

    private static MarketSituationInteraction ResistanceInteraction(
        decimal price,
        decimal zoneLow,
        decimal zoneHigh) =>
        Interaction(ImportantPriceLevelRole.Resistance, MarketSituationType.TestingResistance, price, zoneLow, zoneHigh);

    private static MarketSituationInteraction Interaction(
        ImportantPriceLevelRole role,
        MarketSituationType situation,
        decimal price,
        decimal zoneLow,
        decimal zoneHigh)
    {
        var level = new ImportantPriceLevel(
            Guid.NewGuid(),
            "SYM",
            "session-one",
            "session-one",
            price,
            role,
            role == ImportantPriceLevelRole.Support
                ? ImportantPriceLevelSource.CurrentSwingLow
                : ImportantPriceLevelSource.CurrentSwingHigh,
            Guid.NewGuid(),
            null,
            null,
            null,
            null,
            null,
            MarketSituationContextMode.CurrentSessionOnly,
            "Complete current-session retained event history",
            null,
            null,
            null,
            "market-structure-v1",
            "features-v16",
            null);
        return new MarketSituationInteraction(
            situation,
            level,
            zoneLow,
            zoneHigh,
            0m,
            0m,
            0m,
            0m,
            0m);
    }

    private static TradeEvent Trade(string sessionId, int second, decimal price) =>
        new("SYM", sessionId, Start.AddSeconds(second), Start.AddSeconds(second), "UnitTest", $"trade-{second}", price, 10);

    private sealed class InMemoryReactionStore : IMarketReactionStore
    {
        private readonly Dictionary<Guid, LevelInteractionEpisode> _episodes = [];

        public List<MarketReactionEvaluation> Persisted { get; } = [];

        public Task<IReadOnlyList<LevelInteractionEpisode>> RestoreActiveEpisodesAsync(
            string sessionId,
            IReadOnlyCollection<string>? symbols,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LevelInteractionEpisode>>(_episodes.Values
                .Where(item => item.Status == MarketReactionEpisodeStatus.Active
                    && item.SessionId == sessionId
                    && (symbols is null || symbols.Contains(item.Symbol, StringComparer.OrdinalIgnoreCase)))
                .ToArray());

        public Task PersistAsync(
            MarketReactionEvaluation evaluation,
            CancellationToken cancellationToken)
        {
            _episodes[evaluation.Episode.Id] = evaluation.Episode;
            Persisted.Add(evaluation);
            return Task.CompletedTask;
        }
    }
}
