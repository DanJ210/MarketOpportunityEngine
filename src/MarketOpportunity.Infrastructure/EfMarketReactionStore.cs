using System.Text.Json;
using MarketOpportunity.Analytics;
using MarketOpportunity.Application;
using MarketOpportunity.Domain;
using Microsoft.EntityFrameworkCore;

namespace MarketOpportunity.Infrastructure;

/// <summary>
/// Persists the mutable episode lifecycle and immutable reaction assessments. An episode and its
/// assessment are saved together so a terminal episode cannot reference missing history.
/// </summary>
public sealed class EfMarketReactionStore(MarketOpportunityDbContext dbContext) : IMarketReactionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<LevelInteractionEpisode>> RestoreActiveEpisodesAsync(
        string sessionId,
        IReadOnlyCollection<string>? symbols,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var activeStatus = nameof(MarketReactionEpisodeStatus.Active);
        var query = dbContext.LevelInteractionEpisodes
            .AsNoTracking()
            .Where(item => item.Status == activeStatus
                && EF.Functions.Collate(item.SessionId, "Latin1_General_100_BIN2") == sessionId);
        if (symbols is { Count: > 0 })
        {
            var normalizedSymbols = symbols
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                .Select(symbol => symbol.ToUpperInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            query = query.Where(item => normalizedSymbols.Contains(item.Symbol.ToUpper()));
        }
        else if (symbols is { Count: 0 })
        {
            return [];
        }

        var episodes = await query
            .OrderBy(item => item.StartedAtUtc)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);
        return episodes.Select(item => DeserializeEpisode(item.EpisodeJson, item.Id)).ToArray();
    }

    public async Task PersistAsync(
        MarketReactionEvaluation evaluation,
        CancellationToken cancellationToken) =>
        await PersistAsync(evaluation, null, cancellationToken);

    public async Task PersistAsync(
        MarketReactionEvaluation evaluation,
        MarketReactionResearchContext? researchContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        ValidateEvaluation(evaluation);
        if (researchContext is not null
            && !researchContext.AsOfUtc.EqualsExact(evaluation.Assessment.AsOfUtc))
        {
            throw new ArgumentException(
                "Reaction research context must use the assessment's exact event time.",
                nameof(researchContext));
        }

        var episode = evaluation.Episode;
        var assessment = evaluation.Assessment;
        var episodeJson = JsonSerializer.Serialize(episode, JsonOptions);
        var assessmentJson = JsonSerializer.Serialize(assessment, JsonOptions);
        var contextJson = researchContext is null
            ? null
            : JsonSerializer.Serialize(researchContext, JsonOptions);
        var episodeEntity = await dbContext.LevelInteractionEpisodes
            .SingleOrDefaultAsync(item => item.Id == episode.Id, cancellationToken);
        var assessmentEntity = await dbContext.MarketReactionAssessments
            .SingleOrDefaultAsync(item => item.Id == assessment.Id, cancellationToken);
        var contextEntity = researchContext is null
            ? null
            : await dbContext.MarketReactionResearchContexts
                .SingleOrDefaultAsync(item => item.AssessmentId == assessment.Id, cancellationToken);

        LevelInteractionEpisode? previousEpisode = null;
        if (episodeEntity is not null)
        {
            previousEpisode = DeserializeEpisode(episodeEntity.EpisodeJson, episodeEntity.Id);
            ValidateEpisodeUpdate(previousEpisode, episode, episodeEntity.EpisodeJson, episodeJson);
        }

        if (assessmentEntity is not null
            && !string.Equals(assessmentEntity.AssessmentJson, assessmentJson, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Market reaction assessment '{assessment.Id}' is immutable and already has different values.");
        }

        if (contextEntity is not null
            && !string.Equals(contextEntity.ContextJson, contextJson, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Market reaction research context for assessment '{assessment.Id}' is immutable and already has different values.");
        }

        if (assessment.Reaction is not null
            && await dbContext.MarketReactionAssessments.AnyAsync(
                item => item.EpisodeId == assessment.EpisodeId
                    && item.Id != assessment.Id
                    && item.ReactionType != null,
                cancellationToken))
        {
            throw new InvalidOperationException(
                $"Reaction episode '{assessment.EpisodeId}' already has its immutable confirmed assessment.");
        }

        if (episodeEntity is null)
        {
            dbContext.LevelInteractionEpisodes.Add(ToEntity(episode, episodeJson));
        }
        else if (!string.Equals(episodeEntity.EpisodeJson, episodeJson, StringComparison.Ordinal))
        {
            episodeEntity.Status = episode.Status.ToString();
            episodeEntity.LatestAsOfUtc = episode.LastObservationAtUtc!.Value;
            episodeEntity.EpisodeJson = episodeJson;
        }

        if (assessmentEntity is null)
        {
            dbContext.MarketReactionAssessments.Add(ToEntity(assessment, assessmentJson));
        }
        if (researchContext is not null && contextEntity is null)
        {
            dbContext.MarketReactionResearchContexts.Add(new MarketReactionResearchContextEntity
            {
                AssessmentId = assessment.Id,
                EpisodeId = assessment.EpisodeId,
                Symbol = assessment.Symbol,
                SessionId = assessment.SessionId,
                AsOfUtc = assessment.AsOfUtc,
                ContextJson = contextJson!
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateEvaluation(MarketReactionEvaluation evaluation)
    {
        var episode = evaluation.Episode
            ?? throw new ArgumentException("A reaction evaluation requires an episode.", nameof(evaluation));
        var assessment = evaluation.Assessment
            ?? throw new ArgumentException("A reaction evaluation requires an assessment.", nameof(evaluation));

        if (episode.Id != assessment.EpisodeId
            || episode.LevelId != assessment.LevelId
            || !string.Equals(episode.Symbol, assessment.Symbol, StringComparison.Ordinal)
            || !string.Equals(episode.SessionId, assessment.SessionId, StringComparison.Ordinal)
            || !episode.StartedAtUtc.EqualsExact(assessment.StartedAtUtc)
            || !episode.ExpiresAtUtc.EqualsExact(assessment.ExpiresAtUtc)
            || episode.Reaction != assessment.Reaction
            || !NullableEqualsExact(episode.ReactionAtUtc, assessment.ReactionAtUtc)
            || !string.Equals(episode.ReactionEventIdentity, assessment.ReactionEventIdentity, StringComparison.Ordinal)
            || episode.ReactionPrice != assessment.ReactionPrice
            || !NullableEqualsExact(episode.LastObservationAtUtc, assessment.LastObservationAtUtc)
            || !string.Equals(episode.LastObservationIdentity, assessment.LastObservationIdentity, StringComparison.Ordinal)
            || episode.LastObservationPrice != assessment.LastObservationPrice
            || episode.HasCrossedOppositeZone != assessment.HasCrossedOppositeZone
            || episode.ConfirmationDistancePercent != assessment.ConfirmationDistancePercent
            || episode.MaxEpisodeDurationSeconds != assessment.MaxEpisodeDurationSeconds
            || !string.Equals(episode.DefinitionVersion, assessment.DefinitionVersion, StringComparison.Ordinal)
            || !string.Equals(episode.ConfigurationVersion, assessment.ConfigurationVersion, StringComparison.Ordinal)
            || (episode.Status switch
            {
                MarketReactionEpisodeStatus.Active =>
                    assessment.Status != MarketReactionStatus.Unresolved
                        || !HasNoReactionState(episode)
                        || episode.LastObservationAtUtc is null
                        || episode.LastObservationAtUtc > assessment.AsOfUtc
                        || episode.LastObservationAtUtc >= episode.ExpiresAtUtc
                        || assessment.AsOfUtc < episode.StartedAtUtc
                        || assessment.AsOfUtc >= episode.ExpiresAtUtc,
                MarketReactionEpisodeStatus.Confirmed =>
                    assessment.Status != MarketReactionStatus.Confirmed
                        || episode.Reaction is null
                        || episode.ReactionAtUtc is null
                        || episode.ReactionEventIdentity is null
                        || episode.ReactionPrice is null
                        || !ReactionMatchesRole(episode.Role, episode.Reaction.Value)
                        || episode.ReactionAtUtc.Value <= episode.StartedAtUtc
                        || episode.ReactionAtUtc.Value > episode.ExpiresAtUtc
                        || !episode.ReactionAtUtc.Value.EqualsExact(assessment.AsOfUtc)
                        || !NullableEqualsExact(episode.ReactionAtUtc, episode.LastObservationAtUtc)
                        || !string.Equals(episode.ReactionEventIdentity, episode.LastObservationIdentity, StringComparison.Ordinal)
                        || episode.ReactionPrice != episode.LastObservationPrice,
                MarketReactionEpisodeStatus.Expired =>
                    assessment.Status != MarketReactionStatus.UnresolvedExpired
                        || !HasNoReactionState(episode)
                        || episode.LastObservationAtUtc > episode.ExpiresAtUtc
                        || !episode.ExpiresAtUtc.EqualsExact(assessment.AsOfUtc),
                MarketReactionEpisodeStatus.ContinuityUnknown =>
                    assessment.Status != MarketReactionStatus.ContinuityUnknown
                        || !HasNoReactionState(episode)
                        || episode.LastObservationAtUtc is null
                        || episode.LastObservationAtUtc > episode.ExpiresAtUtc
                        || !episode.LastObservationAtUtc.Value.EqualsExact(assessment.AsOfUtc),
                _ => true
            }))
        {
            throw new InvalidOperationException(
                "The reaction assessment must describe the supplied episode lifecycle state.");
        }
    }

    private static void ValidateEpisodeUpdate(
        LevelInteractionEpisode previous,
        LevelInteractionEpisode current,
        string previousJson,
        string currentJson)
    {
        if (!SameImmutableEpisodeContent(previous, current))
        {
            throw new InvalidOperationException(
                $"Reaction episode '{current.Id}' identity and pinned geometry are immutable.");
        }

        var previousTerminal = IsTerminal(previous.Status);
        if (previousTerminal)
        {
            if (!string.Equals(previousJson, currentJson, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Reaction episode '{current.Id}' is terminal ({previous.Status}); its lifecycle is immutable.");
            }

            return;
        }

        if (previous.Status != MarketReactionEpisodeStatus.Active
            || (current.Status != MarketReactionEpisodeStatus.Active && !IsTerminal(current.Status)))
        {
            throw new InvalidOperationException(
                $"Reaction episode '{current.Id}' has an illegal lifecycle transition from {previous.Status} to {current.Status}.");
        }

        if (previous.HasCrossedOppositeZone && !current.HasCrossedOppositeZone)
        {
            throw new InvalidOperationException(
                $"Reaction episode '{current.Id}' cannot revert its observed opposite-zone crossing.");
        }

        if (previous.LastObservationAtUtc is { } previousAt
            && current.LastObservationAtUtc is { } currentAt)
        {
            var ordering = CompareCanonical(
                currentAt,
                current.LastObservationProvider!,
                current.LastObservationSequenceId,
                current.LastObservationIdentity!,
                previousAt,
                previous.LastObservationProvider!,
                previous.LastObservationSequenceId,
                previous.LastObservationIdentity!);
            if (ordering < 0)
            {
                throw new InvalidOperationException(
                    $"Reaction episode '{current.Id}' cannot move its latest observation backward.");
            }

            if (ordering == 0
                && (!previousAt.EqualsExact(currentAt)
                    || !string.Equals(previous.LastObservationIdentity, current.LastObservationIdentity, StringComparison.Ordinal)
                    || !string.Equals(previous.LastObservationProvider, current.LastObservationProvider, StringComparison.Ordinal)
                    || !string.Equals(previous.LastObservationSequenceId, current.LastObservationSequenceId, StringComparison.Ordinal)
                    || previous.LastObservationPrice != current.LastObservationPrice))
            {
                throw new InvalidOperationException(
                    $"Reaction episode '{current.Id}' cannot rewrite an already observed canonical event.");
            }
        }
    }

    private static bool SameImmutableEpisodeContent(
        LevelInteractionEpisode left,
        LevelInteractionEpisode right) =>
        left.Id == right.Id
        && left.LevelId == right.LevelId
        && string.Equals(left.Symbol, right.Symbol, StringComparison.Ordinal)
        && string.Equals(left.SessionId, right.SessionId, StringComparison.Ordinal)
        && left.Role == right.Role
        && left.InitiatingSituation == right.InitiatingSituation
        && left.LevelPrice == right.LevelPrice
        && left.ZoneLow == right.ZoneLow
        && left.ZoneHigh == right.ZoneHigh
        && left.StartedAtUtc.EqualsExact(right.StartedAtUtc)
        && string.Equals(left.StartObservationIdentity, right.StartObservationIdentity, StringComparison.Ordinal)
        && string.Equals(left.StartObservationProvider, right.StartObservationProvider, StringComparison.Ordinal)
        && string.Equals(left.StartObservationSequenceId, right.StartObservationSequenceId, StringComparison.Ordinal)
        && left.ExpiresAtUtc.EqualsExact(right.ExpiresAtUtc)
        && left.ConfirmationDistancePercent == right.ConfirmationDistancePercent
        && left.MaxEpisodeDurationSeconds == right.MaxEpisodeDurationSeconds
        && string.Equals(left.DefinitionVersion, right.DefinitionVersion, StringComparison.Ordinal)
        && string.Equals(left.ConfigurationVersion, right.ConfigurationVersion, StringComparison.Ordinal);

    private static bool IsTerminal(MarketReactionEpisodeStatus status) =>
        status is MarketReactionEpisodeStatus.Confirmed
            or MarketReactionEpisodeStatus.Expired
            or MarketReactionEpisodeStatus.ContinuityUnknown;

    private static bool HasNoReactionState(LevelInteractionEpisode episode) =>
        episode.Reaction is null
        && episode.ReactionAtUtc is null
        && episode.ReactionEventIdentity is null
        && episode.ReactionPrice is null;

    private static bool ReactionMatchesRole(ImportantPriceLevelRole role, MarketReactionType reaction) =>
        role == ImportantPriceLevelRole.Support
            ? reaction is MarketReactionType.SupportHeld
                or MarketReactionType.SupportBreakdown
                or MarketReactionType.SupportReclaimed
            : role == ImportantPriceLevelRole.Resistance
                && reaction is MarketReactionType.ResistanceRejected
                    or MarketReactionType.ResistanceBreakout
                    or MarketReactionType.ResistanceFailedBreakout;

    private static int CompareCanonical(
        DateTimeOffset leftTime,
        string leftProvider,
        string? leftSequence,
        string leftIdentity,
        DateTimeOffset rightTime,
        string rightProvider,
        string? rightSequence,
        string rightIdentity)
    {
        var result = leftTime.CompareTo(rightTime);
        if (result != 0)
        {
            return result;
        }

        result = string.Compare(leftProvider, rightProvider, StringComparison.Ordinal);
        if (result != 0)
        {
            return result;
        }

        result = string.Compare(leftSequence, rightSequence, StringComparison.Ordinal);
        return result != 0
            ? result
            : string.Compare(leftIdentity, rightIdentity, StringComparison.Ordinal);
    }

    private static bool NullableEqualsExact(DateTimeOffset? left, DateTimeOffset? right) =>
        left.HasValue == right.HasValue
        && (!left.HasValue || left.Value.EqualsExact(right!.Value));

    private static LevelInteractionEpisode DeserializeEpisode(string json, Guid id)
    {
        return JsonSerializer.Deserialize<LevelInteractionEpisode>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Persisted reaction episode '{id}' has invalid JSON.");
    }

    private static LevelInteractionEpisodeEntity ToEntity(
        LevelInteractionEpisode episode,
        string json) => new()
    {
        Id = episode.Id,
        LevelId = episode.LevelId,
        Symbol = episode.Symbol,
        SessionId = episode.SessionId,
        StartedAtUtc = episode.StartedAtUtc,
        LatestAsOfUtc = episode.LastObservationAtUtc!.Value,
        Status = episode.Status.ToString(),
        EpisodeJson = json
    };

    private static MarketReactionAssessmentEntity ToEntity(
        MarketReactionAssessment assessment,
        string json) => new()
    {
        Id = assessment.Id,
        EpisodeId = assessment.EpisodeId,
        LevelId = assessment.LevelId,
        Symbol = assessment.Symbol,
        SessionId = assessment.SessionId,
        AsOfUtc = assessment.AsOfUtc,
        ReactionType = assessment.Reaction?.ToString(),
        AssessmentJson = json
    };
}
