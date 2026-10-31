using System.Runtime.CompilerServices;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MarketOpportunity.Analytics;
using MarketOpportunity.Application;
using MarketOpportunity.Domain;
using MarketOpportunity.Infrastructure;

namespace MarketOpportunity.UnitTests;

public sealed class LiveValidationDiagnosticsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 14, 30, 0, TimeSpan.Zero);
    private const string QuoteJson =
        """{"T":"q","S":"AAPL","bp":100,"bs":2,"ap":0,"as":0,"bx":"V","ax":"V","c":["R","Y"],"z":"C","t":"2026-10-28T14:29:59.123456789Z"}""";

    [Fact]
    public async Task Complete_message_stamp_precedes_json_parsing_and_per_item_backpressure()
    {
        var clock = new ManualClock();
        using var diagnostics = new LiveValidationDiagnostics(clock);
        using var socket = new FragmentedSocket(clock, "[" + QuoteJson + "," + QuoteJson + "]");
        var message = await AlpacaMarketDataSource.ReceiveMessageAsync(
            socket, clock, CancellationToken.None, diagnostics);
        Assert.Equal(Now.AddMilliseconds(10), message.ReceivedUtc);
        Assert.Equal(1, diagnostics.Snapshot().MessagesReceived);
        clock.Advance(TimeSpan.FromMilliseconds(20));
        using var document = JsonDocument.Parse(message.Payload);
        var items = AlpacaMarketDataParser.GetItems(document.RootElement);
        var first = AlpacaMarketDataParser.ParseEvent(items[0], "s", clock.GetUtcNow());
        diagnostics.Produced(first, Guid.Empty, message.ReceivedUtc,
            AlpacaMarketDataParser.ReadDiagnosticMetadata(items[0]), message.ReceivedTimestamp);
        clock.Advance(TimeSpan.FromSeconds(2));
        var second = AlpacaMarketDataParser.ParseEvent(items[1], "s", clock.GetUtcNow());
        diagnostics.Produced(second, Guid.Empty, message.ReceivedUtc,
            AlpacaMarketDataParser.ReadDiagnosticMetadata(items[1]), message.ReceivedTimestamp);
        Assert.Equal(TimeSpan.FromSeconds(2), second.ReceivedTimeUtc - first.ReceivedTimeUtc);
        Assert.Equal(first.EventTimeUtc, second.EventTimeUtc);
        Assert.True(diagnostics.TryGetTiming(second, out var timing));
        Assert.Equal(message.ReceivedUtc, timing!.MessageReceivedTimeUtc);
        Assert.Equal(2020, diagnostics.Snapshot().Timings["message_to_creation"].MaximumMs);
    }

    [Fact]
    public void Trade_conditions_are_retained_without_changing_provider_trade_identity()
    {
        using var document = JsonDocument.Parse(
            """{"T":"t","S":"AAPL","i":42,"p":100,"s":10,"c":["@","I"],"z":"C","t":"2026-10-28T14:30:00Z"}""");
        var trade = AlpacaMarketDataParser.ParseEvent(document.RootElement, "s", Now);
        using var diagnostics = new LiveValidationDiagnostics(new ManualClock());
        var identity = MarketEventIdentity.Create(trade);
        diagnostics.Produced(trade, Guid.Empty, Now,
            AlpacaMarketDataParser.ReadDiagnosticMetadata(document.RootElement));
        Assert.Equal(identity, MarketEventIdentity.Create(trade));
        var metadata = Assert.Single(diagnostics.Snapshot().ProviderMetadata);
        Assert.Equal("Trade", metadata.EventType);
        Assert.Equal(new[] { "@", "I" }, metadata.Metadata.Conditions);
        Assert.Equal("C", metadata.Metadata.Tape);
    }

    [Fact]
    public void Diagnostic_sidecar_preserves_canonical_identity_serialization_and_quote_veto()
    {
        using var document = JsonDocument.Parse(QuoteJson);
        var quote = AlpacaMarketDataParser.ParseEvent(document.RootElement, "diagnostic-session", Now);
        var identity = MarketEventIdentity.Create(quote);
        var json = JsonSerializer.Serialize(quote, quote.GetType());
        var state = new SymbolEventState("AAPL", "diagnostic-session", 100);
        state.Add(new TradeEvent("AAPL", "diagnostic-session", Now.AddSeconds(-2), Now,
            "Alpaca", "1", 100m, 10));
        state.Add(quote);
        var configuration = new ScannerConfiguration();
        var calculator = new FeatureCalculator();
        var before = calculator.Calculate(state, Now, configuration);
        using var diagnostics = new LiveValidationDiagnostics(new ManualClock());
        var connection = diagnostics.Connecting();
        diagnostics.Produced(quote, connection, Now.AddMilliseconds(-20),
            AlpacaMarketDataParser.ReadDiagnosticMetadata(document.RootElement));

        Assert.Equal(identity, MarketEventIdentity.Create(quote));
        Assert.Equal(json, JsonSerializer.Serialize(quote, quote.GetType()));
        Assert.Equal(before, calculator.Calculate(state, Now, configuration));
        Assert.False(before.IsQuoteValid);
        Assert.Contains("InvalidQuoteOrPrice",
            new MomentumContinuationDetector().Evaluate(before, configuration).ActiveVetoes);
        Assert.Equal(Now, quote.ReceivedTimeUtc);
        Assert.Equal(1_234_567, quote.EventTimeUtc.Ticks % TimeSpan.TicksPerSecond);
        Assert.True(diagnostics.TryGetTiming(quote, out var timing));
        Assert.NotNull(timing);
        Assert.Equal(Now.AddMilliseconds(-20), timing.MessageReceivedTimeUtc);
        Assert.Equal(Now, timing.ParsedTimeUtc);
        Assert.Equal("V", timing.Metadata.BidExchange);
        Assert.Equal("V", timing.Metadata.AskExchange);
        Assert.Equal(new[] { "R", "Y" }, timing.Metadata.Conditions);
        Assert.Equal("C", timing.Metadata.Tape);
        Assert.False(timing.Metadata.Incomplete);
        Assert.True(timing.Metadata.ConditionsSupplied);
        var group = Assert.Single(diagnostics.Snapshot().ProviderMetadata);
        Assert.Equal(identity, group.FirstZeroAsk!.EventIdentity);
        Assert.Equal(1, group.ZeroAskQuotes);
    }

    [Fact]
    public void Metadata_is_bounded_and_malformed_optional_metadata_does_not_change_market_values()
    {
        using var document = JsonDocument.Parse(
            """{"T":"q","S":"AAPL","bp":100,"bs":2,"ap":0,"as":0,"bx":3,"ax":"V","c":["R",2],"t":"2026-10-28T14:30:00Z"}""");
        var quote = Assert.IsType<QuoteEvent>(
            AlpacaMarketDataParser.ParseEvent(document.RootElement, "s", Now));
        var metadata = AlpacaMarketDataParser.ReadDiagnosticMetadata(document.RootElement);
        Assert.True(metadata.Incomplete);
        Assert.Null(metadata.BidExchange);
        Assert.Null(metadata.Tape);
        Assert.Equal(new[] { "R" }, metadata.Conditions);
        Assert.Equal(0m, quote.AskPrice);
        Assert.Equal(0L, quote.AskSize);

        using var diagnostics = new LiveValidationDiagnostics(new ManualClock());
        for (var index = 0; index < 260; index++)
        {
            diagnostics.Produced(quote with { SequenceId = index.ToString() },
                Guid.NewGuid(), Now, metadata);
        }
        var snapshot = diagnostics.Snapshot();
        Assert.Equal(256, snapshot.ProviderMetadata.Count);
        Assert.Equal(4, snapshot.UnattributedMetadataEvents);
        Assert.Equal(260, snapshot.CanonicalEventsProduced);
        Assert.Equal(260, snapshot.ZeroAskQuotes);
    }

    [Fact]
    public void Signed_provider_lag_and_monotonic_pipeline_times_have_bounded_percentiles()
    {
        var clock = new ManualClock();
        using var diagnostics = new LiveValidationDiagnostics(clock);
        using var document = JsonDocument.Parse(QuoteJson);
        var quote = AlpacaMarketDataParser.ParseEvent(document.RootElement, "s", Now);
        diagnostics.Produced(quote, Guid.NewGuid(), quote.EventTimeUtc.AddMilliseconds(-500),
            AlpacaMarketDataParser.ReadDiagnosticMetadata(document.RootElement));
        clock.Advance(TimeSpan.FromMilliseconds(20));
        var analysis = diagnostics.AnalysisStarted([quote]);
        clock.Advance(TimeSpan.FromMilliseconds(10));
        var persistence = clock.GetTimestamp();
        clock.Advance(TimeSpan.FromMilliseconds(40));
        diagnostics.PersistenceCompleted(analysis, persistence, 1);
        var snapshot = diagnostics.Snapshot();
        Assert.Equal(-500, snapshot.Timings["provider_to_message"].MinimumMs);
        Assert.Equal(20, snapshot.Timings["creation_to_analysis"].MaximumMs);
        Assert.Equal(50, snapshot.Timings["analysis_to_persistence"].MaximumMs);
        Assert.Equal(40, snapshot.Timings["persistence_batch"].MaximumMs);
        Assert.InRange(snapshot.Timings["persistence_batch"].P95UpperBoundMs!.Value, 40, 50);
        Assert.Equal(1, snapshot.PersistenceAcknowledgedEvents);
        Assert.Null(snapshot.Timings["channel_write_wait"].MaximumMs);
    }

    [Fact]
    public void Timing_summaries_report_exact_counts_above_latency_thresholds()
    {
        var clock = new ManualClock();
        using var diagnostics = new LiveValidationDiagnostics(clock);
        foreach (var milliseconds in new[] { 500, 1000, 1001, 5001, 10001, 15000, 15001 })
        {
            var start = clock.GetTimestamp();
            clock.Advance(TimeSpan.FromMilliseconds(milliseconds));
            diagnostics.PersistenceCompleted(start, start, 1);
        }
        var summary = diagnostics.Snapshot().Timings["persistence_batch"];
        Assert.Equal(7, summary.Count);
        Assert.Equal(500, summary.MinimumMs);
        Assert.Equal(15001, summary.MaximumMs);
        Assert.NotNull(summary.MedianUpperBoundMs);
        Assert.NotNull(summary.P95UpperBoundMs);
        Assert.Equal(5, summary.CountAbove1000Ms);
        Assert.Equal(4, summary.CountAbove5000Ms);
        Assert.Equal(3, summary.CountAbove10000Ms);
        Assert.Equal(1, summary.CountAbove15000Ms);
        Assert.Equal(0, diagnostics.Snapshot().Timings["channel_write_wait"].CountAbove1000Ms);
    }

    [Fact]
    public void Shutdown_trigger_is_unknown_until_recorded_and_keeps_the_first_requested_cause()
    {
        var clock = new ManualClock();
        using var diagnostics = new LiveValidationDiagnostics(clock);
        diagnostics.ConfigureRun(600, "C:\\evidence\\s.app.log");
        var initial = diagnostics.Snapshot();
        Assert.Equal("Unknown", initial.ShutdownTrigger);
        Assert.Null(initial.ShutdownRequestedUtc);
        Assert.Equal(600, initial.ConfiguredDurationSeconds);
        Assert.Equal("C:\\evidence\\s.app.log", initial.ApplicationLogPath);

        clock.Advance(TimeSpan.FromSeconds(600));
        var requested = clock.GetUtcNow();
        diagnostics.RecordShutdownTrigger(ShutdownTrigger.DurationElapsed);
        clock.Advance(TimeSpan.FromSeconds(1));
        diagnostics.ShutdownRequested();
        diagnostics.RecordShutdownTrigger(ShutdownTrigger.OperatorCancellation);
        var snapshot = diagnostics.Snapshot();
        Assert.Equal("DurationElapsed", snapshot.ShutdownTrigger);
        Assert.Equal(requested, snapshot.ShutdownRequestedUtc);
        Assert.Equal(new[] { "DurationElapsed", "OperatorCancellation" },
            snapshot.ShutdownTriggerEvents.Select(item => item.Trigger));
    }

    [Fact]
    public void Abnormal_shutdown_outcomes_supersede_a_requested_trigger_and_remain_in_history()
    {
        using var diagnostics = new LiveValidationDiagnostics(new ManualClock());
        diagnostics.RecordShutdownTrigger(ShutdownTrigger.OperatorCancellation);
        diagnostics.ShutdownDrainTimedOut();
        diagnostics.RecordShutdownTrigger(ShutdownTrigger.ApplicationFailure);
        var snapshot = diagnostics.Snapshot();
        Assert.Equal("GracefulDrainTimeout", snapshot.ShutdownTrigger);
        Assert.Equal(new[] { "OperatorCancellation", "GracefulDrainTimeout", "ApplicationFailure" },
            snapshot.ShutdownTriggerEvents.Select(item => item.Trigger));

        using var failed = new LiveValidationDiagnostics(new ManualClock());
        failed.ShutdownRequested();
        failed.ShutdownDrainFailed("reconciliation failed");
        Assert.Equal("ApplicationFailure", failed.Snapshot().ShutdownTrigger);

        using var unknown = new LiveValidationDiagnostics(new ManualClock());
        unknown.ShutdownRequested();
        unknown.RecordShutdownTrigger(ShutdownTrigger.Unknown);
        Assert.Equal("Unknown", unknown.Snapshot().ShutdownTrigger);
        Assert.NotNull(unknown.Snapshot().ShutdownRequestedUtc);
        Assert.Empty(unknown.Snapshot().ShutdownTriggerEvents);
    }

    [Fact]
    public void Connection_attempts_successful_sockets_and_subscription_acknowledgements_are_distinct()
    {
        using var diagnostics = new LiveValidationDiagnostics(new ManualClock());
        var first = diagnostics.Connecting();
        diagnostics.Connected(first);
        diagnostics.Transition(first, "Authenticated");
        diagnostics.Subscription(["AAPL"], []);
        diagnostics.Transition(first, "Subscribed");
        diagnostics.Transition(first, "Disconnected");
        diagnostics.ReconnectAttempt();
        var second = diagnostics.Connecting();
        diagnostics.Transition(second, "ReconnectAttempt");
        Assert.NotEqual(first, second);
        var snapshot = diagnostics.Snapshot();
        Assert.Equal(2, snapshot.ConnectionAttempts);
        Assert.Equal(1, snapshot.ConnectionsOpened);
        Assert.Equal(1, snapshot.ReconnectAttempts);
        Assert.Equal(new[] { "AAPL" }, snapshot.ConfirmedTradeSymbols);
        Assert.Empty(snapshot.ConfirmedQuoteSymbols);
        Assert.All(snapshot.Transitions.Take(5), transition => Assert.Equal(first, transition.ConnectionId));
        Assert.Equal(second, snapshot.Transitions[^1].ConnectionId);
    }

    [Fact]
    public void Lifecycle_and_anomaly_logs_are_bounded_and_failures_are_counted()
    {
        var messages = new List<string>();
        using var diagnostics = new LiveValidationDiagnostics(new ManualClock(), messages.Add);
        for (var index = 0; index < 260; index++)
        {
            diagnostics.Transition(Guid.NewGuid(), "Live");
        }
        diagnostics.MessageReceived();
        diagnostics.MarketDataMessage();
        diagnostics.ParserFailed(Guid.NewGuid(), new InvalidDataException("secret-not-to-log"));
        diagnostics.ReconnectAttempt();
        var snapshot = diagnostics.Snapshot();
        Assert.Equal(256, snapshot.Transitions.Count);
        Assert.Equal(5, snapshot.DroppedTransitions);
        Assert.Equal(1, snapshot.MessagesReceived);
        Assert.Equal(1, snapshot.MarketDataMessages);
        Assert.Equal(1, snapshot.ParserFailures);
        Assert.Equal(1, snapshot.ReconnectAttempts);
        Assert.DoesNotContain(messages, message => message.Contains("secret-not-to-log"));
    }

    [Fact]
    public async Task Instrumented_live_pipeline_preserves_decisions_and_counts_duplicate_and_persisted_inputs()
    {
        MarketEvent[] events =
        [
            new TradeEvent("AAPL", "s", Now.AddSeconds(-2), Now, "Alpaca", "1", 100m, 10),
            new QuoteEvent("AAPL", "s", Now, Now, "Alpaca", null, 100m, 100.01m, 1, 1)
        ];
        var baseline = new RecordingStore();
        await Pipeline(new Source(events), baseline).RunAsync(CancellationToken.None);
        using var diagnostics = new LiveValidationDiagnostics(TimeProvider.System);
        var recorded = new RecordingStore();
        await Pipeline(new Source([.. events, events[0]], diagnostics), recorded, diagnostics)
            .RunAsync(CancellationToken.None);
        Assert.Equal(baseline.Feature, recorded.Feature);
        Assert.Equal(baseline.Opportunity!.Id, recorded.Opportunity!.Id);
        Assert.Equal(baseline.Opportunity.ActiveVetoes, recorded.Opportunity.ActiveVetoes);
        var snapshot = diagnostics.Snapshot();
        Assert.Equal(3, snapshot.AcceptedEvents);
        Assert.Equal(3, snapshot.DequeuedEvents);
        Assert.Equal(1, snapshot.DuplicateEvents);
        Assert.Equal(2, snapshot.EventsAnalyzed);
        Assert.Equal(2, snapshot.PersistenceAcknowledgedEvents);
        Assert.Equal(0, snapshot.ChannelDepth);
        Assert.Equal(2, snapshot.Timings["creation_to_analysis"].Count);
    }

    [Fact]
    public async Task Shutdown_while_work_is_pending_in_the_analysis_interval_persists_both_events()
    {
        var clock = new ManualClock();
        using var cancellation = new CancellationTokenSource();
        using var diagnostics = new LiveValidationDiagnostics(clock);
        var store = new RecordingStore();
        var source = new GatedSource();
        var run = Pipeline(source, store, diagnostics, clock: clock).RunAsync(cancellation.Token);
        await store.FirstPersistenceCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        source.ReleaseSecond.TrySetResult();
        await source.SecondAccepted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Until(() => diagnostics.Snapshot().DequeuedEvents == 2);
        await cancellation.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        var snapshot = diagnostics.Snapshot();
        Assert.Equal(2, snapshot.AcceptedEvents);
        Assert.Equal(2, snapshot.DequeuedEvents);
        Assert.Equal(2, snapshot.EventsAnalyzed);
        Assert.Equal(2, snapshot.PersistenceAcknowledgedEvents);
        Assert.Equal(0, snapshot.RemainingEvents);
        Assert.Equal("Completed", snapshot.ShutdownDrainStatus);
        Assert.Equal(new[] { "1", "2" },
            store.PersistedEvents.OfType<TradeEvent>().Select(marketEvent => marketEvent.SequenceId));
    }

    [Fact]
    public async Task Shutdown_drains_events_still_queued_in_the_channel()
    {
        using var cancellation = new CancellationTokenSource();
        using var diagnostics = new LiveValidationDiagnostics(TimeProvider.System);
        var store = new GatedStore();
        var source = new GatedSource();
        var run = Pipeline(source, store, diagnostics).RunAsync(cancellation.Token);
        await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        source.ReleaseSecond.TrySetResult();
        await source.SecondAccepted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, diagnostics.Snapshot().AcceptedEvents);
        Assert.Equal(1, diagnostics.Snapshot().ChannelDepth);
        await cancellation.CancelAsync();
        store.Release.TrySetResult();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, diagnostics.Snapshot().PersistenceAcknowledgedEvents);
        Assert.Equal("Completed", diagnostics.Snapshot().ShutdownDrainStatus);
    }

    [Fact]
    public async Task Shutdown_waits_for_in_flight_persistence_within_the_budget()
    {
        using var cancellation = new CancellationTokenSource();
        using var diagnostics = new LiveValidationDiagnostics(TimeProvider.System);
        var store = new GatedStore();
        var run = Pipeline(new PausingSource([CreateTrade("one")]), store, diagnostics)
            .RunAsync(cancellation.Token);
        await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await cancellation.CancelAsync();
        Assert.False(run.IsCompleted);
        store.Release.TrySetResult();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, diagnostics.Snapshot().PersistenceAcknowledgedEvents);
        Assert.Equal("Completed", diagnostics.Snapshot().ShutdownDrainStatus);
    }

    [Fact]
    public async Task Persistence_failure_during_drain_is_surfaced_without_successful_drain()
    {
        using var cancellation = new CancellationTokenSource();
        using var diagnostics = new LiveValidationDiagnostics(TimeProvider.System);
        var store = new GatedStore(failOnRelease: true);
        var run = Pipeline(new PausingSource([CreateTrade("one")]), store, diagnostics)
            .RunAsync(cancellation.Token);
        await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();
        store.Release.TrySetResult();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => run);
        Assert.IsType<IOException>(exception.InnerException);
        Assert.Contains("Remaining=1", exception.Message);
        var snapshot = diagnostics.Snapshot();
        Assert.Equal(1, snapshot.PersistenceFailures);
        Assert.Equal(1, snapshot.PersistenceFailedEvents);
        Assert.Equal(1, snapshot.RemainingEvents);
        Assert.Equal("Failed", snapshot.ShutdownDrainStatus);
        Assert.NotNull(snapshot.ShutdownDrainFailure);
    }

    [Fact]
    public async Task Hung_persistence_times_out_and_reports_remaining_work()
    {
        using var cancellation = new CancellationTokenSource();
        using var diagnostics = new LiveValidationDiagnostics(TimeProvider.System);
        var store = new GatedStore();
        var run = Pipeline(new PausingSource([CreateTrade("one")]), store, diagnostics,
            livePipelineOptions: new LivePipelineOptions { GracefulShutdownTimeoutSeconds = 1 })
            .RunAsync(cancellation.Token);
        await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();

        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Contains("Remaining=1", exception.Message);
        var snapshot = diagnostics.Snapshot();
        Assert.Equal("TimedOut", snapshot.ShutdownDrainStatus);
        Assert.Equal(1, snapshot.RemainingEvents);
        Assert.Equal(0, snapshot.PersistenceAcknowledgedEvents);
        store.Release.TrySetResult();
    }

    [Fact]
    public async Task Shutdown_cancels_a_blocked_full_channel_write_and_drains_accepted_items()
    {
        using var cancellation = new CancellationTokenSource();
        using var diagnostics = new LiveValidationDiagnostics(TimeProvider.System);
        var store = new GatedStore();
        var source = new GatedFloodSource();
        var run = Pipeline(source, store, diagnostics).RunAsync(cancellation.Token);
        await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        source.ReleaseFlood.TrySetResult();
        await Until(() => diagnostics.Snapshot().AcceptedEvents == 5001);
        await Until(() => source.Attempted == 5002);
        await cancellation.CancelAsync();
        store.Release.TrySetResult();
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        var snapshot = diagnostics.Snapshot();
        Assert.Equal(5000, snapshot.MaximumSampledChannelDepth);
        Assert.Equal(0, snapshot.ChannelDepth);
        Assert.Equal(1, snapshot.ChannelBackpressureWaits);
        Assert.Equal(1, snapshot.CancelledWrites);
        Assert.Equal(5001, snapshot.PersistenceAcknowledgedEvents);
        Assert.Equal(0, snapshot.RemainingEvents);
        Assert.Equal("Completed", snapshot.ShutdownDrainStatus);
        Assert.True(snapshot.LongestChannelWaitMs >= 0);
    }

    [Fact]
    public async Task Shutdown_accounts_for_duplicate_input_without_calling_it_lost_work()
    {
        using var cancellation = new CancellationTokenSource();
        using var diagnostics = new LiveValidationDiagnostics(TimeProvider.System);
        var marketEvent = CreateTrade("duplicate");
        var source = new GatedDuplicateSource(marketEvent);
        var store = new RecordingStore();
        var run = Pipeline(source, store, diagnostics).RunAsync(cancellation.Token);
        await source.DuplicateAccepted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Until(() => diagnostics.Snapshot().DuplicateEvents == 1);
        await cancellation.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        var snapshot = diagnostics.Snapshot();
        Assert.Equal(2, snapshot.AcceptedEvents);
        Assert.Equal(2, snapshot.DequeuedEvents);
        Assert.Equal(1, snapshot.DuplicateEvents);
        Assert.Equal(1, snapshot.PersistenceAcknowledgedEvents);
        Assert.Equal(0, snapshot.RemainingEvents);
        Assert.Equal("Completed", snapshot.ShutdownDrainStatus);
        Assert.Single(store.PersistedEvents);
    }

    [Fact]
    public async Task Cancellation_before_any_events_completes_cleanly()
    {
        using var cancellation = new CancellationTokenSource();
        using var diagnostics = new LiveValidationDiagnostics(TimeProvider.System);
        var source = new EmptyWaitingSource();
        var run = Pipeline(source, new RecordingStore(), diagnostics).RunAsync(cancellation.Token);
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        var snapshot = diagnostics.Snapshot();
        Assert.Equal(0, snapshot.AcceptedEvents);
        Assert.Equal(0, snapshot.RemainingEvents);
        Assert.Equal("Completed", snapshot.ShutdownDrainStatus);
    }

    [Fact]
    public void Drain_does_not_complete_when_accepted_work_was_not_analyzed()
    {
        using var diagnostics = new LiveValidationDiagnostics(TimeProvider.System);
        diagnostics.Accepted(0);
        diagnostics.Dequeued(0, duplicate: false);
        diagnostics.PersistenceCompleted(0, 0, events: 1);
        diagnostics.ShutdownDrainStarted();

        Assert.False(diagnostics.ShutdownDrainCompleted());
        var snapshot = diagnostics.Snapshot();
        Assert.Equal("Failed", snapshot.ShutdownDrainStatus);
        Assert.Contains("analysis", snapshot.ShutdownDrainFailure, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }

    private static ScannerPipeline Pipeline(
        IMarketDataSource source,
        IScannerStore store,
        LiveValidationDiagnostics? diagnostics = null,
        TimeProvider? clock = null,
        LivePipelineOptions? livePipelineOptions = null) =>
        new(source, store, new ScannerState(), new ScannerConfiguration(),
            new FeatureCalculator(), new MomentumContinuationDetector(), clock ?? TimeProvider.System,
            diagnostics: diagnostics, livePipelineOptions: livePipelineOptions);

    private static TradeEvent CreateTrade(string id) =>
        new("AAPL", "s", Now.AddSeconds(1), Now, "Alpaca", id, 100m, 10);

    private sealed class Source(MarketEvent[] events, LiveValidationDiagnostics? diagnostics = null)
        : ILiveMarketDataSource
    {
        public async IAsyncEnumerable<MarketEvent> ReadEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var marketEvent in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (diagnostics is not null && !diagnostics.TryGetTiming(marketEvent, out _))
                {
                    diagnostics.Produced(marketEvent, Guid.Empty, Now, new(null, null, [], null));
                }
                yield return marketEvent;
            }
            await Task.CompletedTask;
        }
    }

    private sealed class RecordingStore : IScannerStore
    {
        public FeatureSnapshot? Feature { get; private set; }
        public Opportunity? Opportunity { get; private set; }
        public List<MarketEvent> PersistedEvents { get; } = [];
        public TaskCompletionSource FirstPersistenceCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task PersistRunAsync(ScannerConfiguration configuration,
            IReadOnlyCollection<MarketEvent> marketEvents, FeatureSnapshot featureSnapshot,
            Opportunity opportunity, TradePlan tradePlan, CancellationToken cancellationToken)
        {
            Feature = featureSnapshot;
            Opportunity = opportunity;
            PersistedEvents.AddRange(marketEvents);
            FirstPersistenceCompleted.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class GatedStore(bool failOnRelease = false) : IScannerStore
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task PersistRunAsync(ScannerConfiguration configuration,
            IReadOnlyCollection<MarketEvent> marketEvents, FeatureSnapshot featureSnapshot,
            Opportunity opportunity, TradePlan tradePlan, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Release.Task;
            if (failOnRelease)
            {
                throw new IOException("Controlled persistence failure.");
            }
        }
    }

    private sealed class GatedSource : ILiveMarketDataSource
    {
        public TaskCompletionSource ReleaseSecond { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondAccepted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async IAsyncEnumerable<MarketEvent> ReadEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new TradeEvent("AAPL", "s", Now, Now, "Alpaca", "1", 100m, 10);
            await ReleaseSecond.Task.WaitAsync(cancellationToken);
            yield return new TradeEvent("AAPL", "s", Now.AddSeconds(1), Now, "Alpaca", "2", 101m, 10);
            SecondAccepted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class GatedDuplicateSource(MarketEvent marketEvent) : ILiveMarketDataSource
    {
        public TaskCompletionSource DuplicateAccepted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<MarketEvent> ReadEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return marketEvent;
            yield return marketEvent;
            DuplicateAccepted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class PausingSource(IReadOnlyCollection<MarketEvent> events) : ILiveMarketDataSource
    {
        public async IAsyncEnumerable<MarketEvent> ReadEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var marketEvent in events)
            {
                yield return marketEvent;
            }
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class EmptyWaitingSource : ILiveMarketDataSource
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<MarketEvent> ReadEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }
    }

    private sealed class GatedFloodSource : ILiveMarketDataSource
    {
        private int _attempted;
        public int Attempted => Volatile.Read(ref _attempted);
        public TaskCompletionSource ReleaseFlood { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async IAsyncEnumerable<MarketEvent> ReadEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attempted);
            yield return new TradeEvent("AAPL", "s", Now, Now, "Alpaca", "0", 100m, 10);
            await ReleaseFlood.Task.WaitAsync(cancellationToken);
            for (var index = 1; index < 5002; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref _attempted);
                yield return new TradeEvent("AAPL", "s", Now.AddTicks(index), Now,
                    "Alpaca", index.ToString(), 100m, 10);
            }
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => Now.AddTicks(_ticks);
        public void Advance(TimeSpan duration) => _ticks += duration.Ticks;
    }

    private sealed class FragmentedSocket(ManualClock clock, string payload) : WebSocket
    {
        private readonly byte[] _bytes = Encoding.UTF8.GetBytes(payload);
        private int _offset;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override void Dispose() { }
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription,
            CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription,
            CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType,
            bool endOfMessage, CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            var count = Math.Min(17, _bytes.Length - _offset);
            _bytes.AsSpan(_offset, count).CopyTo(buffer.AsSpan());
            _offset += count;
            var complete = _offset == _bytes.Length;
            if (complete) { clock.Advance(TimeSpan.FromMilliseconds(10)); }
            return Task.FromResult(new WebSocketReceiveResult(count, WebSocketMessageType.Text, complete));
        }
    }
}
