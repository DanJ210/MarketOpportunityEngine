using System.Globalization;
using System.Text.Json;
using MarketOpportunity.Analytics;
using MarketOpportunity.Domain;

namespace MarketOpportunity.UnitTests;

public sealed class MarketReactionEvaluatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 5, 14, 30, 0, TimeSpan.Zero);
    private readonly MarketReactionEvaluator _evaluator = new();
    private readonly ScannerConfiguration _configuration = new();

    [Fact]
    public void Uses_provisional_versioned_defaults_and_validates_reaction_policy()
    {
        _configuration.Validate();

        Assert.Equal("scanner-config-v19", _configuration.Version);
        Assert.Equal(0.10m, _configuration.MarketReaction.ConfirmationDistancePercent);
        Assert.Equal(300, _configuration.MarketReaction.MaxEpisodeDurationSeconds);
        Assert.Equal(
            _configuration,
            JsonSerializer.Deserialize<ScannerConfiguration>(JsonSerializer.Serialize(_configuration)));

        Assert.Throws<InvalidOperationException>(() =>
            (_configuration with
            {
                MarketReaction = new MarketReactionConfiguration { ConfirmationDistancePercent = 0m }
            }).Validate());
        Assert.Throws<InvalidOperationException>(() =>
            (_configuration with
            {
                MarketReaction = new MarketReactionConfiguration { ConfirmationDistancePercent = 100m }
            }).Validate());
        Assert.Throws<InvalidOperationException>(() =>
            (_configuration with
            {
                MarketReaction = new MarketReactionConfiguration { MaxEpisodeDurationSeconds = 0 }
            }).Validate());
        var firstEpisode = StartEpisode(
            ImportantPriceLevelRole.Support,
            MarketSituationType.TestingSupport,
            100m);
        var replayedEpisode = StartEpisode(
            ImportantPriceLevelRole.Support,
            MarketSituationType.TestingSupport,
            100m);
        Assert.Equal(firstEpisode.Id, replayedEpisode.Id);
    }

    [Theory]
    [InlineData(
        ImportantPriceLevelRole.Support,
        MarketSituationType.TestingSupport,
        "100",
        MarketReactionType.SupportHeld,
        "100.2001")]
    [InlineData(
        ImportantPriceLevelRole.Support,
        MarketSituationType.TestingSupport,
        "100",
        MarketReactionType.SupportBreakdown,
        "99.8001")]
    [InlineData(
        ImportantPriceLevelRole.Support,
        MarketSituationType.TestingSupport,
        "100",
        MarketReactionType.SupportReclaimed,
        "100.2001")]
    [InlineData(
        ImportantPriceLevelRole.Resistance,
        MarketSituationType.TestingResistance,
        "100",
        MarketReactionType.ResistanceRejected,
        "99.8001")]
    [InlineData(
        ImportantPriceLevelRole.Resistance,
        MarketSituationType.TestingResistance,
        "100",
        MarketReactionType.ResistanceBreakout,
        "100.2001")]
    [InlineData(
        ImportantPriceLevelRole.Resistance,
        MarketSituationType.TestingResistance,
        "100",
        MarketReactionType.ResistanceFailedBreakout,
        "99.8001")]
    public void Confirms_each_mechanical_reaction_from_later_observations(
        ImportantPriceLevelRole role,
        MarketSituationType startSituation,
        string startPrice,
        MarketReactionType expectedReaction,
        string terminalPrice)
    {
        var parsedStartPrice = decimal.Parse(startPrice, CultureInfo.InvariantCulture);
        var parsedTerminalPrice = decimal.Parse(terminalPrice, CultureInfo.InvariantCulture);
        var episode = StartEpisode(role, startSituation, parsedStartPrice);
        var observations = expectedReaction switch
        {
            MarketReactionType.SupportReclaimed =>
                new MarketEvent[] { Trade(1, 99.89m), Trade(2, parsedTerminalPrice) },
            MarketReactionType.ResistanceFailedBreakout =>
                new MarketEvent[] { Trade(1, 100.11m), Trade(2, parsedTerminalPrice) },
            _ => [Trade(1, parsedTerminalPrice)]
        };

        var result = _evaluator.Evaluate(episode, observations, Start.AddSeconds(3));

        Assert.Equal(MarketReactionEpisodeStatus.Confirmed, result.Episode.Status);
        Assert.Equal(expectedReaction, result.Episode.Reaction);
        Assert.Equal(MarketReactionStatus.Confirmed, result.Assessment.Status);
        Assert.Equal(parsedTerminalPrice, result.Assessment.ReactionPrice);
        Assert.Equal(observations[^1].EventTimeUtc, result.Assessment.ReactionAtUtc);
        Assert.Equal(MarketReactionEvaluator.DefinitionVersion, result.Assessment.DefinitionVersion);
        Assert.Equal(_configuration.Version, result.Assessment.ConfigurationVersion);
    }

    [Theory]
    [InlineData(ImportantPriceLevelRole.Support, MarketSituationType.CrossedSupport, "99.79", "100.21",
        MarketReactionType.SupportReclaimed)]
    [InlineData(ImportantPriceLevelRole.Resistance, MarketSituationType.CrossedResistance, "100.21", "99.79",
        MarketReactionType.ResistanceFailedBreakout)]
    public void Initiating_crossed_observation_does_not_confirm_its_own_reaction(
        ImportantPriceLevelRole role,
        MarketSituationType situation,
        string initiatingPrice,
        string laterPrice,
        MarketReactionType expectedReaction)
    {
        var episode = StartEpisode(
            role,
            situation,
            decimal.Parse(initiatingPrice, CultureInfo.InvariantCulture));

        var unresolved = _evaluator.Evaluate(episode, [], Start);
        var later = _evaluator.Evaluate(
            episode,
            [Trade(1, decimal.Parse(laterPrice, CultureInfo.InvariantCulture))],
            Start.AddSeconds(1));

        Assert.Equal(MarketReactionEpisodeStatus.Active, unresolved.Episode.Status);
        Assert.Equal(MarketReactionStatus.Unresolved, unresolved.Assessment.Status);
        Assert.Null(unresolved.Assessment.Reaction);
        Assert.Equal(expectedReaction, later.Assessment.Reaction);
        Assert.NotEqual(episode.StartObservationIdentity, later.Assessment.ReactionEventIdentity);
    }

    [Fact]
    public void A_small_test_zone_crossing_is_not_a_confirmed_reaction()
    {
        var support = StartEpisode(
            ImportantPriceLevelRole.Support,
            MarketSituationType.TestingSupport,
            100m);
        var resistance = StartEpisode(
            ImportantPriceLevelRole.Resistance,
            MarketSituationType.TestingResistance,
            100m);

        var crossedSupport = _evaluator.Evaluate(support, [Trade(1, 99.89m)], Start.AddSeconds(1));
        var crossedResistance = _evaluator.Evaluate(resistance, [Trade(1, 100.11m)], Start.AddSeconds(1));

        Assert.Equal(MarketReactionStatus.Unresolved, crossedSupport.Assessment.Status);
        Assert.Equal(MarketReactionStatus.Unresolved, crossedResistance.Assessment.Status);
        Assert.Null(crossedSupport.Assessment.Reaction);
        Assert.Null(crossedResistance.Assessment.Reaction);
        Assert.True(crossedSupport.Assessment.HasCrossedOppositeZone);
        Assert.True(crossedResistance.Assessment.HasCrossedOppositeZone);
    }

    [Fact]
    public void Uses_inclusive_confirmation_boundaries_without_rounding()
    {
        var support = StartEpisode(
            ImportantPriceLevelRole.Support,
            MarketSituationType.TestingSupport,
            100m);

        var belowUpperBoundary = _evaluator.Evaluate(
            support,
            [Trade(1, 100.2m)],
            Start.AddSeconds(1));
        var atUpperBoundary = _evaluator.Evaluate(
            support,
            [Trade(1, 100.2001m)],
            Start.AddSeconds(1));

        Assert.Equal(MarketReactionStatus.Unresolved, belowUpperBoundary.Assessment.Status);
        Assert.Equal(MarketReactionType.SupportHeld, atUpperBoundary.Assessment.Reaction);
    }

    [Fact]
    public void Orders_and_deduplicates_later_trades_and_ignores_quotes_other_sessions_and_future_events()
    {
        var episode = StartEpisode(
            ImportantPriceLevelRole.Support,
            MarketSituationType.TestingSupport,
            100m);
        var lower = Trade(1, 99.89m, "001");
        var upper = Trade(1, 100.21m, "002");
        var duplicate = lower with { ReceivedTimeUtc = lower.ReceivedTimeUtc.AddSeconds(5) };
        var wrongSymbol = Trade(2, 1m, "wrong-symbol") with { Symbol = "OTHER" };
        var wrongSession = Trade(2, 1m, "wrong-session") with { SessionId = "other-session" };
        var future = Trade(5, 1m, "future");
        var quote = new QuoteEvent(
            "TEST",
            "current-session",
            Start.AddSeconds(2),
            Start.AddSeconds(3),
            "UnitTest",
            "quote",
            1m,
            2m,
            10,
            10);
        var asOf = Start.AddSeconds(2);
        var events = new MarketEvent[]
        {
            upper, quote, wrongSymbol, duplicate, future, wrongSession, lower
        };

        var result = _evaluator.Evaluate(episode, events, asOf);
        var replay = _evaluator.Evaluate(episode, events.Reverse().ToArray(), asOf);

        Assert.Equal(MarketReactionType.SupportReclaimed, result.Assessment.Reaction);
        Assert.Equal(MarketEventIdentity.Create(upper), result.Assessment.ReactionEventIdentity);
        Assert.Equal(result.Episode, replay.Episode);
        Assert.Equal(result.Assessment, replay.Assessment);

        var conflictingDuplicate = lower with { Price = 99.88m };
        Assert.Throws<InvalidOperationException>(() =>
            _evaluator.Evaluate(episode, [lower, conflictingDuplicate], asOf));
    }

    [Fact]
    public void Does_not_rewrite_the_first_confirmed_terminal_reaction()
    {
        var episode = StartEpisode(
            ImportantPriceLevelRole.Support,
            MarketSituationType.TestingSupport,
            100m);
        var confirmed = _evaluator.Evaluate(
            episode,
            [Trade(1, 100.21m)],
            Start.AddSeconds(1));
        var later = _evaluator.Evaluate(
            confirmed.Episode,
            [Trade(2, 99.79m)],
            Start.AddSeconds(2));

        Assert.Equal(MarketReactionType.SupportHeld, later.Episode.Reaction);
        Assert.Equal(confirmed.Episode.ReactionAtUtc, later.Episode.ReactionAtUtc);
        Assert.Equal(confirmed.Episode.ReactionEventIdentity, later.Episode.ReactionEventIdentity);
        Assert.Equal(confirmed.Assessment.Id, later.Assessment.Id);
    }

    [Fact]
    public void Expires_unresolved_episode_at_event_time_and_ignores_later_observations()
    {
        var configuration = _configuration with
        {
            MarketReaction = new MarketReactionConfiguration { MaxEpisodeDurationSeconds = 10 }
        };
        var episode = StartEpisode(
            ImportantPriceLevelRole.Support,
            MarketSituationType.TestingSupport,
            100m,
            configuration);

        var expired = _evaluator.Evaluate(
            episode,
            [Trade(11, 100.21m)],
            Start.AddSeconds(20));
        var confirmedAtBoundary = _evaluator.Evaluate(
            episode,
            [Trade(10, 100.21m)],
            Start.AddSeconds(20));

        Assert.Equal(MarketReactionEpisodeStatus.Expired, expired.Episode.Status);
        Assert.Equal(MarketReactionStatus.UnresolvedExpired, expired.Assessment.Status);
        Assert.Equal(Start.AddSeconds(10), expired.Assessment.AsOfUtc);
        Assert.Null(expired.Assessment.Reaction);
        Assert.Null(expired.Assessment.ReactionAtUtc);
        Assert.Equal(MarketReactionType.SupportHeld, confirmedAtBoundary.Assessment.Reaction);
    }

    [Fact]
    public void Rejects_unsupported_starts_and_event_time_rewind()
    {
        var support = Interaction(ImportantPriceLevelRole.Support, MarketSituationType.TestingSupport);
        Assert.Throws<ArgumentException>(() =>
            _evaluator.StartEpisode(
                support with { SituationType = MarketSituationType.ApproachingSupport },
                Trade(0, 100m),
                _configuration));
        Assert.Throws<ArgumentException>(() =>
            _evaluator.StartEpisode(
                support,
                Trade(0, 99m),
                _configuration));
        Assert.Throws<ArgumentException>(() =>
            _evaluator.StartEpisode(
                support,
                Trade(0, 100m) with { SessionId = "other-session" },
                _configuration));

        var episode = StartEpisode(
            ImportantPriceLevelRole.Support,
            MarketSituationType.TestingSupport,
            100m);
        var afterObservation = _evaluator.Evaluate(
            episode,
            [Trade(2, 100m)],
            Start.AddSeconds(2));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _evaluator.Evaluate(afterObservation.Episode, [], Start.AddSeconds(1)));
        var terminal = _evaluator.Evaluate(episode, [Trade(1, 100.21m)], Start.AddSeconds(1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _evaluator.Evaluate(terminal.Episode, [], Start));
    }

    [Fact]
    public void Restart_marks_unresolved_episode_continuity_unknown_and_prevents_confirmation()
    {
        var episode = StartEpisode(
            ImportantPriceLevelRole.Support,
            MarketSituationType.TestingSupport,
            100m);

        var continuityUnknown = _evaluator.MarkContinuityUnknown(episode);
        var afterRestart = _evaluator.Evaluate(
            continuityUnknown.Episode,
            [Trade(1, 100.21m)],
            Start.AddSeconds(1));

        Assert.Equal(MarketReactionEpisodeStatus.ContinuityUnknown, continuityUnknown.Episode.Status);
        Assert.Equal(MarketReactionStatus.ContinuityUnknown, continuityUnknown.Assessment.Status);
        Assert.Equal(episode.LastObservationAtUtc, continuityUnknown.Assessment.AsOfUtc);
        Assert.Equal(MarketReactionEpisodeStatus.ContinuityUnknown, afterRestart.Episode.Status);
        Assert.Null(afterRestart.Episode.Reaction);
        Assert.Equal(MarketReactionStatus.ContinuityUnknown, afterRestart.Assessment.Status);

        var repeatedTransition = _evaluator.MarkContinuityUnknown(continuityUnknown.Episode);
        Assert.Equal(continuityUnknown, repeatedTransition);
        Assert.Throws<InvalidOperationException>(() => _evaluator.MarkContinuityUnknown(
            _evaluator.Evaluate(episode, [Trade(1, 100.21m)], Start.AddSeconds(1)).Episode));
    }

    private LevelInteractionEpisode StartEpisode(
        ImportantPriceLevelRole role,
        MarketSituationType situation,
        decimal initiatingPrice,
        ScannerConfiguration? configuration = null)
    {
        var interaction = Interaction(role, situation);
        return _evaluator.StartEpisode(
            interaction,
            Trade(0, initiatingPrice),
            configuration ?? _configuration);
    }

    private static MarketSituationInteraction Interaction(
        ImportantPriceLevelRole role,
        MarketSituationType situation)
    {
        var level = new ImportantPriceLevel(
            role == ImportantPriceLevelRole.Support ? Guid.Parse("11111111-1111-1111-1111-111111111111")
                : Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "TEST",
            "current-session",
            "current-session",
            100m,
            role,
            role == ImportantPriceLevelRole.Support
                ? ImportantPriceLevelSource.CurrentSwingLow
                : ImportantPriceLevelSource.CurrentSwingHigh,
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            null,
            "source-event",
            "confirmation-event",
            Start.AddMinutes(-1),
            Start,
            MarketSituationContextMode.CurrentSessionOnly,
            "Complete current-session retained event history",
            "UnitTest",
            null,
            null,
            "session-market-structure-v1",
            "features-v16",
            null);
        return new MarketSituationInteraction(
            situation,
            level,
            99.9m,
            100.1m,
            0m,
            0m,
            0m,
            0m,
            0m);
    }

    private static TradeEvent Trade(int seconds, decimal price, string? sequenceId = null) =>
        new(
            "TEST",
            "current-session",
            Start.AddSeconds(seconds),
            Start.AddSeconds(seconds + 1),
            "UnitTest",
            sequenceId ?? $"trade-{seconds:D3}",
            price,
            10);
}
