using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MarketOpportunity.Domain;

namespace MarketOpportunity.Analytics;

public sealed class PriorSessionContextBuilder(FeatureCalculator featureCalculator)
{
    public const string DefinitionVersion = "prior-session-context-v1";

    public PriorSessionContext Build(
        string symbol,
        string currentSessionId,
        MarketSession priorSession,
        HistoricalMarketSessionData historicalData,
        ScannerConfiguration configuration)
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

        var eligibleEvents = historicalData.Events
            .Where(item => item.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)
                && item.SessionId.Equals(priorSession.SessionId, StringComparison.Ordinal)
                && item.EventTimeUtc >= priorSession.StartUtc
                && item.EventTimeUtc <= priorSession.EndUtc)
            .ToArray();
        var state = new SymbolEventState(symbol, priorSession.SessionId, Math.Max(1, eligibleEvents.Length));
        foreach (var marketEvent in eligibleEvents)
        {
            state.Add(marketEvent);
        }

        var orderedEvents = state.GetEventsAsOf(priorSession.EndUtc);
        var trades = orderedEvents.OfType<TradeEvent>().ToArray();
        var validTrades = trades.Where(trade => trade.Price > 0).ToArray();
        decimal? observedHigh = validTrades.Length == 0 ? null : validTrades.Max(trade => trade.Price);
        decimal? observedLow = validTrades.Length == 0 ? null : validTrades.Min(trade => trade.Price);
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
        var invalidPrice = trades.Any(trade => trade.Price <= 0);

        FeatureSnapshot? features = null;
        if (trades.Length > 0)
        {
            features = featureCalculator.Calculate(
                state,
                priorSession.EndUtc,
                configuration,
                historicalData.Capabilities);
        }

        var completeness = trades.Length == 0
            ? PriorSessionContextStatus.Unavailable
            : invalidPrice
                ? PriorSessionContextStatus.Invalid
                : completeCoverage
                    ? PriorSessionContextStatus.Available
                    : PriorSessionContextStatus.Partial;
        var completenessReason = completeness switch
        {
            PriorSessionContextStatus.Available =>
                "Historical trade coverage spans the complete prior session with no declared gaps.",
            PriorSessionContextStatus.Partial =>
                "Historical trades are available, but full-session coverage or gap-free completeness is not established; extrema and the last observed trade are not session high, low, or close.",
            PriorSessionContextStatus.Invalid =>
                "At least one eligible prior-session trade has a non-positive price; session levels are invalid.",
            _ => "No eligible prior-session trades are available."
        };
        // Candidate selection uses every eligible confirmed pivot from the shared swing
        // calculation; latest/previous swings remain only the structure summary.
        var (confirmedHighs, confirmedLows) = FeatureCalculator.CalculateConfirmedSwings(
            trades,
            symbol,
            priorSession.SessionId,
            configuration.FeatureSetVersion);
        var supportCandidates = confirmedLows
            .Select(ToCandidate)
            .ToArray();
        var resistanceCandidates = confirmedHighs
            .Select(ToCandidate)
            .ToArray();
        var canPublishSessionLevels = completeness == PriorSessionContextStatus.Available && !invalidPrice;
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
            LatestConfirmedSwingHigh = features?.LatestConfirmedSwingHigh,
            LatestConfirmedSwingLow = features?.LatestConfirmedSwingLow,
            PreviousConfirmedSwingHigh = features?.PreviousConfirmedSwingHigh,
            PreviousConfirmedSwingLow = features?.PreviousConfirmedSwingLow,
            StructureState = features?.StructureState ?? MarketStructureState.Insufficient,
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
            EventInputs = orderedEvents.Select(Fingerprint).ToArray(),
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
            features?.LatestConfirmedSwingHigh,
            features?.LatestConfirmedSwingLow,
            features?.PreviousConfirmedSwingHigh,
            features?.PreviousConfirmedSwingLow,
            features?.StructureState ?? MarketStructureState.Insufficient,
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

    private static string Fingerprint(MarketEvent marketEvent)
    {
        return marketEvent switch
        {
            TradeEvent trade => JsonSerializer.Serialize(trade),
            QuoteEvent quote => JsonSerializer.Serialize(quote),
            _ => throw new NotSupportedException(
                $"Unsupported normalized market event type: {marketEvent.GetType().Name}.")
        };
    }

    private static Guid DeterministicId<T>(T content)
    {
        var json = JsonSerializer.Serialize(content);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return new Guid(hash.AsSpan(0, 16));
    }
}
