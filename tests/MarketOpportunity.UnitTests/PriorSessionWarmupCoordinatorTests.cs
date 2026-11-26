using MarketOpportunity.Analytics;
using MarketOpportunity.Application;
using MarketOpportunity.Domain;

namespace MarketOpportunity.UnitTests;

public sealed class PriorSessionWarmupCoordinatorTests
{
    private static readonly MarketDataCapabilities LiveCapabilities =
        new("Alpaca", "iex", MarketDataCoverage.Limited, true, true, true);
    private static readonly MarketSessionDefinition Current =
        Definition("current-session", new DateOnly(2026, 11, 6), 2026, 11, 6);
    private static readonly MarketSessionDefinition CurrentB =
        Definition("current-session-b", new DateOnly(2026, 11, 7), 2026, 11, 7);
    private static readonly MarketSessionDefinition Prior =
        Definition("prior-session", new DateOnly(2026, 11, 5), 2026, 11, 5);
    private static readonly MarketSessionDefinition PriorB =
        Definition("prior-session-b", new DateOnly(2026, 11, 6), 2026, 11, 6);

    [Fact]
    public async Task Same_scope_shares_one_in_flight_warmup_and_reuses_terminal_success()
    {
        var release = new TaskCompletionSource<HistoricalMarketSessionData>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calendar = new StubCalendar(_ => Resolution(Current, Prior));
        var historical = new StubHistorical((_, _, _) => release.Task);
        var coordinator = CreateCoordinator(calendar, historical);
        var first = coordinator.WarmupAsync(
            "AAPL", Current.SessionId, Current.OpenAtUtc, LiveCapabilities, CancellationToken.None);
        var second = coordinator.WarmupAsync(
            "aapl", Current.SessionId, Current.OpenAtUtc, LiveCapabilities, CancellationToken.None);

        await historical.WaitForCallsAsync(1);
        Assert.Equal(1, calendar.Calls);
        Assert.Equal(1, historical.Calls);
        release.SetResult(CompleteHistory("AAPL", Prior));

        var firstResult = await first;
        var secondResult = await second;
        var reused = await coordinator.WarmupAsync(
            "AAPL", Current.SessionId, Current.OpenAtUtc, LiveCapabilities, CancellationToken.None);
        Assert.Equal(PriorSessionWarmupStatus.CompleteUsable, firstResult.Outcome.Status);
        Assert.Equal(firstResult.Context.ContextId, secondResult.Context.ContextId);
        Assert.Equal(firstResult.Context.ContextId, reused.Context.ContextId);
        Assert.Equal(Prior.SessionId, reused.Outcome.Scope.PriorSessionId);
        Assert.Equal(1, calendar.Calls);
        Assert.Equal(1, historical.Calls);
    }

    [Fact]
    public async Task Cancelling_one_same_scope_waiter_does_not_cancel_shared_warmup()
    {
        var release = new TaskCompletionSource<HistoricalMarketSessionData>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var historical = new StubHistorical(async (_, _, cancellation) =>
            await release.Task.WaitAsync(cancellation));
        var coordinator = CreateCoordinator(
            new StubCalendar(_ => Resolution(Current, Prior)),
            historical);
        using var cancelledWaiter = new CancellationTokenSource();

        var first = coordinator.WarmupAsync(
            "AAPL",
            Current.SessionId,
            Current.OpenAtUtc,
            LiveCapabilities,
            cancelledWaiter.Token);
        var second = coordinator.WarmupAsync(
            "AAPL",
            Current.SessionId,
            Current.OpenAtUtc,
            LiveCapabilities,
            CancellationToken.None);

        await historical.WaitForCallsAsync(1);
        await cancelledWaiter.CancelAsync();
        var cancelledResult = await first;
        Assert.Equal(PriorSessionWarmupStatus.Cancelled, cancelledResult.Outcome.Status);

        release.SetResult(CompleteHistory("AAPL", Prior));
        var successfulResult = await second;
        var reused = await coordinator.WarmupAsync(
            "AAPL",
            Current.SessionId,
            Current.OpenAtUtc,
            LiveCapabilities,
            CancellationToken.None);

        Assert.Equal(PriorSessionWarmupStatus.CompleteUsable, successfulResult.Outcome.Status);
        Assert.Equal(successfulResult.Context.ContextId, reused.Context.ContextId);
        Assert.Equal(1, historical.Calls);
    }

