using MarketOpportunity.Analytics;
using MarketOpportunity.Domain;

namespace MarketOpportunity.Application;

public sealed record MarketReactionResearchContext(
    DateTimeOffset AsOfUtc,
    Guid FeatureSnapshotId,
    string FeatureSetVersion,
    decimal? ReferencePrice,
    decimal? SessionVwap,
    decimal? ShortHorizonReturnPercent,
    decimal? SecondaryHorizonReturnPercent,
    long? ShortHorizonVolume,
    decimal? ShortHorizonDollarVolumeUsd,
    ShortHorizonDollarVolumeStatus ShortHorizonDollarVolumeStatus,
    decimal? SpreadBasisPoints,
    TrendDirectionContext SessionTrendDirection,
    PriceStructure? PriceStructure,
    DataQualityStatus DataQuality,
    bool HasRequiredData,
    bool HasCompleteSessionData,
    OpportunityClassification OpportunityClassification,
    int OpportunityQualityScore,
    MarketSituationStatus MarketSituationStatus,
    MarketSituationContextMode MarketSituationContextMode,
    string MarketDataProvider,
    string MarketDataFeed)
{
    public const string CurrentDefinitionVersion = "market-reaction-research-context-v1";

    public string DefinitionVersion { get; init; } = CurrentDefinitionVersion;
}

public sealed record MarketReactionReadModel(
    LevelInteractionEpisode Episode,
    MarketReactionAssessment Assessment,
    MarketReactionResearchContext? ResearchContext);

