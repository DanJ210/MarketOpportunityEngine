using System.Net.Http;
using MarketOpportunity.Analytics;
using MarketOpportunity.Domain;

namespace MarketOpportunity.Application;

public enum MultiSessionHistoricalReconstructionStatus
{
    Available,
    Insufficient,
    Partial,
    Unavailable,
    Invalid,
    Cancelled,
    AuthenticationFailure,
    EntitlementFailure,
    LimitExceeded,
    TimedOut,
    ProviderFailure,
    ReconstructionFailure
}

public sealed record MultiSessionHistoricalReconstructionOptions(
    int TimeoutSeconds = 120,
    int MaximumPagesPerSession = 100,
    int MaximumRecordsPerSession = 1_000_000,
    long MaximumPagesAcrossHorizon = 300,
    long MaximumRecordsAcrossHorizon = 3_000_000,
    int MaximumPivotsPerSession = 1_000_000)
{
    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);

    public void Validate()
    {
        if (TimeoutSeconds is < 1 or > 300
            || MaximumPagesPerSession < 1
            || MaximumRecordsPerSession < 1
            || MaximumPagesAcrossHorizon < 1
            || MaximumRecordsAcrossHorizon < 1
            || MaximumPivotsPerSession < 1)
        {
            throw new InvalidOperationException(
                "Multi-session historical reconstruction limits must be positive.");
        }
    }
}

public sealed record MultiSessionHistoricalReconstructionAccounting(
    int CalendarRequests,
    int HistoricalRequests,
    long PagesRetrieved,
    long NormalizedRecordsRetrieved,
    long DuplicateRecordsIgnored,
    int PeakRawSessionsRetained,
    long PivotsProduced,
    long CompactPivotRepresentationsRetained,
    long PeakPivotRepresentationsRetained,
    long CompactPivotEstimatedBytes,
    long PeakGlobalClusteringWorkingSet,
    int RawSessionsReleased,
    IReadOnlyList<long> NormalizedRecordsPerSession,
    IReadOnlyList<int> PivotsProducedPerSession,
    IReadOnlyList<long> CompactPivotBytesPerSession);

public sealed record MultiSessionHistoricalReconstructionResult(
    MultiSessionHistoricalReconstructionStatus Status,
    string Reason,
    string? CalendarId,
    IReadOnlyList<StructuralSessionSummary> Sessions,
    MultiSessionStructuralAssessment? Assessment,
    MultiSessionHistoricalReconstructionAccounting Accounting);

public sealed class MultiSessionHistoricalReconstructor
{
    private readonly IMarketSessionCalendar _calendar;
    private readonly IHistoricalMarketDataSource _historicalSource;
    private readonly PriorSessionHistoricalReconstructor _sessionReconstructor;
    private readonly MultiSessionStructuralEvaluator _structuralEvaluator;
    private readonly HistoricalReconstructionRequirements _requirements;
    private readonly ScannerConfiguration _configuration;
    private readonly MultiSessionHistoricalReconstructionOptions _options;
    private readonly TimeProvider _timeProvider;