    [Fact]
    public async Task Different_symbols_warm_independently_under_the_concurrency_bound()
    {
        var aaplRelease = new TaskCompletionSource<HistoricalMarketSessionData>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var msftRelease = new TaskCompletionSource<HistoricalMarketSessionData>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var historical = new StubHistorical((symbol, _, _) =>
            symbol == "AAPL" ? aaplRelease.Task : msftRelease.Task);
        var coordinator = CreateCoordinator(
            new StubCalendar(_ => Resolution(Current, Prior)),
            historical,
            maximumConcurrentWarmups: 2);

        var aapl = coordinator.WarmupAsync(
            "AAPL", Current.SessionId, Current.OpenAtUtc, LiveCapabilities, CancellationToken.None);
        var msft = coordinator.WarmupAsync(
            "MSFT", Current.SessionId, Current.OpenAtUtc, LiveCapabilities, CancellationToken.None);

        await historical.WaitForCallsAsync(2);
        aaplRelease.SetResult(CompleteHistory("AAPL", Prior));
        msftRelease.SetResult(CompleteHistory("MSFT", Prior));
        var results = await Task.WhenAll(aapl, msft);

        Assert.All(
            results,
            result => Assert.Equal(PriorSessionWarmupStatus.CompleteUsable, result.Outcome.Status));
        Assert.Equal(["AAPL", "MSFT"], results.Select(item => item.Outcome.Scope.Symbol).Order());
    }

    [Fact]
    public async Task Terminal_failure_is_frozen_and_is_not_immediately_retried()
    {
        var historical = new StubHistorical((symbol, session, cancellation) =>
            Task.FromResult(History(
                symbol,
                session,
                HistoricalRetrievalStatus.AuthenticationFailure)));
        var calendar = new StubCalendar(_ => Resolution(Current, Prior));
        var coordinator = CreateCoordinator(calendar, historical);

        var first = await coordinator.WarmupAsync(
            "AAPL", Current.SessionId, Current.OpenAtUtc, LiveCapabilities, CancellationToken.None);
        var second = await coordinator.WarmupAsync(
            "AAPL", Current.SessionId, Current.OpenAtUtc, LiveCapabilities, CancellationToken.None);

        Assert.Equal(PriorSessionWarmupStatus.AuthenticationFailure, first.Outcome.Status);
        Assert.Equal(first.Context.ContextId, second.Context.ContextId);
        Assert.Equal(PriorSessionContextStatus.Unavailable, second.Context.Completeness);
        Assert.Equal(1, calendar.Calls);
        Assert.Equal(1, historical.Calls);
    }