public sealed class MarketReactionTracker(
    MarketReactionEvaluator evaluator,
    IMarketReactionStore? store = null)
{
    private readonly Dictionary<(string Symbol, string SessionId), List<LevelInteractionEpisode>> _episodes = [];
    private readonly Dictionary<(string Symbol, string SessionId), HashSet<Guid>> _activeInteractions = [];
    private readonly Dictionary<(string Symbol, string SessionId), Dictionary<Guid, MarketReactionReadModel>> _latest = [];
    private readonly HashSet<(string Symbol, string SessionId)> _restoredScopes = [];

    public async Task RestoreAfterRestartAsync(
        string sessionId,
        string symbol,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        var scope = (symbol, sessionId);
        if (!_restoredScopes.Add(scope) || store is null)
        {
            return;
        }

        var continuity = new MarketReactionContinuityService(store, evaluator);
        var terminal = await continuity.RestoreAfterRestartAsync(
            sessionId,
            [symbol],
            cancellationToken);
        foreach (var evaluation in terminal)
        {
            AddEvaluation(scope, evaluation, null);
        }
    }

    public async Task<IReadOnlyList<MarketReactionReadModel>> ObserveAsync(
        MarketSituationAssessment situation,
        IReadOnlyList<MarketEvent> observations,
        ScannerConfiguration configuration,
        Func<MarketReactionAssessment, MarketReactionResearchContext> captureResearchContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(situation);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(captureResearchContext);

        var scope = (situation.Symbol, situation.SessionId);
        var episodes = GetEpisodes(scope);
        var interactions = GetActiveInteractions(scope);
        var currentInteractions = new[] { situation.SupportInteraction, situation.ResistanceInteraction }
            .Where(IsEpisodeStartSituation)
            .Select(interaction => interaction!)
            .ToArray();
        var currentLevelIds = currentInteractions
            .Select(interaction => interaction.Level.Id)
            .ToHashSet();
        interactions.RemoveWhere(levelId => !currentLevelIds.Contains(levelId));

        foreach (var interaction in currentInteractions)
        {
            if (interactions.Contains(interaction.Level.Id))
            {
                continue;
            }

            var startObservation = observations.OfType<TradeEvent>()
                .Where(trade => MatchesSituation(interaction, trade))
                .OrderBy(trade => trade.EventTimeUtc)
                .ThenBy(trade => trade.Provider, StringComparer.Ordinal)
                .ThenBy(trade => trade.SequenceId, StringComparer.Ordinal)
                .ThenBy(MarketEventIdentity.Create, StringComparer.Ordinal)
                .LastOrDefault();
            if (startObservation is null)
            {
                continue;
            }

            var episode = evaluator.StartEpisode(interaction, startObservation, configuration);
            if (episodes.All(item => item.Id != episode.Id))
            {
                episodes.Add(episode);
                var initial = evaluator.Evaluate(episode, [], episode.StartedAtUtc);
                await PersistAndCaptureAsync(scope, initial, captureResearchContext, cancellationToken);
            }

            interactions.Add(interaction.Level.Id);
        }

        foreach (var episode in episodes
                     .Where(item => item.Status == MarketReactionEpisodeStatus.Active)
                     .ToArray())
        {
            var evaluation = evaluator.Evaluate(episode, observations, situation.AsOfUtc);
            await PersistAndCaptureAsync(scope, evaluation, captureResearchContext, cancellationToken);
            var index = episodes.FindIndex(item => item.Id == evaluation.Episode.Id);
            episodes[index] = evaluation.Episode;
        }

        PruneTerminalHistory(scope, configuration.MaximumStateEvents);
        return Read(scope);
    }

    public IReadOnlyList<MarketReactionReadModel> Read(string symbol, string sessionId) =>
        Read((symbol, sessionId));

    private IReadOnlyList<MarketReactionReadModel> Read((string Symbol, string SessionId) scope) =>
        _latest.TryGetValue(scope, out var latest)
            ? latest.Values
                .OrderByDescending(item => item.Assessment.AsOfUtc)
                .ThenBy(item => item.Episode.Id)
                .ToArray()
            : [];

    private async Task PersistAndCaptureAsync(
        (string Symbol, string SessionId) scope,
        MarketReactionEvaluation evaluation,
        Func<MarketReactionAssessment, MarketReactionResearchContext> captureResearchContext,
        CancellationToken cancellationToken)
    {
        var existing = Read(scope).FirstOrDefault(item => item.Episode.Id == evaluation.Episode.Id);
        if (existing?.Assessment.Id == evaluation.Assessment.Id)
        {
            if (existing.Assessment != evaluation.Assessment || existing.Episode != evaluation.Episode)
            {
                throw new InvalidOperationException(
                    $"Market reaction assessment '{evaluation.Assessment.Id}' changed after it was captured.");
            }

            return;
        }

        var researchContext = captureResearchContext(evaluation.Assessment);
        if (store is not null)
        {
            await store.PersistAsync(evaluation, researchContext, cancellationToken);
        }

        AddEvaluation(scope, evaluation, researchContext);
    }

    private void AddEvaluation(
        (string Symbol, string SessionId) scope,
        MarketReactionEvaluation evaluation,
        MarketReactionResearchContext? context)
    {
        var episodes = GetEpisodes(scope);
        var episodeIndex = episodes.FindIndex(item => item.Id == evaluation.Episode.Id);
        if (episodeIndex < 0)
        {
            episodes.Add(evaluation.Episode);
        }
        else
        {
            episodes[episodeIndex] = evaluation.Episode;
        }

        if (!_latest.TryGetValue(scope, out var latest))
        {
            latest = [];
            _latest.Add(scope, latest);
        }

        latest[evaluation.Episode.Id] = new MarketReactionReadModel(
            evaluation.Episode,
            evaluation.Assessment,
            context);
    }

    private void PruneTerminalHistory(
        (string Symbol, string SessionId) scope,
        int maximumTerminalEpisodes)
    {
        var terminal = GetEpisodes(scope)
            .Where(item => item.Status != MarketReactionEpisodeStatus.Active)
            .OrderByDescending(item => item.ReactionAtUtc ?? item.LastObservationAtUtc ?? item.StartedAtUtc)
            .ThenByDescending(item => item.Id)
            .Skip(maximumTerminalEpisodes)
            .Select(item => item.Id)
            .ToHashSet();
        if (terminal.Count == 0)
        {
            return;
        }

        GetEpisodes(scope).RemoveAll(item => terminal.Contains(item.Id));
        if (_latest.TryGetValue(scope, out var latest))
        {
            foreach (var episodeId in terminal)
            {
                latest.Remove(episodeId);
            }
        }
    }

    private List<LevelInteractionEpisode> GetEpisodes((string Symbol, string SessionId) scope)
    {
        if (!_episodes.TryGetValue(scope, out var episodes))
        {
            episodes = [];
            _episodes.Add(scope, episodes);
        }

        return episodes;
    }

    private HashSet<Guid> GetActiveInteractions((string Symbol, string SessionId) scope)
    {
        if (!_activeInteractions.TryGetValue(scope, out var interactions))
        {
            interactions = [];
            _activeInteractions.Add(scope, interactions);
        }

        return interactions;
    }

    private static bool IsEpisodeStartSituation(MarketSituationInteraction? interaction) =>
        interaction?.SituationType is MarketSituationType.TestingSupport
            or MarketSituationType.CrossedSupport
            or MarketSituationType.TestingResistance
            or MarketSituationType.CrossedResistance;

    private static bool MatchesSituation(MarketSituationInteraction interaction, TradeEvent trade) =>
        trade.Price > 0
        && trade.Symbol.Equals(interaction.Level.Symbol, StringComparison.OrdinalIgnoreCase)
        && trade.SessionId.Equals(interaction.Level.CurrentSessionId, StringComparison.Ordinal)
        && (interaction.SituationType switch
        {
            MarketSituationType.TestingSupport or MarketSituationType.TestingResistance =>
                interaction.ZoneLow <= trade.Price && trade.Price <= interaction.ZoneHigh,
            MarketSituationType.CrossedSupport => trade.Price < interaction.ZoneLow,
            MarketSituationType.CrossedResistance => trade.Price > interaction.ZoneHigh,
            _ => false
        });
}