    public MultiSessionHistoricalReconstructor(
        IMarketSessionCalendar calendar,
        IHistoricalMarketDataSource historicalSource,
        PriorSessionHistoricalReconstructor sessionReconstructor,
        MultiSessionStructuralEvaluator structuralEvaluator,
        HistoricalReconstructionRequirements requirements,
        ScannerConfiguration configuration,
        MultiSessionHistoricalReconstructionOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        _historicalSource = historicalSource ?? throw new ArgumentNullException(nameof(historicalSource));
        _sessionReconstructor = sessionReconstructor
            ?? throw new ArgumentNullException(nameof(sessionReconstructor));
        _structuralEvaluator = structuralEvaluator
            ?? throw new ArgumentNullException(nameof(structuralEvaluator));
        _requirements = requirements ?? throw new ArgumentNullException(nameof(requirements));
        ArgumentException.ThrowIfNullOrWhiteSpace(_requirements.Provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(_requirements.Feed);
        ArgumentException.ThrowIfNullOrWhiteSpace(_requirements.SourceKind);
        if (_requirements.Provider.Equals("Alpaca", StringComparison.OrdinalIgnoreCase)
            && (!_requirements.Feed.Equals("sip", StringComparison.OrdinalIgnoreCase)
                || !_requirements.SourceKind.Equals("historical-rest", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "Alpaca multi-session history requires the approved SIP historical REST source.",
                nameof(requirements));
        }

        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _configuration.Validate();
        _options = options ?? new MultiSessionHistoricalReconstructionOptions();
        _options.Validate();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<MultiSessionHistoricalReconstructionResult> ReconstructAsync(
        string symbol,
        string currentSessionId,
        DateTimeOffset assessmentAsOfUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentSessionId);
        symbol = symbol.Trim().ToUpperInvariant();
        var accounting = new AccountingBuilder();
        var knownSessions = new List<MarketSessionDefinition>();
        string? calendarId = null;
        using var timeout = new CancellationTokenSource(_options.Timeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);

        try
        {
            var horizon = await ResolveHorizonAsync(
                symbol,
                currentSessionId,
                assessmentAsOfUtc,
                linked.Token,
                accounting);
            calendarId = horizon.CalendarId;
            knownSessions.AddRange(horizon.SessionsNewestFirst);
            if (horizon.Failure is { } calendarFailure)
            {
                return CreateResult(
                    symbol,
                    currentSessionId,
                    assessmentAsOfUtc,
                    CreatePendingSummaries(knownSessions.AsEnumerable().Reverse().ToArray()),
                    accounting,
                    calendarId,
                    calendarFailure.Status,
                    calendarFailure.Reason);
            }

            var historicalSessions = horizon.SessionsNewestFirst
                .Reverse()
                .ToArray();
            var summaries = CreatePendingSummaries(historicalSessions);
            foreach (var session in horizon.SessionsNewestFirst)
            {
                linked.Token.ThrowIfCancellationRequested();
                var sessionResult = await ReadAndReduceSessionAsync(
                    symbol,
                    currentSessionId,
                    session,
                    accounting,
                    linked.Token);
                var summaryIndex = Array.FindIndex(
                    historicalSessions,
                    item => item.SessionId.Equals(session.SessionId, StringComparison.Ordinal));
                summaries[summaryIndex] = sessionResult.Summary with
                {
                    PreviousSessionId = summaryIndex == 0
                        ? null
                        : historicalSessions[summaryIndex - 1].SessionId
                };
                if (sessionResult.Failure is { } historyFailure)
                {
                    return CreateResult(
                        symbol,
                        currentSessionId,
                        assessmentAsOfUtc,
                        summaries,
                        accounting,
                        calendarId,
                        historyFailure.Status,
                        historyFailure.Reason);
                }
            }

            linked.Token.ThrowIfCancellationRequested();
            return CreateResult(
                symbol,
                currentSessionId,
                assessmentAsOfUtc,
                summaries,
                accounting,
                calendarId,
                cancellationToken: linked.Token);
        }
        catch (OperationCanceledException)
        {
            var timedOut = !cancellationToken.IsCancellationRequested
                && timeout.IsCancellationRequested;
            var status = timedOut
                ? MultiSessionHistoricalReconstructionStatus.TimedOut
                : MultiSessionHistoricalReconstructionStatus.Cancelled;
            var reason = timedOut
                ? $"Multi-session historical reconstruction timed out after {_options.TimeoutSeconds} seconds."
                : "Multi-session historical reconstruction was cancelled.";
            var summaries = knownSessions.Count > 0
                ? CreatePendingSummaries(knownSessions.AsEnumerable().Reverse().ToArray())
                : [];
            return CreateResult(
                symbol,
                currentSessionId,
                assessmentAsOfUtc,
                summaries,
                accounting,
                calendarId,
                status,
                reason);
        }
    }

    private async Task<CalendarHorizonResult> ResolveHorizonAsync(
        string symbol,
        string currentSessionId,
        DateTimeOffset assessmentAsOfUtc,
        CancellationToken cancellationToken,
        AccountingBuilder accounting)
    {
        var sessionsNewestFirst = new List<MarketSessionDefinition>();
        var requestedCurrentSessionId = currentSessionId;
        var currentEventTimeUtc = assessmentAsOfUtc;
        MarketSessionDefinition? expectedCurrent = null;
        string? resolvedCalendarId = null;

        for (var index = 0; index < _configuration.MultiSessionStructure.HistoricalSessionCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            accounting.CalendarRequests++;
            MarketSessionCalendarResolution resolution;
            try
            {
                resolution = await _calendar.ResolvePreviousSessionAsync(
                    symbol,
                    requestedCurrentSessionId,
                    currentEventTimeUtc,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException)
            {
                return new CalendarHorizonResult(
                    sessionsNewestFirst,
                    resolvedCalendarId,
                    new ReconstructionFailure(
                        MultiSessionHistoricalReconstructionStatus.ProviderFailure,
                        $"Session-calendar resolution failed ({exception.GetType().Name})."));
            }
            catch (TimeoutException)
            {
                return new CalendarHorizonResult(
                    sessionsNewestFirst,
                    resolvedCalendarId,
                    new ReconstructionFailure(
                        MultiSessionHistoricalReconstructionStatus.TimedOut,
                        "Session-calendar resolution timed out."));
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (resolution.Status != MarketSessionCalendarStatus.Success)
            {
                return new CalendarHorizonResult(
                    sessionsNewestFirst,
                    resolvedCalendarId ?? resolution.CalendarId,
                    new ReconstructionFailure(
                        MapCalendarStatus(resolution.Status),
                        resolution.Message ?? "The required preceding regular session could not be resolved."));
            }

            if (string.IsNullOrWhiteSpace(resolution.CalendarId)
                || (resolvedCalendarId is not null
                    && !resolvedCalendarId.Equals(resolution.CalendarId, StringComparison.Ordinal))
                || resolution.CurrentSession is not { } current
                || resolution.PreviousSession is not { } previous
                || !IsUsableCurrentSession(current, requestedCurrentSessionId, currentEventTimeUtc)
                || (expectedCurrent is not null && !SameSession(current, expectedCurrent))
                || !IsUsablePreviousSession(current, previous)
                || !HasCompatibleCalendarProvenance(current, previous))
            {
                return new CalendarHorizonResult(
                    sessionsNewestFirst,
                    resolvedCalendarId ?? resolution.CalendarId,
                    new ReconstructionFailure(
                        MultiSessionHistoricalReconstructionStatus.Invalid,
                        "The calendar returned an invalid, noncontiguous, or incompatible regular-session chain."));
            }

            resolvedCalendarId ??= resolution.CalendarId;
            sessionsNewestFirst.Add(previous);
            expectedCurrent = previous;
            requestedCurrentSessionId = previous.SessionId;
            currentEventTimeUtc = previous.CloseAtUtc;
        }

        return new CalendarHorizonResult(sessionsNewestFirst, resolvedCalendarId, null);
    }

    private async Task<SessionReductionResult> ReadAndReduceSessionAsync(
        string symbol,
        string currentSessionId,
        MarketSessionDefinition session,
        AccountingBuilder accounting,
        CancellationToken cancellationToken)
    {
        accounting.HistoricalRequests++;
        HistoricalMarketSessionData? historicalData;
        try
        {
            historicalData = await _historicalSource.ReadSessionAsync(
                symbol,
                session.ToMarketSession(),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            return FailedSession(
                symbol,
                session,
                StructuralSessionCompleteness.Insufficient,
                MultiSessionHistoricalReconstructionStatus.ProviderFailure,
                $"Historical session retrieval failed ({exception.GetType().Name}).");
        }
        catch (TimeoutException)
        {
            return FailedSession(
                symbol,
                session,
                StructuralSessionCompleteness.Insufficient,
                MultiSessionHistoricalReconstructionStatus.TimedOut,
                "Historical session retrieval timed out.");
        }

        if (historicalData is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return FailedSession(
                symbol,
                session,
                StructuralSessionCompleteness.Insufficient,
                MultiSessionHistoricalReconstructionStatus.Unavailable,
                $"No historical data was returned for required session '{session.SessionId}'.");
        }

        accounting.RawSessionStarted();
        try
        {
            accounting.AddHistoricalData(historicalData);
            cancellationToken.ThrowIfCancellationRequested();
            if (historicalData.Events is null
                || historicalData.Capabilities is null
                || historicalData.Provenance is null
                || string.IsNullOrWhiteSpace(historicalData.Source)
                || string.IsNullOrWhiteSpace(historicalData.Capabilities.Provider)
                || string.IsNullOrWhiteSpace(historicalData.Capabilities.Feed)
                || string.IsNullOrWhiteSpace(historicalData.Provenance.Provider)
                || string.IsNullOrWhiteSpace(historicalData.Provenance.Feed)
                || string.IsNullOrWhiteSpace(historicalData.Provenance.SourceKind)
                || historicalData.Provenance.PagesRetrieved < 0
                || historicalData.Provenance.DuplicateRecordsIgnored < 0)
            {
                return FailedSession(
                    symbol,
                    session,
                    StructuralSessionCompleteness.Invalid,
                    MultiSessionHistoricalReconstructionStatus.Invalid,
                    "Historical retrieval omitted required events or capability provenance.");
            }

            if (historicalData.Events.Count > _options.MaximumRecordsPerSession
                || (historicalData.Provenance?.PagesRetrieved ?? 0) > _options.MaximumPagesPerSession
                || accounting.NormalizedRecordsRetrieved > _options.MaximumRecordsAcrossHorizon
                || accounting.PagesRetrieved > _options.MaximumPagesAcrossHorizon)
            {
                return FailedSession(
                    symbol,
                    session,
                    historicalData.Events.Count == 0
                        ? StructuralSessionCompleteness.Insufficient
                        : StructuralSessionCompleteness.Partial,
                    MultiSessionHistoricalReconstructionStatus.LimitExceeded,
                    "The historical response exceeded an explicit per-session or whole-horizon resource limit.");
            }

            PriorSessionContext context;
            try
            {
                context = _sessionReconstructor.Reconstruct(
                    symbol,
                    currentSessionId,
                    session,
                    historicalData,
                    _requirements,
                    _configuration);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is ArgumentException or InvalidOperationException or InvalidDataException)
            {
                return FailedSession(
                    symbol,
                    session,
                    StructuralSessionCompleteness.Invalid,
                    MultiSessionHistoricalReconstructionStatus.ReconstructionFailure,
                    $"Historical session reconstruction failed ({exception.GetType().Name}).");
            }

            var status = MapContextStatus(context.Completeness);
            if (status != MultiSessionHistoricalReconstructionStatus.Available)
            {
                return FailedSession(
                    symbol,
                    session,
                    MapSessionCompleteness(context.Completeness),
                    MapRetrievalStatus(historicalData.RetrievalStatus, status),
                    context.CompletenessReason);
            }

            if (context.PriorSessionHigh is not { } high
                || context.PriorSessionLow is not { } low
                || FindExtremeTrade(historicalData.Events, symbol, session, high) is not { } highTrade
                || FindExtremeTrade(historicalData.Events, symbol, session, low) is not { } lowTrade)
            {
                return FailedSession(
                    symbol,
                    session,
                    StructuralSessionCompleteness.Invalid,
                    MultiSessionHistoricalReconstructionStatus.ReconstructionFailure,
                    "The complete reconstructed session is missing deterministic extrema provenance.");
            }

            if (context.SupportCandidates.Count + context.ResistanceCandidates.Count
                > _options.MaximumPivotsPerSession)
            {
                return FailedSession(
                    symbol,
                    session,
                    StructuralSessionCompleteness.Insufficient,
                    MultiSessionHistoricalReconstructionStatus.LimitExceeded,
                    "The reconstructed pivot summary exceeded its explicit per-session bound.");
            }

            var compactPivots = ToCompactPivots(context);
            accounting.AddCompactPivots(compactPivots);
            var summary = new StructuralSessionSummary(
                symbol,
                session.SessionId,
                null,
                session.OpenAtUtc,
                session.CloseAtUtc,
                StructuralSessionCompleteness.Available,
                historicalData.DeclaresCompleteCoverage,
                historicalData.HasKnownGaps,
                high,
                low,
                MarketEventIdentity.Create(highTrade),
                MarketEventIdentity.Create(lowTrade),
                highTrade.EventTimeUtc,
                lowTrade.EventTimeUtc,
                session.CloseAtUtc,
                historicalData.Capabilities.Provider,
                historicalData.Capabilities.Feed,
                historicalData.Source,
                [],
                compactPivots);
            return new SessionReductionResult(summary, null);
        }
        finally
        {
            accounting.RawSessionFinished();
        }
    }

    private SessionReductionResult FailedSession(
        string symbol,
        MarketSessionDefinition session,
        StructuralSessionCompleteness completeness,
        MultiSessionHistoricalReconstructionStatus status,
        string reason) =>
        new(
            new StructuralSessionSummary(
                symbol,
                session.SessionId,
                null,
                session.OpenAtUtc,
                session.CloseAtUtc,
                completeness,
                false,
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                _requirements.Provider,
                _requirements.Feed,
                _requirements.SourceKind,
                []),
            new ReconstructionFailure(status, reason));

    private MultiSessionHistoricalReconstructionResult CreateResult(
        string symbol,
        string currentSessionId,
        DateTimeOffset assessmentAsOfUtc,
        IReadOnlyList<StructuralSessionSummary> summaries,
        AccountingBuilder accounting,
        string? calendarId,
        MultiSessionHistoricalReconstructionStatus? terminalStatus = null,
        string? terminalReason = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedSummaries = summaries
            .Select(item => item.Symbol.Length == 0 ? item with { Symbol = symbol } : item)
            .ToArray();
        var assessment = normalizedSummaries.Length == 0
            ? null
            : _structuralEvaluator.Evaluate(
                new StructuralAssessmentInput(
                    symbol,
                    currentSessionId,
                    normalizedSummaries[^1].SessionId,
                    assessmentAsOfUtc,
                    normalizedSummaries),
                _configuration,
                cancellationToken);
        var status = terminalStatus
            ?? (assessment is null
                ? MultiSessionHistoricalReconstructionStatus.Insufficient
                : MapAssessmentStatus(assessment.Completeness.Status));
        return new MultiSessionHistoricalReconstructionResult(
            status,
            terminalReason ?? assessment?.Completeness.Reason ?? "The required prior sessions could not be resolved.",
            calendarId,
            normalizedSummaries,
            assessment,
            accounting.Build());
    }

    private StructuralSessionSummary[] CreatePendingSummaries(
        IReadOnlyList<MarketSessionDefinition> sessionsChronological)
    {
        var summaries = new StructuralSessionSummary[sessionsChronological.Count];
        for (var index = 0; index < sessionsChronological.Count; index++)
        {
            var session = sessionsChronological[index];
            summaries[index] = new StructuralSessionSummary(
                string.Empty,
                session.SessionId,
                index == 0 ? null : sessionsChronological[index - 1].SessionId,
                session.OpenAtUtc,
                session.CloseAtUtc,
                StructuralSessionCompleteness.Insufficient,
                false,
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                _requirements.Provider,
                _requirements.Feed,
                _requirements.SourceKind,
                []);
        }

        return summaries;
    }

    private bool IsUsableCurrentSession(
        MarketSessionDefinition session,
        string requestedSessionId,
        DateTimeOffset eventTimeUtc) =>
        session.SessionId.Equals(requestedSessionId, StringComparison.Ordinal)
        && session.Validity == MarketSessionValidity.Valid
        && session.SessionType == MarketSessionType.Regular
        && session.OpenAtUtc < session.CloseAtUtc
        && eventTimeUtc >= session.OpenAtUtc
        && eventTimeUtc <= session.CloseAtUtc;

    private bool IsUsablePreviousSession(
        MarketSessionDefinition current,
        MarketSessionDefinition previous) =>
        previous.Validity == MarketSessionValidity.Valid
        && previous.SessionType == MarketSessionType.Regular
        && previous.OpenAtUtc < previous.CloseAtUtc
        && previous.TradingDate < current.TradingDate
        && previous.CloseAtUtc <= current.OpenAtUtc;

    private bool HasCompatibleCalendarProvenance(
        MarketSessionDefinition current,
        MarketSessionDefinition previous) =>
        current.Provider.Equals(_requirements.Provider, StringComparison.OrdinalIgnoreCase)
        && previous.Provider.Equals(_requirements.Provider, StringComparison.OrdinalIgnoreCase)
        && current.SourceKind.Equals(previous.SourceKind, StringComparison.Ordinal)
        && current.CalendarVersion.Equals(previous.CalendarVersion, StringComparison.Ordinal);

    private static bool SameSession(
        MarketSessionDefinition actual,
        MarketSessionDefinition expected) =>
        actual.SessionId.Equals(expected.SessionId, StringComparison.Ordinal)
        && actual.TradingDate == expected.TradingDate
        && actual.OpenAtUtc.ToUniversalTime() == expected.OpenAtUtc.ToUniversalTime()
        && actual.CloseAtUtc.ToUniversalTime() == expected.CloseAtUtc.ToUniversalTime()
        && actual.Provider.Equals(expected.Provider, StringComparison.Ordinal)
        && actual.SourceKind.Equals(expected.SourceKind, StringComparison.Ordinal)
        && actual.CalendarVersion.Equals(expected.CalendarVersion, StringComparison.Ordinal)
        && actual.SessionType == expected.SessionType
        && actual.Validity == expected.Validity;

    private static TradeEvent? FindExtremeTrade(
        IReadOnlyList<MarketEvent> events,
        string symbol,
        MarketSessionDefinition session,
        decimal price)
    {
        TradeEvent? best = null;
        foreach (var marketEvent in events)
        {
            if (marketEvent is not TradeEvent trade
                || !trade.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)
                || !trade.SessionId.Equals(session.SessionId, StringComparison.Ordinal)
                || trade.EventTimeUtc < session.OpenAtUtc
                || trade.EventTimeUtc > session.CloseAtUtc
                || trade.Price != price)
            {
                continue;
            }

            if (best is null || CompareCanonicalTrades(trade, best) < 0)
            {
                best = trade;
            }
        }

        return best;
    }

    private static int CompareCanonicalTrades(TradeEvent left, TradeEvent right)
    {
        var comparison = left.EventTimeUtc.CompareTo(right.EventTimeUtc);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = string.Compare(left.Provider, right.Provider, StringComparison.Ordinal);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = string.Compare(left.SequenceId, right.SequenceId, StringComparison.Ordinal);
        return comparison != 0
            ? comparison
            : string.Compare(
                MarketEventIdentity.Create(left),
                MarketEventIdentity.Create(right),
                StringComparison.Ordinal);
    }

    private static CompactStructuralPivotSet ToCompactPivots(PriorSessionContext context)
    {
        var ordered = context.SupportCandidates
            .Select(item => (Candidate: item, Role: StructuralRole.Support))
            .Concat(context.ResistanceCandidates.Select(item =>
                (Candidate: item, Role: StructuralRole.Resistance)))
            .OrderBy(item => item.Candidate.EventTimeUtc)
            .ThenBy(item => item.Candidate.SourceSwingId)
            .ToArray();
        var pivots = new CompactStructuralPivotCandidate[ordered.Length];
        var featureVersions = new List<string>();
        var algorithmVersions = new List<string>();
        for (var index = 0; index < ordered.Length; index++)
        {
            var item = ordered[index];
            var featureVersionIndex = featureVersions.FindIndex(version =>
                version.Equals(item.Candidate.FeatureSetVersion, StringComparison.Ordinal));
            if (featureVersionIndex < 0)
            {
                featureVersionIndex = featureVersions.Count;
                featureVersions.Add(item.Candidate.FeatureSetVersion);
            }

            var algorithmVersionIndex = algorithmVersions.FindIndex(version =>
                version.Equals(
                    FeatureCalculator.SwingAlgorithmVersion,
                    StringComparison.Ordinal));
            if (algorithmVersionIndex < 0)
            {
                algorithmVersionIndex = algorithmVersions.Count;
                algorithmVersions.Add(FeatureCalculator.SwingAlgorithmVersion);
            }

            pivots[index] = new CompactStructuralPivotCandidate(
                item.Candidate.SourceSwingId,
                item.Role,
                item.Candidate.Price,
                item.Candidate.EventTimeUtc,
                item.Candidate.ConfirmedAtEventTimeUtc,
                item.Candidate.OriginEventIdentity,
                item.Candidate.ConfirmationEventIdentity,
                featureVersionIndex,
                algorithmVersionIndex);
        }

        return new CompactStructuralPivotSet(
            pivots,
            featureVersions.ToArray(),
            algorithmVersions.ToArray());
    }

    private static StructuralSessionCompleteness MapSessionCompleteness(
        PriorSessionContextStatus completeness) =>
        completeness switch
        {
            PriorSessionContextStatus.Available => StructuralSessionCompleteness.Available,
            PriorSessionContextStatus.Partial => StructuralSessionCompleteness.Partial,
            PriorSessionContextStatus.Invalid => StructuralSessionCompleteness.Invalid,
            _ => StructuralSessionCompleteness.Insufficient
        };

    private static MultiSessionHistoricalReconstructionStatus MapContextStatus(
        PriorSessionContextStatus completeness) =>
        completeness switch
        {
            PriorSessionContextStatus.Available => MultiSessionHistoricalReconstructionStatus.Available,
            PriorSessionContextStatus.Partial => MultiSessionHistoricalReconstructionStatus.Partial,
            PriorSessionContextStatus.Invalid => MultiSessionHistoricalReconstructionStatus.Invalid,
            _ => MultiSessionHistoricalReconstructionStatus.Unavailable
        };

    private static MultiSessionHistoricalReconstructionStatus MapRetrievalStatus(
        HistoricalRetrievalStatus retrievalStatus,
        MultiSessionHistoricalReconstructionStatus contextStatus) =>
        retrievalStatus switch
        {
            HistoricalRetrievalStatus.Cancelled => MultiSessionHistoricalReconstructionStatus.Cancelled,
            HistoricalRetrievalStatus.AuthenticationFailure => MultiSessionHistoricalReconstructionStatus.AuthenticationFailure,
            HistoricalRetrievalStatus.EntitlementFailure => MultiSessionHistoricalReconstructionStatus.EntitlementFailure,
            HistoricalRetrievalStatus.LimitExceeded => MultiSessionHistoricalReconstructionStatus.LimitExceeded,
            HistoricalRetrievalStatus.ProviderFailure => MultiSessionHistoricalReconstructionStatus.ProviderFailure,
            HistoricalRetrievalStatus.MalformedResponse => MultiSessionHistoricalReconstructionStatus.Invalid,
            _ => contextStatus
        };

    private static MultiSessionHistoricalReconstructionStatus MapCalendarStatus(
        MarketSessionCalendarStatus status) =>
        status switch
        {
            MarketSessionCalendarStatus.NoPreviousSession => MultiSessionHistoricalReconstructionStatus.Insufficient,
            MarketSessionCalendarStatus.InvalidReference or MarketSessionCalendarStatus.MalformedResponse =>
                MultiSessionHistoricalReconstructionStatus.Invalid,
            MarketSessionCalendarStatus.Cancelled => MultiSessionHistoricalReconstructionStatus.Cancelled,
            MarketSessionCalendarStatus.AuthenticationFailure =>
                MultiSessionHistoricalReconstructionStatus.AuthenticationFailure,
            MarketSessionCalendarStatus.EntitlementFailure =>
                MultiSessionHistoricalReconstructionStatus.EntitlementFailure,
            MarketSessionCalendarStatus.LimitExceeded => MultiSessionHistoricalReconstructionStatus.LimitExceeded,
            _ => MultiSessionHistoricalReconstructionStatus.ProviderFailure
        };

    private static MultiSessionHistoricalReconstructionStatus MapAssessmentStatus(
        StructuralAssessmentCompleteness completeness) =>
        completeness switch
        {
            StructuralAssessmentCompleteness.Available => MultiSessionHistoricalReconstructionStatus.Available,
            StructuralAssessmentCompleteness.Partial => MultiSessionHistoricalReconstructionStatus.Partial,
            StructuralAssessmentCompleteness.Invalid => MultiSessionHistoricalReconstructionStatus.Invalid,
            _ => MultiSessionHistoricalReconstructionStatus.Insufficient
        };

    private sealed record CalendarHorizonResult(
        IReadOnlyList<MarketSessionDefinition> SessionsNewestFirst,
        string? CalendarId,
        ReconstructionFailure? Failure);

    private sealed record SessionReductionResult(
        StructuralSessionSummary Summary,
        ReconstructionFailure? Failure);

    private sealed record ReconstructionFailure(
        MultiSessionHistoricalReconstructionStatus Status,
        string Reason);

    private sealed class AccountingBuilder
    {
        private int _activeRawSessions;
        private readonly List<long> _normalizedRecordsPerSession = [];
        private readonly List<int> _pivotsProducedPerSession = [];
        private readonly List<long> _compactPivotBytesPerSession = [];

        public int CalendarRequests { get; set; }
        public int HistoricalRequests { get; set; }
        public long PagesRetrieved { get; private set; }
        public long NormalizedRecordsRetrieved { get; private set; }
        public long DuplicateRecordsIgnored { get; private set; }
        public int PeakRawSessionsRetained { get; private set; }
        public long PivotsProduced { get; private set; }
        public long CompactPivotRepresentationsRetained { get; private set; }
        public long PeakPivotRepresentationsRetained { get; private set; }
        public long CompactPivotEstimatedBytes { get; private set; }
        public long PeakGlobalClusteringWorkingSet { get; private set; }
        public int RawSessionsReleased { get; private set; }

        public void RawSessionStarted()
        {
            _activeRawSessions++;
            PeakRawSessionsRetained = Math.Max(PeakRawSessionsRetained, _activeRawSessions);
        }

        public void RawSessionFinished()
        {
            _activeRawSessions--;
            RawSessionsReleased++;
        }

        public void AddHistoricalData(HistoricalMarketSessionData historicalData)
        {
            PagesRetrieved += Math.Max(0, historicalData.Provenance?.PagesRetrieved ?? 0);
            NormalizedRecordsRetrieved += historicalData.Events?.Count ?? 0;
            DuplicateRecordsIgnored += Math.Max(
                0,
                historicalData.Provenance?.DuplicateRecordsIgnored ?? 0);
            _normalizedRecordsPerSession.Add(historicalData.Events?.Count ?? 0);
        }

        public void AddCompactPivots(CompactStructuralPivotSet pivots)
        {
            PivotsProduced += pivots.Pivots.Count;
            _pivotsProducedPerSession.Add(pivots.Pivots.Count);
            CompactPivotRepresentationsRetained += pivots.Pivots.Count;
            PeakPivotRepresentationsRetained = Math.Max(
                PeakPivotRepresentationsRetained,
                CompactPivotRepresentationsRetained);
            CompactPivotEstimatedBytes += pivots.EstimatedBytes;
            _compactPivotBytesPerSession.Add(pivots.EstimatedBytes);
            PeakGlobalClusteringWorkingSet = Math.Max(
                PeakGlobalClusteringWorkingSet,
                CompactPivotRepresentationsRetained + 6);
        }

        public MultiSessionHistoricalReconstructionAccounting Build() =>
            new(
                CalendarRequests,
                HistoricalRequests,
                PagesRetrieved,
                NormalizedRecordsRetrieved,
                DuplicateRecordsIgnored,
                PeakRawSessionsRetained,
                PivotsProduced,
                CompactPivotRepresentationsRetained,
                PeakPivotRepresentationsRetained,
                CompactPivotEstimatedBytes,
                PeakGlobalClusteringWorkingSet,
                RawSessionsReleased,
                _normalizedRecordsPerSession.ToArray(),
                _pivotsProducedPerSession.ToArray(),
                _compactPivotBytesPerSession.ToArray());
    }
}
