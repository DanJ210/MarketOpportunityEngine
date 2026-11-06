using MarketOpportunity.Analytics;
using MarketOpportunity.Domain;

namespace MarketOpportunity.Application;

public interface IMarketReactionStore
{
    Task<IReadOnlyList<LevelInteractionEpisode>> RestoreActiveEpisodesAsync(
        string sessionId,
        IReadOnlyCollection<string>? symbols,
        CancellationToken cancellationToken);

    Task PersistAsync(
        MarketReactionEvaluation evaluation,
        CancellationToken cancellationToken);
}

public sealed class MarketReactionContinuityService(
    IMarketReactionStore store,
    MarketReactionEvaluator evaluator)
{
    public async Task<IReadOnlyList<MarketReactionEvaluation>> RestoreAfterRestartAsync(
        string currentSessionId,
        IReadOnlyCollection<string>? symbols,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentSessionId);
        var unresolved = await store.RestoreActiveEpisodesAsync(
            currentSessionId,
            symbols,
            cancellationToken);
        var terminal = new List<MarketReactionEvaluation>(unresolved.Count);
        foreach (var episode in unresolved)
        {
            var evaluation = evaluator.MarkContinuityUnknown(episode);
            await store.PersistAsync(evaluation, cancellationToken);
            terminal.Add(evaluation);
        }

        return terminal;
    }
}
