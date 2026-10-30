using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MarketOpportunity.Analytics;
using MarketOpportunity.Domain;

namespace MarketOpportunity.UnitTests;

public sealed class MarketStructureTests
{
    private const string Symbol = "STRUCT";
    private const string SessionId = "structure-session";
    private static readonly DateTimeOffset Start = new(2026, 10, 23, 14, 30, 0, TimeSpan.Zero);
    private static readonly decimal[] Prices = [1.5m, 1m, 5m, 3m, 2m, 4m, 3m, 7m, 5m, 4m, 6m, 5m];
    private readonly FeatureCalculator _calculator = new();
    private readonly ScannerConfiguration _configuration = new();

    [Fact]
    public void Reports_a_swing_only_at_the_second_subsequent_trade_with_auditable_identity()
    {
        var trades = CreateTrades(Prices);
        var state = State(trades);
        var before = _calculator.Calculate(state, trades[4].EventTimeUtc.AddTicks(-1), _configuration);
        var atConfirmation = _calculator.Calculate(state, trades[4].EventTimeUtc, _configuration);
        var laterHigh = _calculator.Calculate(state, trades[9].EventTimeUtc, _configuration);
        var final = _calculator.Calculate(state, trades[11].EventTimeUtc, _configuration);

        Assert.Equal(MarketStructureStatus.Insufficient, before.MarketStructureStatus);
        Assert.Null(before.LatestConfirmedSwingHigh);
        Assert.Equal(MarketStructureStatus.Available, atConfirmation.MarketStructureStatus);
        var firstHigh = Assert.IsType<ConfirmedSwing>(atConfirmation.LatestConfirmedSwingHigh);
        Assert.Equal(ConfirmedSwingType.ConfirmedSwingHigh, firstHigh.Type);
        Assert.Equal(Symbol, firstHigh.Symbol);
        Assert.Equal(SessionId, firstHigh.SessionId);
        Assert.Equal(5m, firstHigh.Price);
        Assert.Equal(trades[2].EventTimeUtc, firstHigh.EventTimeUtc);
        Assert.Equal(trades[4].EventTimeUtc, firstHigh.ConfirmedAtEventTimeUtc);
        Assert.Equal(MarketEventIdentity.Create(trades[2]), firstHigh.OriginEventIdentity);
        Assert.Equal(MarketEventIdentity.Create(trades[4]), firstHigh.ConfirmationEventIdentity);
        Assert.Equal("features-v15", firstHigh.FeatureSetVersion);
        Assert.Equal("session-swing-fractal-v1", firstHigh.AlgorithmVersion);
        Assert.Equal(ExpectedId(firstHigh), firstHigh.Id);
        Assert.Null(atConfirmation.LatestConfirmedSwingLow);

        var high = Assert.IsType<ConfirmedSwing>(laterHigh.LatestConfirmedSwingHigh);
        Assert.Equal(7m, high.Price);
        Assert.Equal(trades[7].EventTimeUtc, high.EventTimeUtc);
        Assert.Equal(trades[9].EventTimeUtc, high.ConfirmedAtEventTimeUtc);
        Assert.Equal(high, final.LatestConfirmedSwingHigh);
        var low = Assert.IsType<ConfirmedSwing>(final.LatestConfirmedSwingLow);
        Assert.Equal(ConfirmedSwingType.ConfirmedSwingLow, low.Type);
        Assert.Equal(4m, low.Price);
        Assert.Equal(trades[9].EventTimeUtc, low.EventTimeUtc);
        Assert.Equal(trades[11].EventTimeUtc, low.ConfirmedAtEventTimeUtc);
        Assert.Equal(ExpectedId(low), low.Id);
        Assert.NotEqual(high.Id, low.Id);
        Assert.Equal(PriceStructure.HigherHighHigherLow, final.SessionPriceStructure);
    }

