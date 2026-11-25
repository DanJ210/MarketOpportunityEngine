using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MarketOpportunity.Domain;

namespace MarketOpportunity.Analytics;

public sealed class PriorSessionContextBuilder
{
    public PriorSessionContextBuilder(FeatureCalculator featureCalculator)
    {
        ArgumentNullException.ThrowIfNull(featureCalculator);
    }

    public const string DefinitionVersion = "prior-session-context-v1";

    public PriorSessionContext Build(
        string symbol,
        string currentSessionId,
        MarketSession priorSession,
        HistoricalMarketSessionData historicalData,
        ScannerConfiguration configuration,
        PriorSessionContextStatus? completenessOverride = null,
        string? completenessReasonOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentSessionId);
        ArgumentNullException.ThrowIfNull(priorSession);
        ArgumentNullException.ThrowIfNull(historicalData);
        ArgumentNullException.ThrowIfNull(configuration);
        if (priorSession.StartUtc >= priorSession.EndUtc)
        {
            throw new ArgumentException("The prior session must have a positive time range.", nameof(priorSession));
        }

        if (completenessOverride.HasValue != (completenessReasonOverride is not null))
        {
            throw new ArgumentException(
                "A completeness override and reason must be supplied together.",
                nameof(completenessOverride));
        }

