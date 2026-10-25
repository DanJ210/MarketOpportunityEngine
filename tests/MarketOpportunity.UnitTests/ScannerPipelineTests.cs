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
        });
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
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        }
    }

    [Fact]
    public void Rejects_nonpositive_live_feed_staleness_threshold()
    {
        var configuration = new ScannerConfiguration { MaximumLiveFeedStalenessSeconds = 0 };

        Assert.Throws<InvalidOperationException>(configuration.Validate);
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

    private sealed class RecordingStore : IScannerStore
    {
        public List<(IReadOnlyCollection<MarketEvent> Events, FeatureSnapshot Features, Opportunity Opportunity)> Runs { get; } = [];

        public Task PersistRunAsync(
            ScannerConfiguration configuration,
            IReadOnlyCollection<MarketEvent> marketEvents,
            FeatureSnapshot featureSnapshot,
            Opportunity opportunity,
            CancellationToken cancellationToken)
        {
            Runs.Add((marketEvents.ToArray(), featureSnapshot, opportunity));
            return Task.CompletedTask;
        }
    }
}