    [Fact]
    public async Task Session_scopes_are_independent_and_late_old_completion_cannot_replace_new_scope()
    {
        var oldRelease = new TaskCompletionSource<HistoricalMarketSessionData>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var historical = new StubHistorical((_, session, _) =>
            session.SessionId == Prior.SessionId
                ? oldRelease.Task
                : Task.FromResult(CompleteHistory("AAPL", PriorB)));
        var coordinator = CreateCoordinator(
            new StubCalendar((_, sessionId, _, _) => sessionId == Current.SessionId
                ? Resolution(Current, Prior)
                : Resolution(CurrentB, PriorB)),
            historical);

        var oldWarmup = coordinator.WarmupAsync(
            "AAPL", Current.SessionId, Current.OpenAtUtc, LiveCapabilities, CancellationToken.None);
        await historical.WaitForCallsAsync(1);
        var newWarmup = await coordinator.WarmupAsync(
            "AAPL", CurrentB.SessionId, CurrentB.OpenAtUtc, LiveCapabilities, CancellationToken.None);
        Assert.Equal(CurrentB.SessionId, newWarmup.Outcome.Scope.CurrentSessionId);
        Assert.Equal(PriorB.SessionId, newWarmup.Context.PriorSessionId);
        Assert.Equal(PriorSessionWarmupStatus.CompleteUsable, newWarmup.Outcome.Status);

        oldRelease.SetResult(CompleteHistory("AAPL", Prior));
        var completedOld = await oldWarmup;
        var retainedNew = await coordinator.WarmupAsync(
            "AAPL", CurrentB.SessionId, CurrentB.OpenAtUtc, LiveCapabilities, CancellationToken.None);
        Assert.Equal(Current.SessionId, completedOld.Outcome.Scope.CurrentSessionId);
        Assert.Equal(PriorSessionWarmupStatus.Cancelled, completedOld.Outcome.Status);
        Assert.Null(completedOld.Context.PriorSessionId);
        Assert.Equal(newWarmup.Context.ContextId, retainedNew.Context.ContextId);
        Assert.NotEqual(completedOld.Context.ContextId, retainedNew.Context.ContextId);
    }

    [Fact]
    public async Task Cancellation_of_one_scope_does_not_corrupt_another()
    {
        var blocked = new TaskCompletionSource<HistoricalMarketSessionData>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var historical = new StubHistorical(async (symbol, session, cancellation) =>
        {
            if (symbol == "AAPL")
            {
                await blocked.Task.WaitAsync(cancellation);
            }

            return CompleteHistory(symbol, session);
        });
        var coordinator = CreateCoordinator(
            new StubCalendar(_ => Resolution(Current, Prior)),
            historical,
            maximumConcurrentWarmups: 2);
        using var cancelled = new CancellationTokenSource();

        var aapl = coordinator.WarmupAsync(
            "AAPL", Current.SessionId, Current.OpenAtUtc, LiveCapabilities, cancelled.Token);
        await historical.WaitForCallsAsync(1);
        var msft = coordinator.WarmupAsync(
            "MSFT", Current.SessionId, Current.OpenAtUtc, LiveCapabilities, CancellationToken.None);
        await historical.WaitForCallsAsync(2);
        await cancelled.CancelAsync();

        var cancelledResult = await aapl;
        var successfulResult = await msft;
        Assert.Equal(PriorSessionWarmupStatus.Cancelled, cancelledResult.Outcome.Status);
        Assert.Equal(PriorSessionWarmupStatus.CompleteUsable, successfulResult.Outcome.Status);
        Assert.Equal("MSFT", successfulResult.Outcome.Scope.Symbol);
    }

    [Theory]
    [InlineData(HistoricalRetrievalStatus.EntitlementFailure, PriorSessionWarmupStatus.EntitlementFailure)]
    [InlineData(HistoricalRetrievalStatus.LimitExceeded, PriorSessionWarmupStatus.LimitExceeded)]
    [InlineData(HistoricalRetrievalStatus.ProviderFailure, PriorSessionWarmupStatus.ProviderFailure)]
    [InlineData(HistoricalRetrievalStatus.MalformedResponse, PriorSessionWarmupStatus.Invalid)]
    public async Task Historical_failures_fall_back_honestly_without_usable_prior_levels(
        HistoricalRetrievalStatus retrievalStatus,
        PriorSessionWarmupStatus expectedWarmupStatus)
    {
        var coordinator = CreateCoordinator(
            new StubCalendar(_ => Resolution(Current, Prior)),
            new StubHistorical((symbol, session, _) =>
                Task.FromResult(History(symbol, session, retrievalStatus))));

        var result = await coordinator.WarmupAsync(
            "AAPL", Current.SessionId, Current.OpenAtUtc, LiveCapabilities, CancellationToken.None);

        Assert.Equal(expectedWarmupStatus, result.Outcome.Status);
        Assert.NotEqual(PriorSessionContextStatus.Available, result.Context.Completeness);
        Assert.Empty(result.Context.SupportCandidates);
        Assert.Empty(result.Context.ResistanceCandidates);
        Assert.Null(result.Context.PriorSessionHigh);
    }