    [Fact]
    public void Excludes_future_events_and_does_not_change_prior_swing_ids_as_as_of_advances()
    {
        var trades = CreateTrades(Prices);
        var early = State(trades.Take(5));
        var withFuture = State(trades);
        withFuture.Add(trades[^1] with
        {
            SequenceId = "future-invalid",
            EventTimeUtc = trades[^1].EventTimeUtc.AddSeconds(1),
            Price = 0m
        });

        var baseline = _calculator.Calculate(early, trades[4].EventTimeUtc, _configuration);
        var replay = _calculator.Calculate(withFuture, trades[4].EventTimeUtc, _configuration);
        var sameSwingLater = _calculator.Calculate(withFuture, trades[5].EventTimeUtc, _configuration);

        Assert.Equal(baseline, replay);
        Assert.Equal(baseline.LatestConfirmedSwingHigh, sameSwingLater.LatestConfirmedSwingHigh);
        Assert.NotEqual(baseline.Id, sameSwingLater.Id);
    }

    [Fact]
    public void Canonical_order_handles_equal_timestamps_reordering_and_duplicate_identities()
    {
        var trades = CreateTrades(Prices)
            .Select(trade => trade with { EventTimeUtc = Start })
            .ToArray();
        var orderedState = State(trades);
        var reorderedState = State(trades.Reverse().Select((trade, index) =>
            trade with { ReceivedTimeUtc = Start.AddDays(index + 1) }));
        Assert.False(reorderedState.Add(trades[0] with { Price = 999m, ReceivedTimeUtc = Start.AddYears(1) }));

        var ordered = _calculator.Calculate(orderedState, Start, _configuration);
        var reordered = _calculator.Calculate(reorderedState, Start, _configuration);

        Assert.Equal(ordered, reordered);
        var high = Assert.IsType<ConfirmedSwing>(ordered.LatestConfirmedSwingHigh);
        var low = Assert.IsType<ConfirmedSwing>(ordered.LatestConfirmedSwingLow);
        Assert.Equal(Start, high.EventTimeUtc);
        Assert.Equal(Start, high.ConfirmedAtEventTimeUtc);
        Assert.Equal(MarketEventIdentity.Create(trades[7]), high.OriginEventIdentity);
        Assert.Equal(MarketEventIdentity.Create(trades[9]), high.ConfirmationEventIdentity);
        Assert.Equal(MarketEventIdentity.Create(trades[11]), low.ConfirmationEventIdentity);
    }

    [Fact]
    public void Equal_price_plateaus_select_the_earliest_canonical_trade()
    {
        var highTrades = CreateTrades([1m, 2m, 5m, 5m, 4m, 3m]);
        var lowTrades = CreateTrades([5m, 4m, 1m, 1m, 2m, 3m]);
        var high = _calculator.Calculate(State(highTrades), highTrades[^1].EventTimeUtc, _configuration)
            .LatestConfirmedSwingHigh;
        var low = _calculator.Calculate(State(lowTrades), lowTrades[^1].EventTimeUtc, _configuration)
            .LatestConfirmedSwingLow;

        Assert.NotNull(high);
        Assert.NotNull(low);
        Assert.Equal(highTrades[2].EventTimeUtc, high.EventTimeUtc);
        Assert.Equal(highTrades[4].EventTimeUtc, high.ConfirmedAtEventTimeUtc);
        Assert.Equal(lowTrades[2].EventTimeUtc, low.EventTimeUtc);
        Assert.Equal(lowTrades[4].EventTimeUtc, low.ConfirmedAtEventTimeUtc);
    }

