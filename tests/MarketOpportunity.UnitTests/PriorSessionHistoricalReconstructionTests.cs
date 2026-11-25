using MarketOpportunity.Analytics;
using MarketOpportunity.Application;
using MarketOpportunity.Domain;

namespace MarketOpportunity.UnitTests;

public sealed class PriorSessionHistoricalReconstructionTests
{
    private static readonly DateTimeOffset SessionOpen =
        new(2026, 10, 22, 13, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SessionClose =
        new(2026, 10, 22, 20, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RetrievedAtUtc =
        new(2026, 10, 23, 1, 0, 0, TimeSpan.Zero);
    private static readonly MarketSessionDefinition Session =
        new(
            "alpaca-us-regular-2026-10-22",
            new DateOnly(2026, 10, 22),
            SessionOpen,
            SessionClose,
            "Alpaca",
            "market-calendar-rest",
            "alpaca-calendar-api-v2",
            MarketSessionType.Regular,
            MarketSessionValidity.Valid);
    private static readonly HistoricalReconstructionRequirements Requirements =
        new("Alpaca", "sip", "historical-rest");
    private readonly ScannerConfiguration _configuration = new();
    private readonly PriorSessionHistoricalReconstructor _reconstructor =
        new(new PriorSessionContextBuilder(new FeatureCalculator()));

    [Fact]
    public void Reconstructs_complete_context_with_exact_extrema_close_swings_and_provenance()
    {
        var result = CompleteResult(CreateTrades(
            [1.5m, 1m, 5m, 3m, 2m, 4m, 3m, 7m, 5m, 4m, 6m, 5m]));

        var context = Reconstruct(result);

        Assert.Equal("prior-session-context-v1", context.ContextDefinitionVersion);
        Assert.Equal(PriorSessionContextStatus.Available, context.Completeness);
        Assert.Equal("AAPL", context.Symbol);
        Assert.Equal("current-session", context.CurrentSessionId);
        Assert.Equal(Session.SessionId, context.PriorSessionId);
        Assert.Equal(SessionOpen, context.PriorSessionStartUtc);
        Assert.Equal(SessionClose, context.PriorSessionEndUtc);
        Assert.Equal(7m, context.PriorSessionHigh);
        Assert.Equal(1m, context.PriorSessionLow);
        Assert.Equal(5m, context.PriorSessionClose);
        Assert.Equal(SessionOpen.AddSeconds(12), context.PriorSessionCloseEventTimeUtc);
        Assert.Equal(
            PriorSessionCloseSemantics.LastEligibleTradeAtOrBeforeSessionEnd,
            context.CloseSemantics);
        Assert.Equal(7m, context.LatestConfirmedSwingHigh?.Price);
        Assert.Equal(5m, context.PreviousConfirmedSwingHigh?.Price);
        Assert.Equal(4m, context.LatestConfirmedSwingLow?.Price);
        Assert.Equal(2m, context.PreviousConfirmedSwingLow?.Price);
        Assert.Equal([5m, 7m], context.ResistanceCandidates.Select(item => item.Price));
        Assert.Equal([2m, 4m], context.SupportCandidates.Select(item => item.Price));
        Assert.Equal(MarketStructureState.Bullish, context.StructureState);
        Assert.Equal("Alpaca", context.MarketData?.Provider);
        Assert.Equal("sip", context.MarketData?.Feed);
        Assert.Equal("historical-rest", context.HistoricalSource);
        Assert.True(context.IsSessionStartCovered);
        Assert.True(context.IsSessionEndCovered);
        Assert.False(context.HasKnownGaps);
    }

    [Fact]
    public void Retrieval_timing_changes_do_not_change_context_identity_or_analytics()
    {
        var trades = CreateTrades([1m, 2m, 5m, 3m, 2m, 4m, 3m, 7m, 5m, 4m, 6m, 5m]);
        var firstData = CompleteResult(trades, RetrievedAtUtc);
        var secondData = CompleteResult(
            trades.Select(trade => trade with
            {
                ReceivedTimeUtc = trade.ReceivedTimeUtc.AddHours(7)
            }).ToArray(),
            RetrievedAtUtc.AddHours(7));

        var first = Reconstruct(firstData);
        var replay = Reconstruct(secondData);

        Assert.Equal(first.ContextId, replay.ContextId);
        Assert.Equal(first.PriorSessionHigh, replay.PriorSessionHigh);
        Assert.Equal(first.PriorSessionLow, replay.PriorSessionLow);
        Assert.Equal(first.PriorSessionClose, replay.PriorSessionClose);
        Assert.Equal(first.SupportCandidates, replay.SupportCandidates);
        Assert.Equal(first.ResistanceCandidates, replay.ResistanceCandidates);
    }

    [Theory]
    [InlineData(HistoricalRetrievalStatus.LimitExceeded)]
    [InlineData(HistoricalRetrievalStatus.Cancelled)]
    public void Incomplete_transport_returns_partial_context_with_observed_values(
        HistoricalRetrievalStatus status)
    {
        var result = CompleteResult(CreateTrades()) with
        {
            RetrievalStatus = status,
            PaginationExhausted = false,
            StatusMessage = status.ToString()
        };

        var context = Reconstruct(result);

        Assert.Equal(PriorSessionContextStatus.Partial, context.Completeness);
        Assert.Null(context.PriorSessionHigh);
        Assert.Null(context.PriorSessionLow);
        Assert.Null(context.PriorSessionClose);
        Assert.NotNull(context.ObservedHigh);
        Assert.Contains("partial", context.CompletenessReason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HistoricalRetrievalStatus.AuthenticationFailure)]
    [InlineData(HistoricalRetrievalStatus.EntitlementFailure)]
    [InlineData(HistoricalRetrievalStatus.ProviderFailure)]
    public void Provider_unavailability_returns_unavailable_context(
        HistoricalRetrievalStatus status)
    {
        var context = Reconstruct(CompleteResult(CreateTrades()) with
        {
            RetrievalStatus = status,
            PaginationExhausted = false
        });

        Assert.Equal(PriorSessionContextStatus.Unavailable, context.Completeness);
        Assert.Contains(status.ToString(), context.CompletenessReason);
        Assert.Empty(context.SupportCandidates);
        Assert.Empty(context.ResistanceCandidates);
    }

    [Fact]
    public void Successful_pagination_without_source_completeness_remains_partial()
    {
        var context = Reconstruct(CompleteResult(CreateTrades()) with
        {
            DeclaresCompleteCoverage = false
        });

        Assert.Equal(PriorSessionContextStatus.Partial, context.Completeness);
        Assert.Contains("Pagination completed", context.CompletenessReason);
        Assert.Contains("gap-free", context.CompletenessReason);
    }

    [Fact]
    public void Session_bounds_are_inclusive_but_known_gaps_prevent_completion()
    {
        var trades = CreateTrades([1m, 2m, 5m, 3m, 2m, 4m, 3m, 7m, 5m]);
        trades[0] = Trade(10m, SessionOpen, 1);
        trades[^1] = Trade(5m, SessionClose, trades.Length);
        var inclusive = Reconstruct(CompleteResult(trades));
        var gapped = Reconstruct(CompleteResult(trades) with
        {
            HasKnownGaps = true
        });

        Assert.Equal(PriorSessionContextStatus.Available, inclusive.Completeness);
        Assert.Equal(10m, inclusive.PriorSessionHigh);
        Assert.Equal(5m, inclusive.PriorSessionClose);
        Assert.Equal(PriorSessionContextStatus.Partial, gapped.Completeness);
        Assert.True(gapped.HasKnownGaps);
    }

    [Fact]
    public void Malformed_provenance_zero_trades_and_invalid_values_are_explicit()
    {
        var malformed = Reconstruct(CompleteResult(CreateTrades()) with
        {
            RetrievalStatus = HistoricalRetrievalStatus.MalformedResponse
        });
        var zero = Reconstruct(CompleteResult([]));
        var invalid = Reconstruct(CompleteResult([
            Trade(1m, SessionOpen.AddSeconds(1), 1),
            Trade(2m, SessionOpen.AddSeconds(2), 2),
            Trade(0m, SessionOpen.AddSeconds(3), 3),
            Trade(4m, SessionOpen.AddSeconds(4), 4),
            Trade(5m, SessionOpen.AddSeconds(5), 5, size: 0)
        ]));
        var invalidIncompleteTransport = Reconstruct(CompleteResult([
            Trade(1m, SessionOpen.AddSeconds(1), 1),
            Trade(2m, SessionOpen.AddSeconds(2), 2),
            Trade(0m, SessionOpen.AddSeconds(3), 3)
        ]) with
        {
            RetrievalStatus = HistoricalRetrievalStatus.LimitExceeded,
            PaginationExhausted = false
        });

        Assert.Equal(PriorSessionContextStatus.Invalid, malformed.Completeness);
        Assert.Contains("malformed", malformed.CompletenessReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PriorSessionContextStatus.Unavailable, zero.Completeness);
        Assert.Equal(PriorSessionContextStatus.Invalid, invalid.Completeness);
        Assert.Equal(PriorSessionContextStatus.Invalid, invalidIncompleteTransport.Completeness);
        Assert.Null(invalid.PriorSessionHigh);
        Assert.Empty(invalid.ResistanceCandidates);
    }

    [Theory]
    [InlineData("feed")]
    [InlineData("provider")]
    [InlineData("source")]
    [InlineData("symbol")]
    [InlineData("session")]
    [InlineData("bounds")]
    public void Provenance_mismatches_are_rejected(string mismatch)
    {
        var source = CompleteResult(CreateTrades());
        source = mismatch switch
        {
            "feed" => source with
            {
                Capabilities = source.Capabilities with { Feed = "iex" }
            },
            "provider" => source with
            {
                Provenance = source.Provenance! with { Provider = "Other" }
            },
            "source" => source with { Source = "live-websocket" },
            "symbol" => source with
            {
                Request = source.Request! with { Symbol = "MSFT" }
            },
            "session" => source with
            {
                Request = source.Request! with { SessionId = "other-session" }
            },
            "bounds" => source with
            {
                Request = source.Request! with { EndUtc = SessionClose.AddMinutes(-1) }
            },
            _ => source
        };

        var context = Reconstruct(source);

        Assert.Equal(PriorSessionContextStatus.Invalid, context.Completeness);
        Assert.Contains(
            mismatch is "feed" or "provider" or "source" ? "provenance" : "request",
            context.CompletenessReason,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Duplicate_replay_is_idempotent_and_identity_collision_is_invalid()
    {
        var trades = CreateTrades([1m, 2m, 5m, 3m, 2m, 4m, 3m, 7m, 5m]);
        var duplicate = CompleteResult(trades.Concat([trades[4] with { ReceivedTimeUtc = RetrievedAtUtc.AddHours(2) }]).ToArray());
        var collision = CompleteResult(trades.Concat([trades[4] with { Price = 99m }]).ToArray());

        var first = Reconstruct(CompleteResult(trades));
        var replay = Reconstruct(duplicate);
        var conflicting = Reconstruct(collision);

        Assert.Equal(first.ContextId, replay.ContextId);
        Assert.Equal(first.SupportCandidates, replay.SupportCandidates);
        Assert.Equal(PriorSessionContextStatus.Invalid, conflicting.Completeness);
        Assert.Contains("conflicting", conflicting.CompletenessReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Out_of_session_other_symbol_and_other_session_records_cannot_leak()
    {
        var valid = CreateTrades([1m, 2m, 5m, 3m, 2m, 4m, 3m, 7m, 5m, 4m, 6m, 5m]);
        var leaked = valid.Concat(
        [
            valid[0] with
            {
                Symbol = "MSFT",
                EventTimeUtc = SessionOpen.AddSeconds(1),
                SequenceId = "wrong-symbol"
            },
            valid[0] with
            {
                SessionId = "current-session",
                EventTimeUtc = SessionClose.AddDays(1),
                SequenceId = "current-session"
            },
            valid[0] with
            {
                EventTimeUtc = SessionClose.AddSeconds(1),
                SequenceId = "after-hours"
            },
            valid[0] with
            {
                EventTimeUtc = SessionOpen.AddSeconds(-1),
                SequenceId = "pre-market"
            }
        ]).ToArray();

        var baseline = Reconstruct(CompleteResult(valid));
        var reconstructed = Reconstruct(CompleteResult(leaked));

        Assert.Equal(baseline.ContextId, reconstructed.ContextId);
        Assert.Equal(baseline.PriorSessionHigh, reconstructed.PriorSessionHigh);
        Assert.Equal(baseline.SupportCandidates, reconstructed.SupportCandidates);
    }

    [Fact]
    public void Pivot_near_session_end_remains_unconfirmed_by_prior_or_current_session_trades()
    {
        var priorOnly = CompleteResult(CreateTrades([1m, 2m, 10m, 3m, 4m, 20m]));
        var withNextSession = priorOnly with
        {
            Events = priorOnly.Events.Concat(
            [
                Trade(8m, SessionClose.AddHours(13), 20, "current-session"),
                Trade(7m, SessionClose.AddHours(13).AddSeconds(1), 21, "current-session")
            ]).ToArray()
        };

        var priorContext = Reconstruct(priorOnly);
        var mixedContext = Reconstruct(withNextSession);

        Assert.DoesNotContain(priorContext.ResistanceCandidates, item => item.Price == 20m);
        Assert.DoesNotContain(mixedContext.ResistanceCandidates, item => item.Price == 20m);
        Assert.Equal(priorContext.ContextId, mixedContext.ContextId);
        Assert.Contains(priorContext.ResistanceCandidates, item => item.Price == 10m);
    }

    [Fact]
    public void Early_close_uses_shortened_regular_session_bounds()
    {
        var earlyClose = SessionClose.AddHours(-3);
        var earlySession = Session with { CloseAtUtc = earlyClose };
        var regularTrades = CreateTrades(
            [1.5m, 1m, 5m, 3m, 2m, 4m, 3m, 7m, 5m, 4m, 6m, 5m],
            earlySession.OpenAtUtc,
            earlySession.CloseAtUtc);
        var extendedHours = Trade(999m, earlyClose.AddMinutes(1), 50);
        var source = CompleteResult(
            regularTrades.Append(extendedHours).ToArray(),
            closeAtUtc: earlyClose);

        var context = _reconstructor.Reconstruct(
            "AAPL",
            "current-session",
            earlySession,
            source with
            {
                Request = source.Request! with { EndUtc = earlyClose }
            },
            Requirements,
            _configuration);

        Assert.Equal(PriorSessionContextStatus.Available, context.Completeness);
        Assert.Equal(earlyClose, context.PriorSessionEndUtc);
        Assert.Equal(7m, context.PriorSessionHigh);
        Assert.Equal(5m, context.PriorSessionClose);
        Assert.DoesNotContain(context.ResistanceCandidates, item => item.Price == 999m);
    }

    [Fact]
    public void Complete_reconstruction_supplies_prior_levels_but_partial_context_does_not()
    {
        var historical = CompleteResult(CreateTrades(
            [100m, 99m, 98m, 100m, 101m, 102m, 100m, 99m, 101m, 100m]));
        var complete = Reconstruct(historical);
        var partial = Reconstruct(historical with { DeclaresCompleteCoverage = false });
        var currentEvents = CurrentEvents();
        var snapshot = CurrentSnapshot(currentEvents);
        var evaluator = new MarketSituationEvaluator();

        var enhanced = evaluator.Evaluate(
            snapshot,
            currentEvents,
            PriorSessionContextBuilder.CreateView(complete, snapshot.ReferencePrice),
            _configuration,
            snapshot.AsOfUtc);
        var currentOnly = evaluator.Evaluate(
            snapshot,
            currentEvents,
            PriorSessionContextBuilder.CreateView(partial, snapshot.ReferencePrice),
            _configuration,
            snapshot.AsOfUtc);

        Assert.Equal(MarketSituationContextMode.PriorSessionEnhanced, enhanced.ContextMode);
        Assert.Contains(enhanced.EligibleLevels, item =>
            item.Source == ImportantPriceLevelSource.PriorSwingLow);
        Assert.Contains(enhanced.EligibleLevels, item =>
            item.Source == ImportantPriceLevelSource.PriorSwingHigh);
        Assert.Contains(enhanced.EligibleLevels, item =>
            item.Source == ImportantPriceLevelSource.PriorSessionHigh);
        Assert.Contains(enhanced.EligibleLevels, item =>
            item.Source == ImportantPriceLevelSource.PriorSessionLow);
        Assert.Equal(MarketSituationContextMode.CurrentSessionOnly, currentOnly.ContextMode);
        Assert.Empty(currentOnly.EligibleLevels);
    }

    [Fact]
    public void MarketReaction_can_pin_a_level_created_from_reconstructed_prior_context()
    {
        var complete = Reconstruct(CompleteResult(CreateTrades(
            [100m, 99m, 98m, 100m, 101m, 102m, 100m, 99m, 101m, 100m])));
        var currentEvents = CurrentEvents(98m);
        var snapshot = CurrentSnapshot(currentEvents);
        var situation = new MarketSituationEvaluator().Evaluate(
            snapshot,
            currentEvents,
            PriorSessionContextBuilder.CreateView(complete, snapshot.ReferencePrice),
            _configuration,
            snapshot.AsOfUtc);
        var interaction = Assert.IsType<MarketSituationInteraction>(situation.SupportInteraction);

        var episode = new MarketReactionEvaluator().StartEpisode(
            interaction,
            Assert.IsType<TradeEvent>(currentEvents[^1]),
            _configuration);

        Assert.Equal(ImportantPriceLevelSource.PriorSwingLow, interaction.Level?.Source);
        Assert.Equal(98m, episode.LevelPrice);
        Assert.Equal("current-session", episode.SessionId);
        Assert.Equal(MarketReactionEpisodeStatus.Active, episode.Status);
    }

    private PriorSessionContext Reconstruct(HistoricalMarketSessionData historicalData) =>
        _reconstructor.Reconstruct(
            "AAPL",
            "current-session",
            Session,
            historicalData,
            Requirements,
            _configuration);

    private static HistoricalMarketSessionData CompleteResult(
        IReadOnlyList<TradeEvent> trades,
        DateTimeOffset? retrievedAtUtc = null,
        DateTimeOffset? closeAtUtc = null)
    {
        var retrieved = retrievedAtUtc ?? RetrievedAtUtc;
        var close = closeAtUtc ?? SessionClose;
        return new HistoricalMarketSessionData(
            trades,
            new MarketDataCapabilities(
                "Alpaca",
                "sip",
                MarketDataCoverage.Consolidated,
                false,
                true,
                false),
            "historical-rest",
            SessionOpen,
            close,
            DeclaresCompleteCoverage: true,
            HasKnownGaps: false,
            new HistoricalDataRequest("AAPL", Session.SessionId, SessionOpen, close),
            HistoricalRetrievalStatus.SuccessPaginationExhausted,
            new HistoricalDataProvenance(
                "Alpaca",
                "sip",
                "historical-rest",
                retrieved,
                81,
                0),
            PaginationExhausted: true);
    }

    private static TradeEvent[] CreateTrades(
        decimal[]? prices = null,
        DateTimeOffset? start = null,
        DateTimeOffset? end = null)
    {
        prices ??= [1.5m, 1m, 5m, 3m, 2m, 4m, 3m, 7m, 5m, 4m, 6m, 5m];
        var usesWholeDefaultSession = start is null && end is null;
        var open = start ?? SessionOpen;
        var close = end ?? SessionClose;
        var intervalTicks = (close - open).Ticks / (prices.Length + 1);
        return prices.Select((price, index) =>
            Trade(
                price,
                usesWholeDefaultSession
                    ? open.AddSeconds(index + 1)
                    : open.AddTicks(intervalTicks * (index + 1)),
                index + 1)).ToArray();
    }

    private static TradeEvent Trade(
        decimal price,
        DateTimeOffset eventTime,
        int id,
        string sessionId = "alpaca-us-regular-2026-10-22",
        long size = 10) =>
        new(
            "AAPL",
            sessionId,
            eventTime,
            RetrievedAtUtc,
            "Alpaca",
            HistoricalTradeIdentity.Create(
                "Alpaca",
                "sip",
                "AAPL",
                "V",
                id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                eventTime),
            price,
            size);

    private static MarketEvent[] CurrentEvents(decimal price = 100m)
    {
        var start = SessionClose.AddDays(1);
        return
        [
            Trade(price, start, 101, "current-session"),
            Trade(price, start.AddSeconds(1), 102, "current-session"),
            Trade(price, start.AddSeconds(2), 103, "current-session")
        ];
    }

    private FeatureSnapshot CurrentSnapshot(IReadOnlyList<MarketEvent> events)
    {
        var state = new SymbolEventState("AAPL", "current-session", 100);
        foreach (var marketEvent in events)
        {
            state.Add(marketEvent);
        }

        return new FeatureCalculator().Calculate(
            state,
            events.Max(item => item.EventTimeUtc),
            _configuration,
            new MarketDataCapabilities(
                "Alpaca",
                "sip",
                MarketDataCoverage.Consolidated,
                true,
                true,
                false));
    }
}
