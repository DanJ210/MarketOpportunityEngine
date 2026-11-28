using MarketOpportunity.Analytics;
using MarketOpportunity.Application;
using MarketOpportunity.Domain;

namespace MarketOpportunity.UnitTests;

public sealed class MultiSessionHistoricalReconstructorTests
{
    private static readonly DateOnly CurrentDate = new(2026, 11, 26);
    private static readonly DateTimeOffset RetrievedAtUtc =
        new(2026, 11, 26, 22, 0, 0, TimeSpan.Zero);
    private static readonly HistoricalReconstructionRequirements Requirements =
        new("TestHistory", "test-feed", "historical-test");
    private static readonly MarketSessionDefinition CurrentSession = Session("D0", CurrentDate);
    private static readonly MarketSessionDefinition D1 = Session("D1", CurrentDate.AddDays(-1));
    private static readonly MarketSessionDefinition D2 = Session("D2", CurrentDate.AddDays(-2));
    private static readonly MarketSessionDefinition D3 = Session("D3", CurrentDate.AddDays(-3));

    [Fact]
    public async Task Three_complete_contiguous_sessions_produce_available_batch_one_assessment()
    {
        var fixture = CreateFixture();

        var result = await fixture.Reconstructor.ReconstructAsync(
            "aapl",
            CurrentSession.SessionId,
            CurrentSession.OpenAtUtc.AddMinutes(1),
            CancellationToken.None);

        Assert.Equal(MultiSessionHistoricalReconstructionStatus.Available, result.Status);
        Assert.NotNull(result.Assessment);
        Assert.Equal(StructuralAssessmentCompleteness.Available, result.Assessment.Completeness.Status);
        Assert.Equal(
            new[] { D3.SessionId, D2.SessionId, D1.SessionId },
            result.Assessment.HistoricalSessionIds);
        Assert.NotEmpty(result.Assessment.SupportZones);
        Assert.NotEmpty(result.Assessment.ResistanceZones);
        Assert.Equal(MultiSessionStructuralTrendState.Bullish, result.Assessment.Trend.State);
        Assert.Equal(3, result.Sessions.Count);
        Assert.Equal(3, result.Accounting.CalendarRequests);
        Assert.Equal(3, result.Accounting.HistoricalRequests);
        Assert.Equal(6L, result.Accounting.PagesRetrieved);
        Assert.Equal(39L, result.Accounting.NormalizedRecordsRetrieved);
        Assert.Equal(6L, result.Accounting.DuplicateRecordsIgnored);
        Assert.Equal(1, result.Accounting.PeakRawSessionsRetained);
        Assert.Equal(9L, result.Accounting.PivotsProduced);
        Assert.Equal(9L, result.Accounting.CompactPivotRepresentationsRetained);
        Assert.Equal(9L, result.Accounting.PeakPivotRepresentationsRetained);
        Assert.True(result.Accounting.CompactPivotEstimatedBytes > 0);
        Assert.Equal(
            15L,
            result.Accounting.PeakGlobalClusteringWorkingSet);
        Assert.Equal(3, result.Accounting.RawSessionsReleased);
        Assert.Equal(new long[] { 13, 13, 13 }, result.Accounting.NormalizedRecordsPerSession);
        Assert.Equal(new[] { 3, 3, 3 }, result.Accounting.PivotsProducedPerSession);
        Assert.Equal(3, result.Accounting.CompactPivotBytesPerSession.Count);
        Assert.All(result.Accounting.CompactPivotBytesPerSession, bytes => Assert.True(bytes > 0));
        Assert.DoesNotContain(
            ExpandPivots(result.Sessions),
            item => item.ConfirmedAtEventTimeUtc > result.Assessment.AssessmentAsOfUtc);
    }

