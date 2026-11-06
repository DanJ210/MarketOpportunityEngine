using System.Globalization;
using MarketOpportunity.Domain;

namespace MarketOpportunity.Analytics;

public sealed class MarketReactionEvaluator
{
    public const string DefinitionVersion = "market-reaction-v1";

    public LevelInteractionEpisode StartEpisode(
        MarketSituationInteraction interaction,
        TradeEvent initiatingObservation,
        ScannerConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(initiatingObservation);
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();

        var level = interaction.Level
            ?? throw new ArgumentException("An eligible structural level is required.", nameof(interaction));
        if (level.Price <= 0
            || interaction.ZoneLow <= 0
            || interaction.ZoneLow >= level.Price
            || interaction.ZoneHigh <= level.Price)
        {
            throw new ArgumentException("The interaction must contain a positive, valid pinned test zone.", nameof(interaction));
        }

        var expectedRole = interaction.SituationType switch
        {
            MarketSituationType.TestingSupport or MarketSituationType.CrossedSupport =>
                ImportantPriceLevelRole.Support,
            MarketSituationType.TestingResistance or MarketSituationType.CrossedResistance =>
                ImportantPriceLevelRole.Resistance,
            _ => throw new ArgumentException(
                "Only testing or crossed support/resistance interactions can start a reaction episode.",
                nameof(interaction))
        };
        if (level.Role != expectedRole)
        {
            throw new ArgumentException("The interaction type does not match the structural level role.", nameof(interaction));
        }

        if (!initiatingObservation.Symbol.Equals(level.Symbol, StringComparison.OrdinalIgnoreCase)
            || !initiatingObservation.SessionId.Equals(level.CurrentSessionId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The initiating trade must match the interaction symbol and current session.",
                nameof(initiatingObservation));
        }

        if (initiatingObservation.Price <= 0 || !MatchesSituation(interaction, initiatingObservation.Price))
        {
            throw new ArgumentException(
                "The initiating trade price must be positive and agree with the interaction and pinned zone.",
                nameof(initiatingObservation));
        }

        var policy = configuration.MarketReaction;
        var startedAtUtc = initiatingObservation.EventTimeUtc.ToUniversalTime();
        var startIdentity = MarketEventIdentity.Create(initiatingObservation);
        var expiresAtUtc = startedAtUtc.AddSeconds(policy.MaxEpisodeDurationSeconds);
        var hasCrossedOppositeZone = expectedRole == ImportantPriceLevelRole.Support
            ? initiatingObservation.Price < interaction.ZoneLow
            : initiatingObservation.Price > interaction.ZoneHigh;
        var identity = string.Join(
            "\u001f",
            DefinitionVersion,
            level.Id.ToString("N"),
            level.Symbol.ToUpperInvariant(),
            level.CurrentSessionId,
            expectedRole.ToString(),
            interaction.SituationType.ToString(),
            Format(level.Price),
            Format(interaction.ZoneLow),
            Format(interaction.ZoneHigh),
            startedAtUtc.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture),
            startIdentity,
            Format(policy.ConfirmationDistancePercent),
            policy.MaxEpisodeDurationSeconds.ToString(CultureInfo.InvariantCulture),
            configuration.Version);