    [Fact]
    public async Task Incomplete_declared_coverage_remains_partial()
    {
        var incomplete = CompleteHistory("AAPL", Prior) with
        {
            DeclaresCompleteCoverage = false
        };
        var coordinator = CreateCoordinator(
            new StubCalendar(_ => Resolution(Current, Prior)),
            new StubHistorical((_, _, _) => Task.FromResult(incomplete)));

        var result = await coordinator.WarmupAsync(
            "AAPL", Current.SessionId, Current.OpenAtUtc, LiveCapabilities, CancellationToken.None);

        Assert.Equal(PriorSessionWarmupStatus.Partial, result.Outcome.Status);
        Assert.Equal(PriorSessionContextStatus.Partial, result.Context.Completeness);
        Assert.Null(result.Context.PriorSessionHigh);
        Assert.NotNull(result.Context.ObservedHigh);
    }

    [Fact]
    public async Task Calendar_unavailable_and_invalid_reconstruction_are_terminal_fallbacks()
    {
        var calendarFailure = CreateCoordinator(
            new StubCalendar(_ => new MarketSessionCalendarResolution(
                null,
                null,
                "test-calendar",
                "Calendar unavailable.",
                MarketSessionCalendarStatus.ProviderFailure)),
            new StubHistorical((symbol, session, _) =>
                Task.FromResult(CompleteHistory(symbol, session))));
        var invalidHistory = CompleteHistory("AAPL", Prior) with
        {
            Provenance = new HistoricalDataProvenance(
                "WrongProvider", "sip", "historical-rest", DateTimeOffset.UtcNow, 1, 0)
        };
        var invalidReconstruction = CreateCoordinator(
            new StubCalendar(_ => Resolution(Current, Prior)),
            new StubHistorical((_, _, _) => Task.FromResult(invalidHistory)));

        var unavailable = await calendarFailure.WarmupAsync(
            "AAPL", Current.SessionId, Current.OpenAtUtc, LiveCapabilities, CancellationToken.None);
        var invalid = await invalidReconstruction.WarmupAsync(
            "AAPL", Current.SessionId, Current.OpenAtUtc, LiveCapabilities, CancellationToken.None);

        Assert.Equal(PriorSessionWarmupStatus.ProviderFailure, unavailable.Outcome.Status);
        Assert.Equal(PriorSessionWarmupStatus.Invalid, invalid.Outcome.Status);
        Assert.Equal(PriorSessionContextStatus.Invalid, invalid.Context.Completeness);
    }

    [Fact]
    public async Task Warmup_timeout_is_bounded_and_falls_back_to_unavailable()
    {
        var coordinator = CreateCoordinator(
            new StubCalendar(_ => Resolution(Current, Prior)),
            new StubHistorical(async (_, _, cancellation) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
                throw new InvalidOperationException("Cancellation should have interrupted the warm-up.");
            }),
            timeoutSeconds: 1);

        var result = await coordinator.WarmupAsync(
            "AAPL", Current.SessionId, Current.OpenAtUtc, LiveCapabilities, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(PriorSessionWarmupStatus.TimedOut, result.Outcome.Status);
        Assert.Equal(PriorSessionContextStatus.Unavailable, result.Context.Completeness);
    }

    private static PriorSessionContextProvider CreateCoordinator(
        IMarketSessionCalendar calendar,
        IHistoricalMarketDataSource historical,
        int maximumConcurrentWarmups = 4,
        int timeoutSeconds = 5)
    {
        var builder = new PriorSessionContextBuilder(new FeatureCalculator());
        return new PriorSessionContextProvider(
            calendar,
            historical,
            builder,
            new ScannerConfiguration(),
            new PriorSessionHistoricalReconstructor(builder),
            new PriorSessionWarmupOptions(timeoutSeconds, maximumConcurrentWarmups));
    }