    [Theory]
    [InlineData("D1", "unavailable")]
    [InlineData("D2", "unavailable")]
    [InlineData("D3", "unavailable")]
    [InlineData("D1", "partial")]
    [InlineData("D2", "partial")]
    [InlineData("D3", "partial")]
    public async Task Any_unavailable_or_partial_required_session_prevents_assessment(
        string failedSessionId,
        string failureKind)
    {
        var source = new StubHistoricalSource((symbol, session, _) =>
        {
            if (session.SessionId == failedSessionId && failureKind == "unavailable")
            {
                return Task.FromResult<HistoricalMarketSessionData?>(null);
            }

            var data = CompleteData(session, SessionIndex(session.SessionId));
            if (session.SessionId == failedSessionId)
            {
                data = data with { DeclaresCompleteCoverage = false };
            }

            return Task.FromResult<HistoricalMarketSessionData?>(data);
        });
        var fixture = CreateFixture(source);

        var result = await Run(fixture.Reconstructor);

        Assert.NotEqual(MultiSessionHistoricalReconstructionStatus.Available, result.Status);
        Assert.NotNull(result.Assessment);
        Assert.NotEqual(StructuralAssessmentCompleteness.Available, result.Assessment.Completeness.Status);
        Assert.Empty(result.Assessment.SupportZones);
        Assert.Empty(result.Assessment.ResistanceZones);
        Assert.Equal(MultiSessionStructuralTrendState.Insufficient, result.Assessment.Trend.State);
        Assert.Equal(3, result.Assessment.HistoricalSessionIds.Count);
    }

    [Fact]
    public async Task Missing_middle_session_does_not_bridge_from_d1_to_d3()
    {
        var source = new StubHistoricalSource((symbol, session, _) =>
            Task.FromResult<HistoricalMarketSessionData?>(
                session.SessionId == D2.SessionId
                    ? null
                    : CompleteData(session, SessionIndex(session.SessionId))));
        var fixture = CreateFixture(source);

        var result = await Run(fixture.Reconstructor);

        Assert.NotEqual(MultiSessionHistoricalReconstructionStatus.Available, result.Status);
        Assert.NotNull(result.Assessment);
        Assert.Equal(
            new[] { D3.SessionId, D2.SessionId, D1.SessionId },
            result.Assessment.HistoricalSessionIds);
        Assert.Empty(result.Assessment.SupportZones);
        Assert.Empty(result.Assessment.ResistanceZones);
        Assert.Equal(MultiSessionStructuralTrendState.Insufficient, result.Assessment.Trend.State);
        Assert.Equal(new[] { D1.SessionId, D2.SessionId }, source.RequestedSessionIds);
    }

    [Fact]
    public async Task Missing_required_session_never_substitutes_an_older_available_session()
    {
        var older = Session("D4", CurrentDate.AddDays(-4));
        var calendar = CreateCalendar(missingD2: true);
        var source = new StubHistoricalSource((symbol, session, _) =>
            Task.FromResult<HistoricalMarketSessionData?>(
                session.SessionId == older.SessionId
                    ? CompleteData(session, 0)
                    : CompleteData(session, SessionIndex(session.SessionId))));
        var fixture = CreateFixture(source, calendar);

        var result = await Run(fixture.Reconstructor);

        Assert.NotEqual(MultiSessionHistoricalReconstructionStatus.Available, result.Status);
        Assert.Empty(source.RequestedSessionIds);
        Assert.NotNull(result.Assessment);
        Assert.Equal(new[] { D1.SessionId }, result.Assessment.HistoricalSessionIds);
        Assert.DoesNotContain(older.SessionId, result.Assessment.HistoricalSessionIds);
        Assert.Empty(result.Assessment.SupportZones);
        Assert.Empty(result.Assessment.ResistanceZones);
    }

    [Fact]
    public async Task Three_complete_but_noncontiguous_calendar_sessions_are_rejected()
    {
        var overlappingD2 = D2 with
        {
            OpenAtUtc = D1.OpenAtUtc.AddMinutes(-30),
            CloseAtUtc = D1.OpenAtUtc.AddMinutes(30)
        };
        var calendar = CreateCalendar(overlappingD2: overlappingD2);
        var source = new StubHistoricalSource((_, session, _) =>
            Task.FromResult<HistoricalMarketSessionData?>(CompleteData(session, 0)));
        var fixture = CreateFixture(source, calendar);

        var result = await Run(fixture.Reconstructor);

        Assert.Equal(MultiSessionHistoricalReconstructionStatus.Invalid, result.Status);
        Assert.NotNull(result.Assessment);
        Assert.NotEqual(StructuralAssessmentCompleteness.Available, result.Assessment.Completeness.Status);
        Assert.Empty(result.Assessment.SupportZones);
        Assert.Empty(result.Assessment.ResistanceZones);
        Assert.Empty(source.RequestedSessionIds);
    }

