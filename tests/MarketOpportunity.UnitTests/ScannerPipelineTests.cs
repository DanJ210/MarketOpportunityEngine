using MarketOpportunity.Analytics;
using MarketOpportunity.Application;
using MarketOpportunity.Domain;

namespace MarketOpportunity.UnitTests;

public sealed class ScannerPipelineTests
{
    [Fact]
    public async Task Produces_an_isolated_persisted_analysis_for_each_symbol_and_session()
    {
        MarketEvent[] events =
        [
            .. CreateSession("MOE", "session-one", new DateTimeOffset(2026, 10, 22, 14, 30, 0, TimeSpan.Zero), 100m),
            .. CreateSession("QNT", "session-one", new DateTimeOffset(2026, 10, 22, 14, 30, 0, TimeSpan.Zero), 200m),
            .. CreateSession("MOE", "session-two", new DateTimeOffset(2026, 10, 23, 14, 30, 0, TimeSpan.Zero), 300m),
            .. CreateSession("QNT", "session-two", new DateTimeOffset(2026, 10, 23, 14, 30, 0, TimeSpan.Zero), 400m)
        ];
        var store = new RecordingStore();
        var scannerState = new ScannerState();
        var pipeline = new ScannerPipeline(
            new EventSource(events),
            store,
            scannerState,
            new ScannerConfiguration(),
            new FeatureCalculator(),
            new MomentumContinuationDetector(),
            TimeProvider.System);

        await pipeline.RunFixtureAsync(CancellationToken.None);

        var result = scannerState.Read();
        Assert.Equal(4, result.Results.Count);
        Assert.Equal(4, store.Runs.Count);
        Assert.Equal(events.Length, result.Status.EventsProcessed);
        Assert.Equal(MarketDataHealthStatus.Recorded, result.Status.FeedStatus);
        Assert.Null(result.Status.LatestReceivedTimeUtc);
        Assert.Null(result.Status.ReceiveAge);
        Assert.Equal(4, result.Results.Select(item => (item.Features.Symbol, item.Features.SessionId)).Distinct().Count());
        Assert.Collection(
            result.Results.OrderBy(item => item.Features.SessionId).ThenBy(item => item.Features.Symbol),
            item => AssertAnalysis(item, "MOE", "session-one", 101m),
            item => AssertAnalysis(item, "QNT", "session-one", 201m),
            item => AssertAnalysis(item, "MOE", "session-two", 301m),
            item => AssertAnalysis(item, "QNT", "session-two", 401m));
        Assert.All(store.Runs, run =>
        {
            Assert.All(run.Events, marketEvent =>
            {
                Assert.Equal(run.Features.Symbol, marketEvent.Symbol);
                Assert.Equal(run.Features.SessionId, marketEvent.SessionId);
            });
            Assert.Equal(run.Features.SessionId, run.Opportunity.SessionId);
            Assert.Equal(run.Opportunity.Id, run.TradePlan.OpportunityId);
            Assert.Equal(run.Features.Symbol, run.TradePlan.Symbol);
            Assert.Equal(run.Features.SessionId, run.TradePlan.SessionId);
        });
    }

    [Fact]
    public async Task Fixture_replay_remains_deterministic_across_repeated_runs()
    {
        var events = CreateSession(
            "MOE",
            "fixture-replay",
            new DateTimeOffset(2026, 10, 22, 14, 30, 0, TimeSpan.Zero),
            100m);
        var firstStore = new RecordingStore();
        var secondStore = new RecordingStore();
        var first = CreatePipeline(events, firstStore);
        var second = CreatePipeline(events, secondStore);

        await first.RunFixtureAsync(CancellationToken.None);
        await second.RunFixtureAsync(CancellationToken.None);

        var firstRun = Assert.Single(firstStore.Runs);
        var secondRun = Assert.Single(secondStore.Runs);
        Assert.Equal(firstRun.Features, secondRun.Features);
        Assert.Equal(firstRun.Opportunity.Id, secondRun.Opportunity.Id);
        Assert.Equal(firstRun.TradePlan.Id, secondRun.TradePlan.Id);
        Assert.Equal(
            firstRun.Events.Select(MarketEventIdentity.Create),
            secondRun.Events.Select(MarketEventIdentity.Create));
    }