    [Fact]
    public void Swing_times_are_utc_and_identity_is_independent_of_timestamp_offset()
    {
        var trades = CreateTrades(Prices);
        var offsetTrades = trades.Select(trade => trade with
        {
            EventTimeUtc = trade.EventTimeUtc.ToOffset(TimeSpan.FromHours(-4))
        }).ToArray();
        var baseline = _calculator.Calculate(State(trades), trades[^1].EventTimeUtc, _configuration);
        var offset = _calculator.Calculate(State(offsetTrades), trades[^1].EventTimeUtc, _configuration);

        Assert.Equal(baseline.LatestConfirmedSwingHigh, offset.LatestConfirmedSwingHigh);
        Assert.Equal(baseline.LatestConfirmedSwingLow, offset.LatestConfirmedSwingLow);
        Assert.Equal(TimeSpan.Zero, offset.LatestConfirmedSwingHigh!.EventTimeUtc.Offset);
        Assert.Equal(TimeSpan.Zero, offset.LatestConfirmedSwingHigh.ConfirmedAtEventTimeUtc.Offset);
        Assert.Equal(TimeSpan.Zero, offset.LatestConfirmedSwingLow!.EventTimeUtc.Offset);
        Assert.Equal(TimeSpan.Zero, offset.LatestConfirmedSwingLow.ConfirmedAtEventTimeUtc.Offset);
    }

    [Fact]
    public void Distinguishes_insufficient_invalid_and_valid_inputs_without_a_confirmed_pivot()
    {
        var noTrades = _calculator.Calculate(State([]), Start, _configuration);
        var tooFew = CreateTrades([1m, 2m, 3m, 4m]);
        var rising = CreateTrades([1m, 2m, 3m, 4m, 5m]);
        var invalid = CreateTrades([1m, 2m, 0m, 4m, 5m]);

        Assert.Equal(MarketStructureStatus.Insufficient, noTrades.MarketStructureStatus);
        Assert.Equal(MarketStructureStatus.Insufficient,
            _calculator.Calculate(State(tooFew), tooFew[^1].EventTimeUtc, _configuration).MarketStructureStatus);
        var monotonic = _calculator.Calculate(State(rising), rising[^1].EventTimeUtc, _configuration);
        Assert.Equal(MarketStructureStatus.Available, monotonic.MarketStructureStatus);
        Assert.Null(monotonic.LatestConfirmedSwingHigh);
        Assert.Null(monotonic.LatestConfirmedSwingLow);
        var invalidSnapshot = _calculator.Calculate(State(invalid), invalid[^1].EventTimeUtc, _configuration);
        Assert.Equal(MarketStructureStatus.InvalidPriceData, invalidSnapshot.MarketStructureStatus);
        Assert.Null(invalidSnapshot.LatestConfirmedSwingHigh);
        Assert.Null(invalidSnapshot.LatestConfirmedSwingLow);
    }

    [Fact]
    public void Quotes_and_trade_sizes_do_not_supply_or_modify_swing_observations()
    {
        var trades = CreateTrades(Prices);
        var baseline = _calculator.Calculate(State(trades), trades[^1].EventTimeUtc, _configuration);
        var changed = State(trades.Select(trade => trade with { Size = -1 }));
        changed.Add(new QuoteEvent(
            Symbol, SessionId, trades[^1].EventTimeUtc, Start, "Fixture", "quote",
            1_000m, 1_001m, 10, 10));
        var result = _calculator.Calculate(changed, trades[^1].EventTimeUtc, _configuration);

        Assert.Equal(MarketStructureStatus.Available, result.MarketStructureStatus);
        Assert.Equal(baseline.LatestConfirmedSwingHigh, result.LatestConfirmedSwingHigh);
        Assert.Equal(baseline.LatestConfirmedSwingLow, result.LatestConfirmedSwingLow);
        Assert.False(result.IsPriceDataValid);
    }