    [Fact]
    public async Task Incompatible_historical_provenance_invalidates_the_horizon()
    {
        var source = new StubHistoricalSource((_, session, _) =>
        {
            var data = CompleteData(session, SessionIndex(session.SessionId));
            return Task.FromResult<HistoricalMarketSessionData?>(
                session.SessionId == D2.SessionId
                    ? data with
                    {
                        Capabilities = data.Capabilities with { Provider = "testhistory" },
                        Provenance = data.Provenance! with { Provider = "testhistory" }
                    }
                    : data);
        });
        var fixture = CreateFixture(source);

        var result = await Run(fixture.Reconstructor);

        Assert.Equal(MultiSessionHistoricalReconstructionStatus.Invalid, result.Status);
        Assert.NotNull(result.Assessment);
        Assert.Equal(StructuralAssessmentCompleteness.Invalid, result.Assessment.Completeness.Status);
        Assert.False(result.Assessment.Completeness.HasUniformProvenance);
        Assert.Empty(result.Assessment.SupportZones);
        Assert.Empty(result.Assessment.ResistanceZones);
    }

    [Theory]
    [InlineData(-1, 2)]
    [InlineData(2, -1)]
    public async Task Invalid_resource_accounting_provenance_fails_closed(
        int pagesRetrieved,
        int duplicatesIgnored)
    {
        var source = new StubHistoricalSource((_, session, _) =>
        {
            var completeData = CompleteData(session, SessionIndex(session.SessionId));
            var data = completeData with
            {
                Provenance = completeData.Provenance! with
                {
                    PagesRetrieved = pagesRetrieved,
                    DuplicateRecordsIgnored = duplicatesIgnored
                }
            };
            return Task.FromResult<HistoricalMarketSessionData?>(data);
        });
        var fixture = CreateFixture(source);

        var result = await Run(fixture.Reconstructor);

        Assert.Equal(MultiSessionHistoricalReconstructionStatus.Invalid, result.Status);
        Assert.NotNull(result.Assessment);
        Assert.Equal(StructuralAssessmentCompleteness.Invalid, result.Assessment.Completeness.Status);
        Assert.Empty(result.Assessment.SupportZones);
        Assert.Empty(result.Assessment.ResistanceZones);
        Assert.Equal((long)Math.Max(0, pagesRetrieved), result.Accounting.PagesRetrieved);
        Assert.Equal((long)Math.Max(0, duplicatesIgnored), result.Accounting.DuplicateRecordsIgnored);
    }

    [Fact]
    public async Task Summary_pivots_match_the_existing_full_session_swing_calculation()
    {
        var source = new StubHistoricalSource((_, session, _) =>
            Task.FromResult<HistoricalMarketSessionData?>(
                CompleteData(session, SessionIndex(session.SessionId))));
        var fixture = CreateFixture(source);

        var result = await Run(fixture.Reconstructor);

        Assert.Equal(MultiSessionHistoricalReconstructionStatus.Available, result.Status);
        foreach (var session in result.Sessions)
        {
            var trades = CompleteData(
                    ChronologicalSessions.Single(item => item.SessionId == session.SessionId)
                        .ToMarketSession(),
                    SessionIndex(session.SessionId))
                .Events
                .OfType<TradeEvent>()
                .OrderBy(item => item.EventTimeUtc)
                .ThenBy(item => item.Provider, StringComparer.Ordinal)
                .ThenBy(item => item.SequenceId, StringComparer.Ordinal)
                .ThenBy(MarketEventIdentity.Create, StringComparer.Ordinal)
                .ToArray();
            var (highs, lows) = FeatureCalculator.CalculateConfirmedSwings(
                trades,
                "AAPL",
                session.SessionId,
                new ScannerConfiguration().FeatureSetVersion);
            var expected = highs
                .Select(item => ToStructuralPivot(item, StructuralRole.Resistance))
                .Concat(lows.Select(item => ToStructuralPivot(item, StructuralRole.Support)))
                .OrderBy(item => item.CandidateEventTimeUtc)
                .ThenBy(item => item.SourceSwingId)
                .ToArray();

            Assert.Empty(session.ConfirmablePivots);
            Assert.NotNull(session.CompactPivots);
            Assert.Single(session.CompactPivots.FeatureSetVersions);
            Assert.Single(session.CompactPivots.StructureAlgorithmVersions);
            Assert.Equal(expected, ExpandPivots(session));
        }

        var fullPivotSummaries = result.Sessions
            .Select(session => session with
            {
                ConfirmablePivots = ExpandPivots(session),
                CompactPivots = null
            })
            .ToArray();
        var expectedAssessment = new MultiSessionStructuralEvaluator().Evaluate(
            new StructuralAssessmentInput(
                "AAPL",
                CurrentSession.SessionId,
                D1.SessionId,
                CurrentSession.OpenAtUtc.AddMinutes(1),
                fullPivotSummaries),
            new ScannerConfiguration());
        Assert.Equivalent(expectedAssessment, result.Assessment);
        Assert.Equal(expectedAssessment.Id, result.Assessment?.Id);
    }