    [Theory]
    [InlineData("", "Provider")]
    [InlineData("   ", "Provider")]
    [InlineData("", "SessionId")]
    [InlineData("   ", "SessionId")]
    public async Task Rejects_events_without_explicit_source_or_session_identity(
        string invalidValue,
        string propertyName)
    {
        var marketEvent = new TradeEvent(
            "MOE",
            propertyName == "SessionId" ? invalidValue : "session-one",
            new DateTimeOffset(2026, 10, 22, 14, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 22, 14, 30, 0, TimeSpan.Zero),
            propertyName == "Provider" ? invalidValue : "Fixture",
            "trade-1",
            100m,
            10);
        var store = new RecordingStore();
        var pipeline = new ScannerPipeline(
            new EventSource([marketEvent]),
            store,
            new ScannerState(),
            new ScannerConfiguration(),
            new FeatureCalculator(),
            new MomentumContinuationDetector(),
            TimeProvider.System);

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => pipeline.RunFixtureAsync(CancellationToken.None));

        Assert.Contains(propertyName, exception.ParamName);
        Assert.Empty(store.Runs);
    }

    [Fact]
    public async Task Live_source_batches_events_and_persists_analysis_before_stream_ends()
    {
        var events = CreateSession(
            "MOE",
            "manual-session",
            new DateTimeOffset(2026, 10, 23, 14, 30, 0, TimeSpan.Zero),
            100m);
        var store = new RecordingStore();
        var scannerState = new ScannerState();
        var pipeline = new ScannerPipeline(
            new EndingLiveSource(events),
            store,
            scannerState,
            new ScannerConfiguration(),
            new FeatureCalculator(),
            new MomentumContinuationDetector(),
            TimeProvider.System);

        await Assert.ThrowsAsync<IOException>(() => pipeline.RunAsync(CancellationToken.None));

        Assert.Single(store.Runs);
        Assert.Equal(events.Length, store.Runs[0].Events.Count);
        Assert.Equal("manual-session", store.Runs[0].Features.SessionId);
        Assert.Single(scannerState.Read().Results);
    }

    [Fact]
    public async Task Disconnected_live_source_suspends_an_otherwise_active_opportunity()
    {
        var now = new DateTimeOffset(2026, 10, 24, 14, 31, 0, TimeSpan.Zero);
        var events = CreateSession("MOE", "manual-session", now.AddSeconds(-42), 100m);
        var timeProvider = new FixedTimeProvider(now);
        var scannerState = new ScannerState(timeProvider: timeProvider);
        var pipeline = new ScannerPipeline(
            new CompletingLiveSource(events),
            new RecordingStore(),
            scannerState,
            new ScannerConfiguration(),
            new FeatureCalculator(),
            new MomentumContinuationDetector(),
            timeProvider);

        await pipeline.RunAsync(CancellationToken.None);

        var result = Assert.Single(scannerState.Read().Results);
        Assert.Equal(MarketDataHealthStatus.Offline, scannerState.Read().Status.FeedStatus);
        Assert.Equal(OpportunityLifecycleState.Suspended, result.Publication.State);
        Assert.Equal("MarketDataOffline", result.Publication.ReasonCode);
        Assert.NotEmpty(result.Opportunity.SupportingEvidence);
    }

    [Fact]
    public async Task Reconnect_waits_for_valid_data_and_exposes_unresolved_gap_until_shutdown()
    {
        var now = DateTimeOffset.UtcNow;
        var initialEvents = CreateSession("MOE", "manual-session", now.AddSeconds(-10), 100m);
        var source = new ReconnectingLiveSource(initialEvents);
        var scannerState = new ScannerState();
        var disconnected = new TaskCompletionSource<ScannerStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        scannerState.Changed += () =>
        {
            var status = scannerState.Read().Status;
            if (status.FeedStatus == MarketDataHealthStatus.Offline)
            {
                disconnected.TrySetResult(status);
            }
        };
        using var cancellation = new CancellationTokenSource();
        var pipeline = new ScannerPipeline(
            source,
            new RecordingStore(),
            scannerState,
            new ScannerConfiguration(),
            new FeatureCalculator(),
            new MomentumContinuationDetector(),
            TimeProvider.System,
            new LiveMarketDataRecoveryOptions(ReconnectDelayMilliseconds: 100));

        var run = pipeline.RunAsync(cancellation.Token);
        try
        {
            var status = await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => scannerState.Read().Results.Count > 0);

            var result = Assert.Single(scannerState.Read().Results);
            Assert.True(status.HasUnresolvedDataGap);
            Assert.Contains("gap", status.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(OpportunityLifecycleState.Suspended, result.Publication.State);
            Assert.Equal("MarketDataGapUnresolved", result.Publication.ReasonCode);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await source.SecondAttemptStarted.WaitAsync(timeout.Token);
            Assert.Equal(MarketDataHealthStatus.Connecting, scannerState.Read().Status.FeedStatus);
            source.ResumeConnection();
            await WaitUntilAsync(() =>
                scannerState.Read().Status.FeedStatus == MarketDataHealthStatus.Live
                && scannerState.Read().Status.EventsProcessed > initialEvents.Length);

            status = scannerState.Read().Status;
            result = Assert.Single(scannerState.Read().Results);
            Assert.True(status.HasUnresolvedDataGap);
            Assert.Contains("gap", status.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(OpportunityLifecycleState.Suspended, result.Publication.State);
            Assert.Equal("MarketDataGapUnresolved", result.Publication.ReasonCode);
        }
        finally
        {
            await cancellation.CancelAsync();
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }

        var attemptsAtShutdown = source.Attempts;
        await Task.Delay(50);
        Assert.Equal(attemptsAtShutdown, source.Attempts);
    }

    [Fact]
    public async Task Cancellation_interrupts_reconnect_delay_without_another_connection_attempt()
    {
        var source = new ReconnectingLiveSource([]);
        var scannerState = new ScannerState();
        using var cancellation = new CancellationTokenSource();
        var pipeline = new ScannerPipeline(
            source,
            new RecordingStore(),
            scannerState,
            new ScannerConfiguration(),
            new FeatureCalculator(),
            new MomentumContinuationDetector(),
            TimeProvider.System,
            new LiveMarketDataRecoveryOptions(ReconnectDelayMilliseconds: 300_000));

        var run = pipeline.RunAsync(cancellation.Token);
        try
        {
            await WaitForStatusAsync(scannerState, MarketDataHealthStatus.Offline);
            Assert.Equal(1, source.Attempts);
            Assert.False(source.SecondAttemptStarted.IsCompleted);
        }
        finally
        {
            await cancellation.CancelAsync();
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(1, source.Attempts);
        Assert.False(source.SecondAttemptStarted.IsCompleted);
        Assert.Equal(MarketDataHealthStatus.Offline, scannerState.Read().Status.FeedStatus);
    }

    [Fact]
    public async Task Live_feed_reports_receive_time_and_becomes_stale_without_another_event()
    {
        var receivedAt = DateTimeOffset.UtcNow;
        var marketEvent = new TradeEvent(
            "MOE",
            "manual-session",
            receivedAt.AddDays(-1),
            receivedAt,
            "Massive",
            "same-event",
            100m,
            10);
        var duplicate = marketEvent with { ReceivedTimeUtc = receivedAt.AddMilliseconds(100) };
        var scannerState = new ScannerState();
        using var cancellation = new CancellationTokenSource();
        var pipeline = new ScannerPipeline(
            new PausingLiveSource([marketEvent, duplicate]),
            new RecordingStore(),
            scannerState,
            new ScannerConfiguration { MaximumLiveFeedStalenessSeconds = 1 },
            new FeatureCalculator(),
            new MomentumContinuationDetector(),
            TimeProvider.System);

        var run = pipeline.RunAsync(cancellation.Token);
        try
        {
            await WaitForStatusAsync(scannerState, MarketDataHealthStatus.Live);
            var liveStatus = scannerState.Read().Status;
            Assert.Equal(1, liveStatus.EventsProcessed);
            Assert.Equal(receivedAt.AddMilliseconds(100), liveStatus.LatestReceivedTimeUtc);
            Assert.Equal(receivedAt.AddDays(-1), liveStatus.LatestEventTimeUtc);
            Assert.NotNull(liveStatus.ReceiveAge);
            Assert.True(liveStatus.ReceiveAge < TimeSpan.FromSeconds(1));

            await WaitForStatusAsync(scannerState, MarketDataHealthStatus.Stale);

            var staleStatus = scannerState.Read().Status;
            Assert.Equal(receivedAt.AddMilliseconds(100), staleStatus.LatestReceivedTimeUtc);
            Assert.True(staleStatus.ReceiveAge >= TimeSpan.FromSeconds(1));
        }
        finally
        {
            await cancellation.CancelAsync();
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void Rejects_nonpositive_live_feed_staleness_threshold()
    {
        var configuration = new ScannerConfiguration { MaximumLiveFeedStalenessSeconds = 0 };

        Assert.Throws<InvalidOperationException>(configuration.Validate);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(301, 1)]
    [InlineData(30, 0)]
    [InlineData(30, 33)]
    public void Rejects_out_of_range_prior_session_warmup_options(
        int timeoutSeconds,
        int maximumConcurrentWarmups)
    {
        var options = new PriorSessionWarmupOptions(timeoutSeconds, maximumConcurrentWarmups);

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(301)]
    public void Rejects_out_of_range_graceful_shutdown_timeout(int seconds)
    {
        var options = new LivePipelineOptions { GracefulShutdownTimeoutSeconds = seconds };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public async Task Live_analysis_waits_for_its_symbol_session_warmup_before_first_persist()
    {
        var events = CreateSession(
            "MOE",
            "warmup-session",
            DateTimeOffset.UtcNow,
            100m);
        var release = new TaskCompletionSource<PriorSessionWarmupResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new ControlledWarmupProvider((_, sessionId, _, _) => release.Task);
        var store = new RecordingStore();
        var scannerState = new ScannerState();
        using var cancellation = new CancellationTokenSource();
        var pipeline = new ScannerPipeline(
            new PausingLiveSource(events),
            store,
            scannerState,
            new ScannerConfiguration(),
            new FeatureCalculator(),
            new MomentumContinuationDetector(),
            TimeProvider.System,
            priorSessionContextProvider: provider);
        var run = pipeline.RunAsync(cancellation.Token);
        try
        {
            await provider.WaitForCallsAsync(1);
            await Task.Delay(100);
            Assert.Empty(store.Runs);
            Assert.Empty(scannerState.Read().Results);
            Assert.Equal(
                PriorSessionWarmupStatus.Pending,
                Assert.Single(scannerState.Read().Warmups).Status);

            release.SetResult(UnavailableWarmup("MOE", "warmup-session"));
            await WaitUntilAsync(() => store.Runs.Count == 1);
            var result = Assert.Single(scannerState.Read().Results);
            Assert.Equal(
                PriorSessionContextStatus.Unavailable,
                result.PriorSessionContext?.Context.Completeness);
            Assert.Equal(
                PriorSessionWarmupStatus.Unavailable,
                result.PriorSessionWarmup?.Status);
        }
        finally
        {
            await cancellation.CancelAsync();
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Slow_warmup_does_not_block_an_unrelated_symbol()
    {
        var now = DateTimeOffset.UtcNow;
        MarketEvent[] events =
        [
            new TradeEvent("SLOW", "shared-session", now, now, "Alpaca", "slow-1", 10m, 1),
            new TradeEvent("FAST", "shared-session", now, now, "Alpaca", "fast-1", 20m, 1)
        ];
        var releaseSlow = new TaskCompletionSource<PriorSessionWarmupResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new ControlledWarmupProvider((symbol, sessionId, _, _) =>
            symbol == "SLOW"
                ? releaseSlow.Task
                : Task.FromResult(UnavailableWarmup(symbol, sessionId)));
        var store = new RecordingStore();
        var scannerState = new ScannerState();
        using var cancellation = new CancellationTokenSource();
        var pipeline = new ScannerPipeline(
            new PausingLiveSource(events),
            store,
            scannerState,
            new ScannerConfiguration(),
            new FeatureCalculator(),
            new MomentumContinuationDetector(),
            TimeProvider.System,
            priorSessionContextProvider: provider);
        var run = pipeline.RunAsync(cancellation.Token);
        try
        {
            await provider.WaitForCallsAsync(2);
            await WaitUntilAsync(() => scannerState.Read().Results
                .Any(item => item.Features.Symbol == "FAST"));
            Assert.DoesNotContain(
                scannerState.Read().Results,
                item => item.Features.Symbol == "SLOW");

            releaseSlow.SetResult(UnavailableWarmup("SLOW", "shared-session"));
            await WaitUntilAsync(() => scannerState.Read().Results.Count == 2);
        }
        finally
        {
            await cancellation.CancelAsync();
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Late_obsolete_session_warmup_does_not_replace_current_session_scope()
    {
        var now = DateTimeOffset.UtcNow;
        MarketEvent[] events =
        [
            new TradeEvent("AAPL", "session-a", now.AddDays(-1), now.AddDays(-1), "Alpaca", "a-1", 10m, 1),
            new TradeEvent("AAPL", "session-b", now, now, "Alpaca", "b-1", 20m, 1)
        ];
        var releaseA = new TaskCompletionSource<PriorSessionWarmupResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new ControlledWarmupProvider((_, sessionId, _, cancellation) =>
            sessionId == "session-a"
                ? releaseA.Task
                : Task.FromResult(UnavailableWarmup("AAPL", sessionId)));
        var store = new RecordingStore();
        var scannerState = new ScannerState();
        using var cancellation = new CancellationTokenSource();
        var pipeline = new ScannerPipeline(
            new PausingLiveSource(events),
            store,
            scannerState,
            new ScannerConfiguration(),
            new FeatureCalculator(),
            new MomentumContinuationDetector(),
            TimeProvider.System,
            priorSessionContextProvider: provider);
        var run = pipeline.RunAsync(cancellation.Token);
        try
        {
            await provider.WaitForCallsAsync(2);
            await WaitUntilAsync(() => scannerState.Read().Results
                .Any(item => item.Features.SessionId == "session-b"));
            Assert.Equal(
                "session-b",
                Assert.Single(scannerState.Read().Warmups).Scope.CurrentSessionId);

            releaseA.SetResult(UnavailableWarmup("AAPL", "session-a"));
            await WaitUntilAsync(() => scannerState.Read().Results.Count == 2);
            Assert.Equal(
                "session-b",
                Assert.Single(scannerState.Read().Warmups).Scope.CurrentSessionId);
            Assert.Contains(
                scannerState.Read().Results,
                item => item.PriorSessionWarmup?.Scope.CurrentSessionId == "session-a");
            Assert.Contains(
                scannerState.Read().Results,
                item => item.PriorSessionWarmup?.Scope.CurrentSessionId == "session-b");
        }
        finally
        {
            await cancellation.CancelAsync();
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static ScannerPipeline CreatePipeline(
        IReadOnlyCollection<MarketEvent> events,
        RecordingStore store) =>
        new(
            new EventSource(events),
            store,
            new ScannerState(),
            new ScannerConfiguration(),
            new FeatureCalculator(),
            new MomentumContinuationDetector(),
            TimeProvider.System);

    private static PriorSessionWarmupResult UnavailableWarmup(
        string symbol,
        string sessionId)
    {
        var context = new PriorSessionContextBuilder(new FeatureCalculator()).BuildUnavailable(
            symbol,
            sessionId,
            "Synthetic unavailable history.");
        var capabilities = new MarketDataCapabilities(
            "Alpaca", "iex", MarketDataCoverage.Limited, true, true, true);
        return new PriorSessionWarmupResult(
            context,
            new PriorSessionWarmupOutcome(
                PriorSessionWarmupScopeFactory.Create(
                    symbol,
                    sessionId,
                    capabilities,
                    priorSessionId: null,
                    PriorSessionContextBuilder.DefinitionVersion,
                    "features-v16",
                    "scanner-config-v19"),
                PriorSessionWarmupStatus.Unavailable,
                context.CompletenessReason,
                ContextCompleteness: context.Completeness));
    }

    private static void AssertAnalysis(
        ScannerAnalysisResult result,
        string symbol,
        string sessionId,
        decimal expectedVwap)
    {
        Assert.Equal(symbol, result.Features.Symbol);
        Assert.Equal(sessionId, result.Features.SessionId);
        Assert.Equal(expectedVwap, result.Features.SessionVwap);
        Assert.Equal(sessionId, result.Opportunity.SessionId);
    }

    private static MarketEvent[] CreateSession(
        string symbol,
        string sessionId,
        DateTimeOffset start,
        decimal price)
    {
        var received = start.AddMilliseconds(10);
        return
        [
            new TradeEvent(symbol, sessionId, start, received, "Fixture", $"{sessionId}-{symbol}-1", price, 10),
            new TradeEvent(symbol, sessionId, start.AddSeconds(20), received.AddSeconds(20), "Fixture", $"{sessionId}-{symbol}-3", price + 2m, 10),
            new QuoteEvent(
                symbol,
                sessionId,
                start.AddSeconds(42),
                received.AddSeconds(42),
                "Fixture",
                $"{sessionId}-{symbol}-quote",
                price + 2.99m,
                price + 3.01m,
                500,
                500),
            new TradeEvent(symbol, sessionId, start.AddSeconds(10), received.AddSeconds(43), "Fixture", $"{sessionId}-{symbol}-2-late", price + 1m, 10)
        ];
    }

    private sealed class EventSource(IReadOnlyCollection<MarketEvent> events) : IMarketDataSource
    {
        public async IAsyncEnumerable<MarketEvent> ReadEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var marketEvent in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return marketEvent;
            }
        }
    }

    private sealed class EndingLiveSource(IReadOnlyCollection<MarketEvent> events) : ILiveMarketDataSource
    {
        public async IAsyncEnumerable<MarketEvent> ReadEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var marketEvent in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return marketEvent;
            }

            await Task.CompletedTask;
            throw new IOException("Test stream ended.");
        }
    }

    private sealed class ControlledWarmupProvider(
        Func<string, string, MarketDataCapabilities, CancellationToken, Task<PriorSessionWarmupResult>> warmup)
        : IPriorSessionContextProvider
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<PriorSessionContextView> GetContextAsync(
            string symbol,
            string currentSessionId,
            DateTimeOffset currentEventTimeUtc,
            decimal? currentReferencePrice,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PriorSessionWarmupResult> WarmupAsync(
            string symbol,
            string currentSessionId,
            DateTimeOffset currentEventTimeUtc,
            MarketDataCapabilities currentMarketData,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return warmup(
                symbol,
                currentSessionId,
                currentMarketData,
                cancellationToken);
        }

        public async Task WaitForCallsAsync(int expected)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (Calls < expected)
            {
                await Task.Delay(10, timeout.Token);
            }
        }
    }

    private sealed class PausingLiveSource(IReadOnlyCollection<MarketEvent> events) : ILiveMarketDataSource
    {
        public async IAsyncEnumerable<MarketEvent> ReadEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var marketEvent in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return marketEvent;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class CompletingLiveSource(IReadOnlyCollection<MarketEvent> events) : ILiveMarketDataSource
    {
        public MarketDataCapabilities Capabilities { get; } = new(
            "TestProvider",
            "test-feed",
            MarketDataCoverage.Synthetic,
            true,
            true,
            true);

        public async IAsyncEnumerable<MarketEvent> ReadEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var marketEvent in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return marketEvent;
            }

            await Task.CompletedTask;
        }
    }

    private sealed class ReconnectingLiveSource(IReadOnlyCollection<MarketEvent> events)
        : IReconnectableMarketDataSource
    {
        private int _attempts;
        private readonly TaskCompletionSource _secondAttemptStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _resumeConnection = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int Attempts => Volatile.Read(ref _attempts);
        public Task SecondAttemptStarted => _secondAttemptStarted.Task;

        public void ResumeConnection() => _resumeConnection.TrySetResult();

        public MarketDataCapabilities Capabilities { get; } = new(
            "Alpaca",
            "test",
            MarketDataCoverage.Synthetic,
            true,
            true,
            true);

        public async IAsyncEnumerable<MarketEvent> ReadEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var attempt = Interlocked.Increment(ref _attempts);
            if (attempt == 1)
            {
                foreach (var marketEvent in events)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return marketEvent;
                }

                throw new IOException("Synthetic socket close.");
            }

            _secondAttemptStarted.TrySetResult();
            await _resumeConnection.Task.WaitAsync(cancellationToken);
            var lastTrade = Assert.IsType<TradeEvent>(events.OfType<TradeEvent>().Last());
            yield return lastTrade with
            {
                SequenceId = "reconnected-event",
                EventTimeUtc = lastTrade.EventTimeUtc.AddSeconds(1),
                ReceivedTimeUtc = DateTimeOffset.UtcNow
            };
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }

    private static async Task WaitForStatusAsync(ScannerState state, MarketDataHealthStatus expectedStatus)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (state.Read().Status.FeedStatus != expectedStatus)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    private sealed class RecordingStore : IScannerStore
    {
        public List<(IReadOnlyCollection<MarketEvent> Events, FeatureSnapshot Features, Opportunity Opportunity, TradePlan TradePlan)> Runs { get; } = [];

        public Task PersistRunAsync(
            ScannerConfiguration configuration,
            IReadOnlyCollection<MarketEvent> marketEvents,
            FeatureSnapshot featureSnapshot,
            Opportunity opportunity,
            TradePlan tradePlan,
            CancellationToken cancellationToken)
        {
            Runs.Add((marketEvents.ToArray(), featureSnapshot, opportunity, tradePlan));
            return Task.CompletedTask;
        }
    }
}