    [Fact]
    public void Isolates_symbols_sessions_and_feature_versions()
    {
        var trades = CreateTrades(Prices);
        var otherSymbol = CreateTrades(Prices, "OTHER", SessionId);
        var otherSession = CreateTrades(Prices, Symbol, "other-session");
        var asOf = trades[^1].EventTimeUtc;
        var baseline = _calculator.Calculate(State(trades), asOf, _configuration);
        var symbol = _calculator.Calculate(State(otherSymbol, "OTHER", SessionId), asOf, _configuration);
        var session = _calculator.Calculate(State(otherSession, Symbol, "other-session"), asOf, _configuration);
        var version = _calculator.Calculate(
            State(trades), asOf, _configuration with { FeatureSetVersion = "alternate-features" });

        Assert.Equal("OTHER", symbol.LatestConfirmedSwingHigh!.Symbol);
        Assert.Equal("other-session", session.LatestConfirmedSwingHigh!.SessionId);
        Assert.Equal("alternate-features", version.LatestConfirmedSwingHigh!.FeatureSetVersion);
        Assert.NotEqual(baseline.LatestConfirmedSwingHigh!.Id, symbol.LatestConfirmedSwingHigh.Id);
        Assert.NotEqual(baseline.LatestConfirmedSwingHigh.Id, session.LatestConfirmedSwingHigh.Id);
        Assert.NotEqual(baseline.LatestConfirmedSwingHigh.Id, version.LatestConfirmedSwingHigh.Id);
        Assert.Throws<ArgumentException>(() => State(trades).Add(otherSession[0]));
    }

    [Fact]
    public void Bounded_eviction_marks_recomputed_structure_partial_without_rewriting_prior_results()
    {
        var trades = CreateTrades(Prices.Take(5).ToArray());
        var state = State(trades, maximumEvents: 5);
        var before = _calculator.Calculate(state, trades[^1].EventTimeUtc, _configuration);
        state.Add(trades[^1] with
        {
            SequenceId = "future",
            EventTimeUtc = trades[^1].EventTimeUtc.AddTicks(1),
            Price = 0m
        });
        var after = _calculator.Calculate(state, trades[^1].EventTimeUtc, _configuration);

        Assert.True(before.HasCompleteSessionData);
        Assert.NotNull(before.LatestConfirmedSwingHigh);
        Assert.False(after.HasCompleteSessionData);
        Assert.Equal(MarketStructureStatus.Insufficient, after.MarketStructureStatus);
        Assert.Null(after.LatestConfirmedSwingHigh);
        Assert.Equal(5m, before.LatestConfirmedSwingHigh.Price);
        Assert.NotEqual(before.Id, after.Id);
    }

    [Fact]
    public void Late_event_creates_a_new_confirmation_identity_without_mutating_the_prior_snapshot()
    {
        var trades = CreateTrades([10m, 11m, 15m, 12m, 11m]);
        var state = State(trades);
        var before = _calculator.Calculate(state, trades[^1].EventTimeUtc, _configuration);
        state.Add(trades[2] with
        {
            SequenceId = "late",
            EventTimeUtc = trades[2].EventTimeUtc.AddMilliseconds(500),
            ReceivedTimeUtc = trades[^1].EventTimeUtc.AddMinutes(1),
            Price = 14m
        });
        var after = _calculator.Calculate(state, trades[^1].EventTimeUtc, _configuration);

        var original = Assert.IsType<ConfirmedSwing>(before.LatestConfirmedSwingHigh);
        var revised = Assert.IsType<ConfirmedSwing>(after.LatestConfirmedSwingHigh);
        Assert.Equal(original.OriginEventIdentity, revised.OriginEventIdentity);
        Assert.Equal(trades[4].EventTimeUtc, original.ConfirmedAtEventTimeUtc);
        Assert.Equal(trades[3].EventTimeUtc, revised.ConfirmedAtEventTimeUtc);
        Assert.NotEqual(original.ConfirmationEventIdentity, revised.ConfirmationEventIdentity);
        Assert.NotEqual(original.Id, revised.Id);
        Assert.NotEqual(before.Id, after.Id);
    }