    private static MarketSessionCalendarResolution Resolution(
        MarketSessionDefinition current,
        MarketSessionDefinition prior) =>
        new(current, prior, "test-calendar", null);

    private static MarketSessionDefinition Definition(
        string sessionId,
        DateOnly tradingDate,
        int year,
        int month,
        int day) =>
        new(
            sessionId,
            tradingDate,
            new DateTimeOffset(year, month, day, 13, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(year, month, day, 20, 0, 0, TimeSpan.Zero),
            "Alpaca",
            "market-calendar-rest",
            "test-calendar-v1",
            MarketSessionType.Regular,
            MarketSessionValidity.Valid);

    private static HistoricalMarketSessionData CompleteHistory(
        string symbol,
        MarketSessionDefinition session) =>
        CompleteHistory(symbol, session.ToMarketSession());

    private static HistoricalMarketSessionData CompleteHistory(
        string symbol,
        MarketSession session)
    {
        decimal[] prices = [102m, 100m, 101m, 99m, 100m, 98m];
        var events = prices.Select((price, index) => new TradeEvent(
            symbol,
            session.SessionId,
            session.StartUtc.AddMinutes(index + 1),
            session.StartUtc.AddMinutes(index + 2),
            "Alpaca",
            $"history-{symbol}-{session.SessionId}-{index}",
            price,
            100));
        return History(symbol, session, HistoricalRetrievalStatus.SuccessPaginationExhausted, events.ToArray());
    }

    private static HistoricalMarketSessionData History(
        string symbol,
        MarketSessionDefinition session,
        HistoricalRetrievalStatus retrievalStatus,
        IReadOnlyList<MarketEvent>? events = null) =>
        History(symbol, session.ToMarketSession(), retrievalStatus, events);

    private static HistoricalMarketSessionData History(
        string symbol,
        MarketSession session,
        HistoricalRetrievalStatus retrievalStatus,
        IReadOnlyList<MarketEvent>? events = null)
    {
        var completed = retrievalStatus == HistoricalRetrievalStatus.SuccessPaginationExhausted;
        return new HistoricalMarketSessionData(
            events ?? [],
            new MarketDataCapabilities(
                "Alpaca", "sip", MarketDataCoverage.Consolidated, false, true, false),
            "historical-rest",
            completed ? session.StartUtc : null,
            completed ? session.EndUtc : null,
            DeclaresCompleteCoverage: completed,
            HasKnownGaps: false,
            new HistoricalDataRequest(symbol, session.SessionId, session.StartUtc, session.EndUtc),
            retrievalStatus,
            new HistoricalDataProvenance(
                "Alpaca", "sip", "historical-rest", DateTimeOffset.UtcNow, completed ? 1 : 0, 0),
            PaginationExhausted: completed,
            StatusMessage: retrievalStatus.ToString());
    }

    private sealed class StubCalendar(
        Func<string, string, DateTimeOffset, CancellationToken, MarketSessionCalendarResolution> resolve)
        : IMarketSessionCalendar
    {
        private int _calls;

        public StubCalendar(Func<object?, MarketSessionCalendarResolution> resolve)
            : this((_, _, _, _) => resolve(null))
        {
        }

        public int Calls => Volatile.Read(ref _calls);

        public Task<MarketSessionCalendarResolution> ResolvePreviousSessionAsync(
            string symbol,
            string currentSessionId,
            DateTimeOffset currentEventTimeUtc,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(resolve(symbol, currentSessionId, currentEventTimeUtc, cancellationToken));
        }
    }

    private sealed class StubHistorical(
        Func<string, MarketSession, CancellationToken, Task<HistoricalMarketSessionData>> read)
        : IHistoricalMarketDataSource
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public async Task<HistoricalMarketSessionData?> ReadSessionAsync(
            string symbol,
            MarketSession session,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return await read(symbol, session, cancellationToken);
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
}