        return new LevelInteractionEpisode(
            DeterministicId.From(identity),
            level.Id,
            level.Symbol,
            level.CurrentSessionId,
            expectedRole,
            interaction.SituationType,
            level.Price,
            interaction.ZoneLow,
            interaction.ZoneHigh,
            startedAtUtc,
            startIdentity,
            initiatingObservation.Provider,
            initiatingObservation.SequenceId,
            expiresAtUtc,
            policy.ConfirmationDistancePercent,
            policy.MaxEpisodeDurationSeconds,
            DefinitionVersion,
            configuration.Version,
            hasCrossedOppositeZone,
            MarketReactionEpisodeStatus.Active,
            null,
            null,
            null,
            null,
            startedAtUtc,
            startIdentity,
            initiatingObservation.Provider,
            initiatingObservation.SequenceId,
            initiatingObservation.Price);
    }

    public MarketReactionEvaluation Evaluate(
        LevelInteractionEpisode episode,
        IReadOnlyList<MarketEvent> observations,
        DateTimeOffset asOfUtc)
    {
        ArgumentNullException.ThrowIfNull(episode);
        ArgumentNullException.ThrowIfNull(observations);
        ValidateEpisode(episode);

        var asOf = asOfUtc.ToUniversalTime();
        if (asOf < episode.StartedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(asOfUtc), "Evaluation cannot precede the episode start.");
        }

        if ((episode.Status == MarketReactionEpisodeStatus.Confirmed
                && episode.ReactionAtUtc is { } reactionAtUtc
                && reactionAtUtc > asOf)
            || (episode.Status == MarketReactionEpisodeStatus.Expired
                && episode.ExpiresAtUtc > asOf))
        {
            throw new ArgumentOutOfRangeException(
                nameof(asOfUtc),
                "Evaluation cannot precede an already-recorded terminal episode event.");
        }

        if (episode.Status == MarketReactionEpisodeStatus.Active
            && episode.LastObservationAtUtc is { } lastObservationAtUtc
            && lastObservationAtUtc > asOf)
        {
            throw new ArgumentOutOfRangeException(
                nameof(asOfUtc),
                "Evaluation cannot rewind behind an observation already incorporated into the episode.");
        }

        var updated = episode;
        if (updated.Status == MarketReactionEpisodeStatus.Active)
        {
            foreach (var observation in CanonicalObservations(updated, observations, asOf))
            {
                if (observation.EventTimeUtc > updated.ExpiresAtUtc)
                {
                    break;
                }

                updated = ApplyObservation(updated, observation);
                if (updated.Status != MarketReactionEpisodeStatus.Active)
                {
                    break;
                }
            }

            if (updated.Status == MarketReactionEpisodeStatus.Active && asOf >= updated.ExpiresAtUtc)
            {
                updated = updated with
                {
                    Status = MarketReactionEpisodeStatus.Expired,
                    Reaction = null,
                    ReactionEventIdentity = null,
                    ReactionPrice = null
                };
            }
        }

        var assessmentAsOf = updated.Status switch
        {
            MarketReactionEpisodeStatus.Confirmed => updated.ReactionAtUtc!.Value,
            MarketReactionEpisodeStatus.Expired => updated.ExpiresAtUtc,
            _ => asOf
        };
        return new MarketReactionEvaluation(updated, CreateAssessment(updated, assessmentAsOf));
    }

    private static IReadOnlyList<TradeEvent> CanonicalObservations(
        LevelInteractionEpisode episode,
        IReadOnlyList<MarketEvent> observations,
        DateTimeOffset asOfUtc)
    {
        var ordered = observations.OfType<TradeEvent>()
            .Where(item => item.Price > 0
                && item.Symbol.Equals(episode.Symbol, StringComparison.OrdinalIgnoreCase)
                && item.SessionId.Equals(episode.SessionId, StringComparison.Ordinal)
                && item.EventTimeUtc <= asOfUtc)
            .OrderBy(item => item, TradeEventComparer.Instance)
            .GroupBy(MarketEventIdentity.Create, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                if (group.Skip(1).Any(item => item.EventTimeUtc != first.EventTimeUtc
                        || item.Provider != first.Provider
                        || item.Symbol != first.Symbol
                        || item.SessionId != first.SessionId
                        || item.SequenceId != first.SequenceId
                        || item.Price != first.Price
                        || item.Size != first.Size))
                {
                    throw new InvalidOperationException(
                        "Conflicting trade observations share the same canonical market-event identity.");
                }

                return first;
            })
            .ToArray();

        return ordered
            .Where(item => CompareCanonical(
                    item.EventTimeUtc,
                    item.Provider,
                    item.SequenceId,
                    MarketEventIdentity.Create(item),
                    episode.StartedAtUtc,
                    episode.StartObservationProvider,
                    episode.StartObservationSequenceId,
                    episode.StartObservationIdentity) > 0)
            .Where(item => episode.LastObservationAtUtc is null
                || CompareCanonical(
                    item.EventTimeUtc,
                    item.Provider,
                    item.SequenceId,
                    MarketEventIdentity.Create(item),
                    episode.LastObservationAtUtc.Value,
                    episode.LastObservationProvider!,
                    episode.LastObservationSequenceId,
                    episode.LastObservationIdentity!) > 0)
            .ToArray();
    }

    private static LevelInteractionEpisode ApplyObservation(
        LevelInteractionEpisode episode,
        TradeEvent observation)
    {
        var crossedOppositeZone = episode.HasCrossedOppositeZone
            || (episode.Role == ImportantPriceLevelRole.Support
                ? observation.Price < episode.ZoneLow
                : observation.Price > episode.ZoneHigh);
        var confirmationAbove = episode.ZoneHigh
            * (1m + episode.ConfirmationDistancePercent / 100m);
        var confirmationBelow = episode.ZoneLow
            * (1m - episode.ConfirmationDistancePercent / 100m);

        MarketReactionType? reaction = episode.Role switch
        {
            ImportantPriceLevelRole.Support when observation.Price <= confirmationBelow =>
                MarketReactionType.SupportBreakdown,
            ImportantPriceLevelRole.Support when observation.Price >= confirmationAbove =>
                crossedOppositeZone
                    ? MarketReactionType.SupportReclaimed
                    : MarketReactionType.SupportHeld,
            ImportantPriceLevelRole.Resistance when observation.Price >= confirmationAbove =>
                MarketReactionType.ResistanceBreakout,
            ImportantPriceLevelRole.Resistance when observation.Price <= confirmationBelow =>
                crossedOppositeZone
                    ? MarketReactionType.ResistanceFailedBreakout
                    : MarketReactionType.ResistanceRejected,
            _ => null
        };

        return episode with
        {
            HasCrossedOppositeZone = crossedOppositeZone,
            Status = reaction is null
                ? MarketReactionEpisodeStatus.Active
                : MarketReactionEpisodeStatus.Confirmed,
            Reaction = reaction,
            ReactionAtUtc = reaction is null ? null : observation.EventTimeUtc.ToUniversalTime(),
            ReactionEventIdentity = reaction is null ? null : MarketEventIdentity.Create(observation),
            ReactionPrice = reaction is null ? null : observation.Price,
            LastObservationAtUtc = observation.EventTimeUtc.ToUniversalTime(),
            LastObservationIdentity = MarketEventIdentity.Create(observation),
            LastObservationProvider = observation.Provider,
            LastObservationSequenceId = observation.SequenceId,
            LastObservationPrice = observation.Price
        };
    }

    private static MarketReactionAssessment CreateAssessment(
        LevelInteractionEpisode episode,
        DateTimeOffset asOfUtc)
    {
        var status = episode.Status switch
        {
            MarketReactionEpisodeStatus.Active => MarketReactionStatus.Unresolved,
            MarketReactionEpisodeStatus.Confirmed => MarketReactionStatus.Confirmed,
            MarketReactionEpisodeStatus.Expired => MarketReactionStatus.UnresolvedExpired,
            _ => throw new ArgumentOutOfRangeException(nameof(episode), episode.Status, "Unknown episode status.")
        };
        var id = DeterministicId.From(string.Join(
            "\u001f",
            episode.DefinitionVersion,
            episode.Id.ToString("N"),
            asOfUtc.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture),
            status.ToString(),
            episode.Reaction?.ToString() ?? "",
            episode.ReactionAtUtc?.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture) ?? "",
            episode.ReactionEventIdentity ?? "",
            Format(episode.ReactionPrice),
            episode.LastObservationAtUtc?.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture) ?? "",
            episode.LastObservationIdentity ?? "",
            Format(episode.LastObservationPrice),
            episode.HasCrossedOppositeZone.ToString(CultureInfo.InvariantCulture),
            Format(episode.ConfirmationDistancePercent),
            episode.MaxEpisodeDurationSeconds.ToString(CultureInfo.InvariantCulture),
            episode.ConfigurationVersion));

        return new MarketReactionAssessment(
            id,
            episode.Id,
            episode.LevelId,
            episode.Symbol,
            episode.SessionId,
            asOfUtc,
            episode.StartedAtUtc,
            episode.ExpiresAtUtc,
            status,
            episode.Reaction,
            episode.ReactionAtUtc,
            episode.ReactionEventIdentity,
            episode.ReactionPrice,
            episode.LastObservationAtUtc,
            episode.LastObservationIdentity,
            episode.LastObservationPrice,
            episode.HasCrossedOppositeZone,
            episode.ConfirmationDistancePercent,
            episode.MaxEpisodeDurationSeconds,
            episode.DefinitionVersion,
            episode.ConfigurationVersion);
    }

    private static bool MatchesSituation(MarketSituationInteraction interaction, decimal price) =>
        interaction.SituationType switch
        {
            MarketSituationType.TestingSupport or MarketSituationType.TestingResistance =>
                interaction.ZoneLow <= price && price <= interaction.ZoneHigh,
            MarketSituationType.CrossedSupport => price < interaction.ZoneLow,
            MarketSituationType.CrossedResistance => price > interaction.ZoneHigh,
            _ => false
        };

    private static void ValidateEpisode(LevelInteractionEpisode episode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(episode.Symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(episode.SessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(episode.StartObservationIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(episode.StartObservationProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(episode.DefinitionVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(episode.ConfigurationVersion);
        var validInitiatingSituation = episode.Role switch
        {
            ImportantPriceLevelRole.Support => episode.InitiatingSituation
                is MarketSituationType.TestingSupport or MarketSituationType.CrossedSupport,
            ImportantPriceLevelRole.Resistance => episode.InitiatingSituation
                is MarketSituationType.TestingResistance or MarketSituationType.CrossedResistance,
            _ => false
        };
        if (episode.DefinitionVersion != DefinitionVersion
            || episode.LevelId == Guid.Empty
            || !validInitiatingSituation
            || episode.LevelPrice <= 0
            || episode.ZoneLow <= 0
            || episode.ZoneLow >= episode.LevelPrice
            || episode.ZoneHigh <= episode.LevelPrice
            || episode.ConfirmationDistancePercent is <= 0 or >= 100
            || episode.MaxEpisodeDurationSeconds < 1
            || episode.ExpiresAtUtc != episode.StartedAtUtc.AddSeconds(episode.MaxEpisodeDurationSeconds)
            || episode.LastObservationAtUtc is null
            || episode.LastObservationIdentity is null
            || episode.LastObservationProvider is null
            || episode.LastObservationPrice is null)
        {
            throw new ArgumentException("The reaction episode has invalid or unsupported material state.", nameof(episode));
        }

        var confirmedStateValid =
            episode.Reaction is { } reaction
            && episode.ReactionAtUtc is { } reactionAtUtc
            && episode.ReactionEventIdentity is { } reactionIdentity
            && episode.ReactionPrice is { } reactionPrice
            && ReactionMatchesRole(episode.Role, reaction)
            && reactionAtUtc > episode.StartedAtUtc
            && reactionAtUtc <= episode.ExpiresAtUtc
            && reactionAtUtc == episode.LastObservationAtUtc
            && reactionIdentity == episode.LastObservationIdentity
            && reactionPrice == episode.LastObservationPrice;
        if (episode.Status == MarketReactionEpisodeStatus.Confirmed && !confirmedStateValid)
        {
            throw new ArgumentException("A confirmed reaction episode requires terminal observation evidence.", nameof(episode));
        }

        var activeStateValid =
            episode.Reaction is null
            && episode.ReactionAtUtc is null
            && episode.ReactionEventIdentity is null
            && episode.ReactionPrice is null
            && episode.LastObservationAtUtc is { } activeLastObservationAtUtc
            && activeLastObservationAtUtc < episode.ExpiresAtUtc;
        if (episode.Status == MarketReactionEpisodeStatus.Active && !activeStateValid)
        {
            throw new ArgumentException("An active reaction episode cannot contain terminal state or reach expiry.", nameof(episode));
        }

        var expiredStateValid =
            episode.Reaction is null
            && episode.ReactionAtUtc is null
            && episode.ReactionEventIdentity is null
            && episode.ReactionPrice is null;
        if (episode.Status == MarketReactionEpisodeStatus.Expired && !expiredStateValid)
        {
            throw new ArgumentException("An unresolved expired episode cannot contain a reaction.", nameof(episode));
        }
    }

    private static bool ReactionMatchesRole(
        ImportantPriceLevelRole role,
        MarketReactionType reaction) =>
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

    private static string Format(decimal? value) =>
        value?.ToString("G29", CultureInfo.InvariantCulture) ?? "";

    private sealed class TradeEventComparer : IComparer<TradeEvent>
    {
        public static TradeEventComparer Instance { get; } = new();

        public int Compare(TradeEvent? left, TradeEvent? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            return CompareCanonical(
                left.EventTimeUtc,
                left.Provider,
                left.SequenceId,
                MarketEventIdentity.Create(left),
                right.EventTimeUtc,
                right.Provider,
                right.SequenceId,
                MarketEventIdentity.Create(right));
        }
    }
}