    [Fact]
    public void Structure_round_trips_in_persisted_and_api_json_without_changing_detector_decisions()
    {
        var trades = CreateTrades(Prices);
        var snapshot = _calculator.Calculate(State(trades), trades[^1].EventTimeUtc, _configuration);
        var persistedOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var persistedJson = JsonSerializer.Serialize(snapshot, persistedOptions);
        using var persisted = JsonDocument.Parse(persistedJson);
        var persistedHigh = persisted.RootElement.GetProperty("latestConfirmedSwingHigh");
        Assert.Equal((int)ConfirmedSwingType.ConfirmedSwingHigh, persistedHigh.GetProperty("type").GetInt32());
        Assert.Equal((int)MarketStructureStatus.Available,
            persisted.RootElement.GetProperty("marketStructureStatus").GetInt32());
        Assert.Equal(snapshot, JsonSerializer.Deserialize<FeatureSnapshot>(persistedJson, persistedOptions));

        var apiOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        apiOptions.Converters.Add(new JsonStringEnumConverter());
        var apiJson = JsonSerializer.Serialize(snapshot, apiOptions);
        using var api = JsonDocument.Parse(apiJson);
        Assert.Equal("ConfirmedSwingHigh",
            api.RootElement.GetProperty("latestConfirmedSwingHigh").GetProperty("type").GetString());
        Assert.Equal("Available", api.RootElement.GetProperty("marketStructureStatus").GetString());
        Assert.Equal(snapshot, JsonSerializer.Deserialize<FeatureSnapshot>(apiJson, apiOptions));

        var legacy = JsonNode.Parse(persistedJson)!.AsObject();
        legacy.Remove("latestConfirmedSwingHigh");
        legacy.Remove("latestConfirmedSwingLow");
        legacy.Remove("marketStructureStatus");
        var legacySnapshot = legacy.Deserialize<FeatureSnapshot>(persistedOptions);
        Assert.NotNull(legacySnapshot);
        Assert.Null(legacySnapshot.LatestConfirmedSwingHigh);
        Assert.Null(legacySnapshot.LatestConfirmedSwingLow);
        Assert.Equal(MarketStructureStatus.NotComputed, legacySnapshot.MarketStructureStatus);

        var detector = new MomentumContinuationDetector();
        var baseline = detector.Evaluate(snapshot, _configuration);
        var changed = detector.Evaluate(snapshot with
        {
            LatestConfirmedSwingHigh = null,
            LatestConfirmedSwingLow = null,
            MarketStructureStatus = MarketStructureStatus.InvalidPriceData
        }, _configuration);
        Assert.Equal(JsonSerializer.Serialize(baseline), JsonSerializer.Serialize(changed));
    }

    private static TradeEvent[] CreateTrades(
        IReadOnlyList<decimal> prices,
        string symbol = Symbol,
        string sessionId = SessionId) =>
        prices.Select((price, index) => new TradeEvent(
            symbol, sessionId, Start.AddSeconds(index), Start.AddSeconds(index + 10),
            "Fixture", $"swing-{index:D3}", price, 10)).ToArray();

    private static SymbolEventState State(
        IEnumerable<TradeEvent> trades,
        string symbol = Symbol,
        string sessionId = SessionId,
        int maximumEvents = 100)
    {
        var state = new SymbolEventState(symbol, sessionId, maximumEvents);
        foreach (var trade in trades)
        {
            state.Add(trade);
        }

        return state;
    }

    private static Guid ExpectedId(ConfirmedSwing swing)
    {
        var identity = string.Join(
            "\u001f",
            "confirmed-swing",
            swing.Symbol,
            swing.SessionId,
            swing.Type.ToString(),
            swing.Price.ToString("G29", CultureInfo.InvariantCulture),
            swing.EventTimeUtc.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture),
            swing.ConfirmedAtEventTimeUtc.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture),
            swing.OriginEventIdentity,
            swing.ConfirmationEventIdentity,
            swing.FeatureSetVersion,
            swing.AlgorithmVersion);
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, 16));
    }
}