    [Fact]
    public async Task Replay_identity_excludes_retrieval_and_receive_times()
    {
        var original = CreateFixture();
        var shiftedSource = new StubHistoricalSource((_, session, _) =>
        {
            var data = CompleteData(session, SessionIndex(session.SessionId));
            return Task.FromResult<HistoricalMarketSessionData?>(data with
            {
                Events = data.Events
                    .OfType<TradeEvent>()
                    .Select(item => item with { ReceivedTimeUtc = item.ReceivedTimeUtc.AddHours(5) })
                    .Cast<MarketEvent>()
                    .ToArray(),
                Provenance = data.Provenance! with
                {
                    RetrievedAtUtc = data.Provenance.RetrievedAtUtc.AddHours(5)
                }
            });
        });
        var shifted = CreateFixture(shiftedSource);

        var first = await Run(original.Reconstructor);
        var replay = await Run(shifted.Reconstructor);

        Assert.Equal(first.Assessment?.Id, replay.Assessment?.Id);
        Assert.Equal(
            first.Sessions.Select(item =>
                (item.SessionId, item.High, item.Low, item.HighSourceEventIdentity, item.LowSourceEventIdentity)),
            replay.Sessions.Select(item =>
                (item.SessionId, item.High, item.Low, item.HighSourceEventIdentity, item.LowSourceEventIdentity)));
        Assert.Equal(
            ExpandPivots(first.Sessions),
            ExpandPivots(replay.Sessions));
    }

    [Fact]
    public async Task Whole_horizon_limits_stop_additional_retrieval_without_truncating_success()
    {
        var source = new StubHistoricalSource((_, session, _) =>
            Task.FromResult<HistoricalMarketSessionData?>(
                CompleteData(session, SessionIndex(session.SessionId))));
        var fixture = CreateFixture(
            source,
            options: new MultiSessionHistoricalReconstructionOptions(
                MaximumRecordsAcrossHorizon: 20));

        var result = await Run(fixture.Reconstructor);

        Assert.Equal(MultiSessionHistoricalReconstructionStatus.LimitExceeded, result.Status);
        Assert.NotNull(result.Assessment);
        Assert.NotEqual(StructuralAssessmentCompleteness.Available, result.Assessment.Completeness.Status);
        Assert.Empty(result.Assessment.SupportZones);
        Assert.Empty(result.Assessment.ResistanceZones);
        Assert.Equal(new[] { D1.SessionId, D2.SessionId }, source.RequestedSessionIds);
        Assert.Equal(2, result.Accounting.HistoricalRequests);
        Assert.Equal(26L, result.Accounting.NormalizedRecordsRetrieved);
        Assert.Equal(1, result.Accounting.PeakRawSessionsRetained);
    }

    [Fact]
    public async Task Per_session_pivot_bound_fails_closed_instead_of_truncating_evidence()
    {
        var fixture = CreateFixture(
            options: new MultiSessionHistoricalReconstructionOptions(
                MaximumPivotsPerSession: 1));

        var result = await Run(fixture.Reconstructor);

        Assert.Equal(MultiSessionHistoricalReconstructionStatus.LimitExceeded, result.Status);
        Assert.NotNull(result.Assessment);
        Assert.NotEqual(StructuralAssessmentCompleteness.Available, result.Assessment.Completeness.Status);
        Assert.Empty(result.Assessment.SupportZones);
        Assert.Empty(result.Assessment.ResistanceZones);
        Assert.Empty(result.Sessions.Single(item => item.SessionId == D1.SessionId).ConfirmablePivots);
    }