        var trades = historicalData.Events
            .OfType<TradeEvent>()
            .Where(item => item.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)
                && item.SessionId.Equals(priorSession.SessionId, StringComparison.Ordinal)
                && item.EventTimeUtc >= priorSession.StartUtc
                && item.EventTimeUtc <= priorSession.EndUtc)
            .DistinctBy(MarketEventIdentity.Create)
            .OrderBy(item => item.EventTimeUtc)
            .ThenBy(item => item.Provider, StringComparer.Ordinal)
            .ThenBy(item => item.SequenceId, StringComparer.Ordinal)
            .ThenBy(MarketEventIdentity.Create, StringComparer.Ordinal)
            .ToArray();
        decimal? observedHigh = null;
        decimal? observedLow = null;
        foreach (var trade in trades)
        {
            if (trade.Price <= 0)
            {
                continue;
            }

            observedHigh = observedHigh is null || trade.Price > observedHigh
                ? trade.Price
                : observedHigh;
            observedLow = observedLow is null || trade.Price < observedLow
                ? trade.Price
                : observedLow;
        }

        var lastObservedTrade = trades.LastOrDefault();
        var sessionStartCovered = historicalData.CoverageStartUtc is { } coverageStart
            && coverageStart <= priorSession.StartUtc;
        var sessionEndCovered = historicalData.CoverageEndUtc is { } coverageEnd
            && coverageEnd >= priorSession.EndUtc;
        var completeCoverage = historicalData.DeclaresCompleteCoverage
            && historicalData.Capabilities.SupportsTrades
            && sessionStartCovered
            && sessionEndCovered
            && !historicalData.HasKnownGaps;
        var invalidTrade = trades.Any(trade => trade.Price <= 0 || trade.Size <= 0);

        var computedCompleteness = trades.Length == 0
            ? PriorSessionContextStatus.Unavailable
            : invalidTrade
                ? PriorSessionContextStatus.Invalid
                : completeCoverage
                    ? PriorSessionContextStatus.Available
                    : PriorSessionContextStatus.Partial;
        var computedReason = computedCompleteness switch
        {
            PriorSessionContextStatus.Available =>
                "Historical trade coverage spans the complete prior session with no declared gaps.",
            PriorSessionContextStatus.Partial =>
                "Historical trades are available, but full-session coverage or gap-free completeness is not established; extrema and the last observed trade are not session high, low, or close.",
            PriorSessionContextStatus.Invalid =>
                "At least one eligible prior-session trade has a non-positive price or size; session levels are invalid.",
            _ => "No eligible prior-session trades are available."
        };
        if (completenessOverride == PriorSessionContextStatus.Available
            && computedCompleteness != PriorSessionContextStatus.Available)
        {
            throw new ArgumentException(
                "A completeness override cannot promote an incomplete or invalid context to Available.",
                nameof(completenessOverride));
        }

        var completeness = completenessOverride ?? computedCompleteness;
        var completenessReason = completenessReasonOverride ?? computedReason;
        var (confirmedHighs, confirmedLows) = FeatureCalculator.CalculateConfirmedSwings(
            trades,
            symbol,
            priorSession.SessionId,
            configuration.FeatureSetVersion);
        var latestHigh = confirmedHighs.Count > 0 ? confirmedHighs[^1] : null;
        var previousHigh = confirmedHighs.Count > 1 ? confirmedHighs[^2] : null;
        var latestLow = confirmedLows.Count > 0 ? confirmedLows[^1] : null;
        var previousLow = confirmedLows.Count > 1 ? confirmedLows[^2] : null;
        var structureState = CalculateStructureState(latestHigh, previousHigh, latestLow, previousLow);
        var supportCandidates = confirmedLows
            .Select(ToCandidate)
            .ToArray();
        var resistanceCandidates = confirmedHighs
            .Select(ToCandidate)
            .ToArray();
        var canPublishSessionLevels =
            completeness == PriorSessionContextStatus.Available && !invalidTrade;
        var contextContent = new
        {
            Symbol = symbol,
            CurrentSessionId = currentSessionId,
            PriorSessionId = priorSession.SessionId,
            PriorSessionStartUtc = priorSession.StartUtc,
            PriorSessionEndUtc = priorSession.EndUtc,
            PriorSessionHigh = canPublishSessionLevels ? observedHigh : null,
            PriorSessionLow = canPublishSessionLevels ? observedLow : null,
            PriorSessionClose = canPublishSessionLevels ? lastObservedTrade?.Price : null,
            PriorSessionCloseEventTimeUtc = canPublishSessionLevels ? lastObservedTrade?.EventTimeUtc : null,
            CloseSemantics = canPublishSessionLevels
                ? PriorSessionCloseSemantics.LastEligibleTradeAtOrBeforeSessionEnd
                : PriorSessionCloseSemantics.Unavailable,
            ObservedHigh = observedHigh,
            ObservedLow = observedLow,
            LastObservedTrade = lastObservedTrade?.Price,
            LastObservedTradeEventTimeUtc = lastObservedTrade?.EventTimeUtc,
            LatestConfirmedSwingHigh = latestHigh,
            LatestConfirmedSwingLow = latestLow,
            PreviousConfirmedSwingHigh = previousHigh,
            PreviousConfirmedSwingLow = previousLow,
            StructureState = structureState,
            SupportCandidates = supportCandidates,
            ResistanceCandidates = resistanceCandidates,
            Completeness = completeness,
            CompletenessReason = completenessReason,
            MarketData = historicalData.Capabilities,
            HistoricalSource = historicalData.Source,
            CoverageStartUtc = historicalData.CoverageStartUtc,
            CoverageEndUtc = historicalData.CoverageEndUtc,
            DeclaredCompleteCoverage = historicalData.DeclaresCompleteCoverage,
            IsSessionStartCovered = sessionStartCovered,
            IsSessionEndCovered = sessionEndCovered,
            HasKnownGaps = historicalData.HasKnownGaps,
            ContextDefinitionVersion = DefinitionVersion,
            EventInputs = trades.Select(Fingerprint).ToArray(),
            AlgorithmVersion = "session-swing-fractal-v1",
            FeatureSetVersion = configuration.FeatureSetVersion
        };

        return new PriorSessionContext(
            DeterministicId(contextContent),
            symbol,
            currentSessionId,
            priorSession.SessionId,
            priorSession.StartUtc,
            priorSession.EndUtc,
            canPublishSessionLevels ? observedHigh : null,
            canPublishSessionLevels ? observedLow : null,
            canPublishSessionLevels ? lastObservedTrade?.Price : null,
            canPublishSessionLevels ? lastObservedTrade?.EventTimeUtc : null,
            canPublishSessionLevels
                ? PriorSessionCloseSemantics.LastEligibleTradeAtOrBeforeSessionEnd
                : PriorSessionCloseSemantics.Unavailable,
            observedHigh,
            observedLow,
            lastObservedTrade?.Price,
            lastObservedTrade?.EventTimeUtc,
            latestHigh,
            latestLow,
            previousHigh,
            previousLow,
            structureState,
            supportCandidates,
            resistanceCandidates,
            completeness,
            completenessReason,
            historicalData.Capabilities,
            historicalData.Source,
            historicalData.CoverageStartUtc,
            historicalData.CoverageEndUtc,
            sessionStartCovered,
            sessionEndCovered,
            historicalData.HasKnownGaps,
            DefinitionVersion);
    }

    public PriorSessionContext BuildUnavailable(
        string symbol,
        string currentSessionId,
        string reason,
        string? calendarId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentSessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var content = new
        {
            Symbol = symbol,
            CurrentSessionId = currentSessionId,
            Reason = reason,
            CalendarId = calendarId,
            ContextDefinitionVersion = DefinitionVersion
        };
        return new PriorSessionContext(
            DeterministicId(content),
            symbol,
            currentSessionId,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            PriorSessionCloseSemantics.Unavailable,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            MarketStructureState.Insufficient,
            [],
            [],
            PriorSessionContextStatus.Unavailable,
            reason,
            null,
            null,
            null,
            null,
            false,
            false,
            false,
            DefinitionVersion);
    }

    public static PriorSessionContextView CreateView(
        PriorSessionContext context,
        decimal? currentReferencePrice)
    {
        ArgumentNullException.ThrowIfNull(context);
        var validReferencePrice = currentReferencePrice is > 0 ? currentReferencePrice : null;
        PriceDistance? Distance(decimal? levelPrice) =>
            validReferencePrice is { } reference && levelPrice is { } level
                ? new PriceDistance(
                    reference,
                    level,
                    Math.Abs(reference - level),
                    Math.Abs(reference - level) / reference * 100m)
                : null;
        var support = context.SupportCandidates
            .Where(item => validReferencePrice is { } reference && item.Price <= reference)
            .OrderBy(item => validReferencePrice is { } reference
                ? reference - item.Price
                : decimal.MaxValue)
            .ThenBy(item => item.Id)
            .FirstOrDefault();
        var resistance = context.ResistanceCandidates
            .Where(item => validReferencePrice is { } reference && item.Price >= reference)
            .OrderBy(item => validReferencePrice is { } reference
                ? item.Price - reference
                : decimal.MaxValue)
            .ThenBy(item => item.Id)
            .FirstOrDefault();

        return new PriorSessionContextView(
            context,
            validReferencePrice,
            Distance(context.PriorSessionHigh),
            Distance(context.PriorSessionLow),
            Distance(context.ObservedHigh),
            Distance(context.ObservedLow),
            Distance(context.LatestConfirmedSwingHigh?.Price),
            Distance(context.LatestConfirmedSwingLow?.Price),
            support,
            Distance(support?.Price),
            resistance,
            Distance(resistance?.Price));
    }

    private static StructuralLevelCandidate ToCandidate(ConfirmedSwing swing) =>
        new(
            swing.Id,
            swing.Type == ConfirmedSwingType.ConfirmedSwingLow
                ? StructuralLevelType.Support
                : StructuralLevelType.Resistance,
            swing.Symbol,
            swing.SessionId,
            swing.Price,
            swing.EventTimeUtc,
            swing.ConfirmedAtEventTimeUtc,
            swing.Id,
            swing.OriginEventIdentity,
            swing.ConfirmationEventIdentity,
            swing.FeatureSetVersion,
            swing.AlgorithmVersion);

    private static string Fingerprint(TradeEvent trade) =>
        JsonSerializer.Serialize(new
        {
            trade.Symbol,
            trade.SessionId,
            trade.EventTimeUtc,
            trade.Provider,
            trade.SequenceId,
            trade.Price,
            trade.Size
        });

    private static MarketStructureState CalculateStructureState(
        ConfirmedSwing? latestHigh,
        ConfirmedSwing? previousHigh,
        ConfirmedSwing? latestLow,
        ConfirmedSwing? previousLow)
    {
        if (latestHigh is null
            || previousHigh is null
            || latestLow is null
            || previousLow is null)
        {
            return MarketStructureState.Insufficient;
        }

        var high = latestHigh.Price.CompareTo(previousHigh.Price);
        var low = latestLow.Price.CompareTo(previousLow.Price);
        return (high, low) switch
        {
            (> 0, > 0) => MarketStructureState.Bullish,
            (< 0, < 0) => MarketStructureState.Bearish,
            (0, 0) => MarketStructureState.Range,
            _ => MarketStructureState.Mixed
        };
    }

    private static Guid DeterministicId<T>(T content)
    {
        var json = JsonSerializer.Serialize(content);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return new Guid(hash.AsSpan(0, 16));
    }
}