    [Theory]
    [InlineData(
        HistoricalRetrievalStatus.AuthenticationFailure,
        MultiSessionHistoricalReconstructionStatus.AuthenticationFailure)]
    [InlineData(
        HistoricalRetrievalStatus.EntitlementFailure,
        MultiSessionHistoricalReconstructionStatus.EntitlementFailure)]
    [InlineData(
        HistoricalRetrievalStatus.LimitExceeded,
        MultiSessionHistoricalReconstructionStatus.LimitExceeded)]
    [InlineData(
        HistoricalRetrievalStatus.Cancelled,
        MultiSessionHistoricalReconstructionStatus.Cancelled)]
    [InlineData(
        HistoricalRetrievalStatus.ProviderFailure,
        MultiSessionHistoricalReconstructionStatus.ProviderFailure)]
    [InlineData(
        HistoricalRetrievalStatus.MalformedResponse,
        MultiSessionHistoricalReconstructionStatus.Invalid)]
    public async Task Historical_retrieval_failures_remain_distinct_and_non_available(
        HistoricalRetrievalStatus retrievalStatus,
        MultiSessionHistoricalReconstructionStatus expectedStatus)
    {
        var source = new StubHistoricalSource((_, session, _) =>
        {
            var data = CompleteData(session, SessionIndex(session.SessionId)) with
            {
                RetrievalStatus = retrievalStatus,
                PaginationExhausted = false
            };
            return Task.FromResult<HistoricalMarketSessionData?>(data);
        });
        var fixture = CreateFixture(source);

        var result = await Run(fixture.Reconstructor);

        Assert.Equal(expectedStatus, result.Status);
        Assert.NotNull(result.Assessment);
        Assert.NotEqual(StructuralAssessmentCompleteness.Available, result.Assessment.Completeness.Status);
        Assert.Empty(result.Assessment.SupportZones);
        Assert.Empty(result.Assessment.ResistanceZones);
    }

    [Fact]
    public async Task Caller_cancellation_is_reported_and_never_produces_zones()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new StubHistoricalSource(async (_, _, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return null;
        });
        var fixture = CreateFixture(source);

        var pending = fixture.Reconstructor.ReconstructAsync(
            "AAPL",
            CurrentSession.SessionId,
            CurrentSession.OpenAtUtc.AddMinutes(1),
            cancellation.Token);
        await started.Task;
        cancellation.Cancel();
        var result = await pending;

        Assert.Equal(MultiSessionHistoricalReconstructionStatus.Cancelled, result.Status);
        Assert.NotNull(result.Assessment);
        Assert.Empty(result.Assessment.SupportZones);
        Assert.Empty(result.Assessment.ResistanceZones);
        Assert.Equal(MultiSessionStructuralTrendState.Insufficient, result.Assessment.Trend.State);
    }

    [Fact]
    public async Task Cancellation_after_a_response_accounts_for_the_single_raw_session()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new StubHistoricalSource((_, session, _) =>
        {
            var data = CompleteData(session, SessionIndex(session.SessionId));
            cancellation.Cancel();
            return Task.FromResult<HistoricalMarketSessionData?>(data);
        });
        var fixture = CreateFixture(source);

        var result = await fixture.Reconstructor.ReconstructAsync(
            "AAPL",
            CurrentSession.SessionId,
            CurrentSession.OpenAtUtc.AddMinutes(1),
            cancellation.Token);

        Assert.Equal(MultiSessionHistoricalReconstructionStatus.Cancelled, result.Status);
        Assert.Equal(1, result.Accounting.HistoricalRequests);
        Assert.Equal(2L, result.Accounting.PagesRetrieved);
        Assert.Equal(13L, result.Accounting.NormalizedRecordsRetrieved);
        Assert.Equal(2L, result.Accounting.DuplicateRecordsIgnored);
        Assert.Equal(1, result.Accounting.PeakRawSessionsRetained);
        Assert.NotNull(result.Assessment);
        Assert.Empty(result.Assessment.SupportZones);
        Assert.Empty(result.Assessment.ResistanceZones);
        Assert.Equal(MultiSessionStructuralTrendState.Insufficient, result.Assessment.Trend.State);
    }

    [Fact]
    public async Task Cancellation_after_second_session_releases_both_raw_sessions()
    {
        using var cancellation = new CancellationTokenSource();
        var requests = 0;
        var source = new StubHistoricalSource((_, session, _) =>
        {
            var data = CompleteData(session, SessionIndex(session.SessionId));
            requests++;
            if (requests == 2)
            {
                cancellation.Cancel();
            }

            return Task.FromResult<HistoricalMarketSessionData?>(data);
        });
        var fixture = CreateFixture(source);

        var result = await fixture.Reconstructor.ReconstructAsync(
            "AAPL",
            CurrentSession.SessionId,
            CurrentSession.OpenAtUtc.AddMinutes(1),
            cancellation.Token);

        Assert.Equal(MultiSessionHistoricalReconstructionStatus.Cancelled, result.Status);
        Assert.Equal(new[] { D1.SessionId, D2.SessionId }, source.RequestedSessionIds);
        Assert.Equal(2, result.Accounting.HistoricalRequests);
        Assert.Equal(2, result.Accounting.RawSessionsReleased);
        Assert.Equal(1, result.Accounting.PeakRawSessionsRetained);
        Assert.Empty(result.Assessment?.SupportZones ?? []);
        Assert.Empty(result.Assessment?.ResistanceZones ?? []);
    }

    [Fact]
    public async Task Historical_request_timeout_stops_the_horizon_without_producing_zones()
    {
        var source = new StubHistoricalSource((_, _, _) =>
            Task.FromException<HistoricalMarketSessionData?>(new TimeoutException()));
        var fixture = CreateFixture(source);

        var result = await Run(fixture.Reconstructor);

        Assert.Equal(MultiSessionHistoricalReconstructionStatus.TimedOut, result.Status);
        Assert.Equal(new[] { D1.SessionId }, source.RequestedSessionIds);
        Assert.NotNull(result.Assessment);
        Assert.Empty(result.Assessment.SupportZones);
        Assert.Empty(result.Assessment.ResistanceZones);
        Assert.Equal(MultiSessionStructuralTrendState.Insufficient, result.Assessment.Trend.State);
    }

    [Fact]
    public async Task Whole_horizon_timeout_is_distinct_from_caller_cancellation()
    {
        var source = new StubHistoricalSource(async (_, _, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return null;
        });
        var fixture = CreateFixture(
            source,
            options: new MultiSessionHistoricalReconstructionOptions(TimeoutSeconds: 1));

        var result = await Run(fixture.Reconstructor);

        Assert.Equal(MultiSessionHistoricalReconstructionStatus.TimedOut, result.Status);
        Assert.NotNull(result.Assessment);
        Assert.Empty(result.Assessment.SupportZones);
        Assert.Empty(result.Assessment.ResistanceZones);
    }

    [Fact]
    public async Task Reconstruction_failures_leave_prior_session_context_behavior_unchanged()
    {
        var source = new StubHistoricalSource((_, session, _) =>
            Task.FromResult<HistoricalMarketSessionData?>(
                session.SessionId == D1.SessionId
                    ? CompleteData(session, 2) with { DeclaresCompleteCoverage = false }
                    : CompleteData(session, 0)));
        var configuration = new ScannerConfiguration();
        var fixture = CreateFixture(source, configuration: configuration);

        var result = await Run(fixture.Reconstructor);
        var priorContext = new PriorSessionHistoricalReconstructor(
                new PriorSessionContextBuilder(new FeatureCalculator()))
            .Reconstruct(
                "AAPL",
                CurrentSession.SessionId,
                D1,
                CompleteData(D1, 2),
                Requirements,
                configuration);

        Assert.NotEqual(MultiSessionHistoricalReconstructionStatus.Available, result.Status);
        Assert.NotNull(result.Assessment);
        Assert.Empty(result.Assessment.SupportZones);
        Assert.Empty(result.Assessment.ResistanceZones);
        Assert.Equal(PriorSessionContextStatus.Available, priorContext.Completeness);
        Assert.Equal("scanner-config-v20", configuration.Version);
    }

    [Fact]
    public async Task Calendar_provider_failure_is_reported_without_historical_retrieval()
    {
        var calendar = new StubCalendar(new Dictionary<string, MarketSessionCalendarResolution>
        {
            [CurrentSession.SessionId] = Resolution(CurrentSession, D1),
            [D1.SessionId] = new(
                D1,
                null,
                "test-calendar-v1",
                "Calendar provider is unavailable.",
                MarketSessionCalendarStatus.ProviderFailure)
        });
        var source = new StubHistoricalSource((_, session, _) =>
            Task.FromResult<HistoricalMarketSessionData?>(CompleteData(session, 0)));
        var fixture = CreateFixture(source, calendar);

        var result = await Run(fixture.Reconstructor);

        Assert.Equal(MultiSessionHistoricalReconstructionStatus.ProviderFailure, result.Status);
        Assert.Empty(source.RequestedSessionIds);
        Assert.NotNull(result.Assessment);
        Assert.Equal(new[] { D1.SessionId }, result.Assessment.HistoricalSessionIds);
        Assert.Empty(result.Assessment.SupportZones);
        Assert.Empty(result.Assessment.ResistanceZones);
    }

    [Fact]
    public void Alpaca_requirements_cannot_select_non_sip_history()
    {
        Assert.Throws<ArgumentException>(() => new MultiSessionHistoricalReconstructor(
            CreateCalendar(),
            new StubHistoricalSource((_, _, _) => Task.FromResult<HistoricalMarketSessionData?>(null)),
            new PriorSessionHistoricalReconstructor(new PriorSessionContextBuilder(new FeatureCalculator())),
            new MultiSessionStructuralEvaluator(),
            new HistoricalReconstructionRequirements("Alpaca", "iex", "historical-rest"),
            new ScannerConfiguration()));
    }

    private static async Task<MultiSessionHistoricalReconstructionResult> Run(
        MultiSessionHistoricalReconstructor reconstructor) =>
        await reconstructor.ReconstructAsync(
            "AAPL",
            CurrentSession.SessionId,
            CurrentSession.OpenAtUtc.AddMinutes(1),
            CancellationToken.None);

    private static Fixture CreateFixture(
        StubHistoricalSource? source = null,
        StubCalendar? calendar = null,
        MultiSessionHistoricalReconstructionOptions? options = null,
        ScannerConfiguration? configuration = null)
    {
        var scannerConfiguration = configuration ?? new ScannerConfiguration();
        source ??= new StubHistoricalSource((_, session, _) =>
            Task.FromResult<HistoricalMarketSessionData?>(
                CompleteData(session, SessionIndex(session.SessionId))));
        var reconstructor = new MultiSessionHistoricalReconstructor(
            calendar ?? CreateCalendar(),
            source,
            new PriorSessionHistoricalReconstructor(
                new PriorSessionContextBuilder(new FeatureCalculator())),
            new MultiSessionStructuralEvaluator(),
            Requirements,
            scannerConfiguration,
            options);
        return new Fixture(reconstructor, source);
    }

    private static StubCalendar CreateCalendar(
        MarketSessionDefinition? overlappingD2 = null,
        bool missingD2 = false)
    {
        var d2 = overlappingD2 ?? D2;
        return new StubCalendar(new Dictionary<string, MarketSessionCalendarResolution>
        {
            [CurrentSession.SessionId] = Resolution(CurrentSession, D1),
            [D1.SessionId] = Resolution(D1, missingD2 ? null : d2),
            [D2.SessionId] = Resolution(d2, D3)
        });
    }

    private static MarketSessionCalendarResolution Resolution(
        MarketSessionDefinition current,
        MarketSessionDefinition? previous) =>
        new(
            current,
            previous,
            "test-calendar-v1",
            previous is null ? "No previous session." : null,
            previous is null
                ? MarketSessionCalendarStatus.NoPreviousSession
                : MarketSessionCalendarStatus.Success);

    private static HistoricalMarketSessionData CompleteData(
        MarketSession session,
        int sessionIndex,
        int pages = 2,
        int duplicates = 2)
    {
        decimal offset = sessionIndex * 10m;
        decimal[] prices =
        [
            100m + offset, 99m + offset, 98m + offset, 100m + offset,
            102m + offset, 101m + offset, 100m + offset, 98m + offset,
            99m + offset, 101m + offset, 100m + offset, 102m + offset,
            100m + offset
        ];
        var events = prices.Select((price, index) =>
            (MarketEvent)new TradeEvent(
                "AAPL",
                session.SessionId,
                session.StartUtc.AddSeconds(index + 1),
                RetrievedAtUtc,
                Requirements.Provider,
                $"{session.SessionId}-trade-{index}",
                price,
                100)).ToArray();
        return new HistoricalMarketSessionData(
            events,
            new MarketDataCapabilities(
                Requirements.Provider,
                Requirements.Feed,
                MarketDataCoverage.Consolidated,
                false,
                true,
                false),
            Requirements.SourceKind,
            session.StartUtc,
            session.EndUtc,
            true,
            false,
            new HistoricalDataRequest(
                "AAPL",
                session.SessionId,
                session.StartUtc,
                session.EndUtc),
            HistoricalRetrievalStatus.SuccessPaginationExhausted,
            new HistoricalDataProvenance(
                Requirements.Provider,
                Requirements.Feed,
                Requirements.SourceKind,
                RetrievedAtUtc,
                pages,
                duplicates),
            true);
    }

    private static HistoricalMarketSessionData CompleteData(
        MarketSessionDefinition session,
        int sessionIndex,
        int pages = 2,
        int duplicates = 2) =>
        CompleteData(session.ToMarketSession(), sessionIndex, pages, duplicates);

    private static int SessionIndex(string sessionId) =>
        sessionId switch
        {
            "D1" => 2,
            "D2" => 1,
            "D3" => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(sessionId))
        };

    private static StructuralPivotCandidate ToStructuralPivot(
        ConfirmedSwing swing,
        StructuralRole role) =>
        new(
            swing.Id,
            swing.Symbol,
            swing.SessionId,
            role,
            swing.Price,
            swing.EventTimeUtc,
            swing.ConfirmedAtEventTimeUtc,
            swing.OriginEventIdentity,
            swing.ConfirmationEventIdentity,
            swing.FeatureSetVersion,
            swing.AlgorithmVersion);

    private static StructuralPivotCandidate[] ExpandPivots(
        IEnumerable<StructuralSessionSummary> sessions) =>
        sessions.SelectMany(ExpandPivots).ToArray();

    private static StructuralPivotCandidate[] ExpandPivots(
        StructuralSessionSummary session)
    {
        if (session.CompactPivots is null)
        {
            return session.ConfirmablePivots.ToArray();
        }

        return session.CompactPivots.Pivots
            .Select(item => new StructuralPivotCandidate(
                item.SourceSwingId,
                session.Symbol,
                session.SessionId,
                item.Role,
                item.Price,
                item.CandidateEventTimeUtc,
                item.ConfirmedAtEventTimeUtc,
                item.OriginEventIdentity,
                item.ConfirmationEventIdentity,
                session.CompactPivots.FeatureSetVersions[item.FeatureSetVersionIndex],
                session.CompactPivots.StructureAlgorithmVersions[item.StructureAlgorithmVersionIndex]))
            .ToArray();
    }

    private static MarketSessionDefinition Session(string id, DateOnly date) =>
        new(
            id,
            date,
            new DateTimeOffset(date, new TimeOnly(14, 30), TimeSpan.Zero),
            new DateTimeOffset(date, new TimeOnly(21, 0), TimeSpan.Zero),
            Requirements.Provider,
            "calendar-test",
            "calendar-v1",
            MarketSessionType.Regular,
            MarketSessionValidity.Valid);

    private static MarketSessionDefinition[] ChronologicalSessions => [D3, D2, D1];

    private sealed record Fixture(
        MultiSessionHistoricalReconstructor Reconstructor,
        StubHistoricalSource Source);

    private sealed class StubCalendar(
        IReadOnlyDictionary<string, MarketSessionCalendarResolution> resolutions)
        : IMarketSessionCalendar
    {
        public Task<MarketSessionCalendarResolution> ResolvePreviousSessionAsync(
            string symbol,
            string currentSessionId,
            DateTimeOffset currentEventTimeUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(resolutions.TryGetValue(currentSessionId, out var resolution)
                ? resolution
                : new MarketSessionCalendarResolution(
                    null,
                    null,
                    "test-calendar-v1",
                    $"No calendar entry for '{currentSessionId}'.",
                    MarketSessionCalendarStatus.InvalidReference));
        }
    }

    private sealed class StubHistoricalSource(
        Func<string, MarketSession, CancellationToken, Task<HistoricalMarketSessionData?>> readSession)
        : IHistoricalMarketDataSource
    {
        public List<string> RequestedSessionIds { get; } = [];

        public Task<HistoricalMarketSessionData?> ReadSessionAsync(
            string symbol,
            MarketSession session,
            CancellationToken cancellationToken)
        {
            RequestedSessionIds.Add(session.SessionId);
            return readSession(symbol, session, cancellationToken);
        }
    }
}
