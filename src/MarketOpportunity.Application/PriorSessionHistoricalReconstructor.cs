using MarketOpportunity.Analytics;
using MarketOpportunity.Domain;

namespace MarketOpportunity.Application;

public sealed record HistoricalReconstructionRequirements(
    string Provider,
    string Feed,
    string SourceKind);

public sealed class PriorSessionHistoricalReconstructor(PriorSessionContextBuilder builder)
{
    public PriorSessionContext Reconstruct(
        string symbol,
        string currentSessionId,
        MarketSessionDefinition session,
        HistoricalMarketSessionData historicalData,
        HistoricalReconstructionRequirements requirements,
        ScannerConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentSessionId);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(historicalData);
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(requirements.Provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(requirements.Feed);
        ArgumentException.ThrowIfNullOrWhiteSpace(requirements.SourceKind);
        symbol = symbol.Trim().ToUpperInvariant();

        if (session.Validity != MarketSessionValidity.Valid
            || session.SessionType != MarketSessionType.Regular
            || session.OpenAtUtc >= session.CloseAtUtc
            || session.SessionId.Equals(currentSessionId, StringComparison.Ordinal))
        {
            return builder.BuildUnavailable(
                symbol,
                currentSessionId,
                "The calendar session is not a valid distinct regular session for reconstruction.");
        }

        if (historicalData.Request is null
            || historicalData.Provenance is null
            || !historicalData.Request.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)
            || !historicalData.Request.SessionId.Equals(session.SessionId, StringComparison.Ordinal)
            || historicalData.Request.StartUtc.ToUniversalTime() != session.OpenAtUtc.ToUniversalTime()
            || historicalData.Request.EndUtc.ToUniversalTime() != session.CloseAtUtc.ToUniversalTime())
        {
            return BuildRejected(
                symbol,
                currentSessionId,
                session,
                historicalData,
                configuration,
                "Historical request provenance does not match the requested symbol, prior session, or regular-session bounds.");
        }

        if (!Matches(historicalData.Provenance.Provider, requirements.Provider)
            || !Matches(historicalData.Provenance.Feed, requirements.Feed)
            || !Matches(historicalData.Provenance.SourceKind, requirements.SourceKind)
            || !Matches(historicalData.Capabilities.Provider, requirements.Provider)
            || !Matches(historicalData.Capabilities.Feed, requirements.Feed)
            || !Matches(historicalData.Source, requirements.SourceKind)
            || !Matches(session.Provider, requirements.Provider)
            || !historicalData.Capabilities.SupportsTrades)
        {
            return BuildRejected(
                symbol,
                currentSessionId,
                session,
                historicalData,
                configuration,
                $"Historical provider/feed provenance must match {requirements.Provider}/{requirements.Feed}/{requirements.SourceKind}.");
        }

        if (historicalData.RetrievalStatus is HistoricalRetrievalStatus.AuthenticationFailure
            or HistoricalRetrievalStatus.EntitlementFailure
            or HistoricalRetrievalStatus.ProviderFailure)
        {
            return builder.BuildUnavailable(
                symbol,
                currentSessionId,
                $"Historical retrieval was unavailable ({historicalData.RetrievalStatus}).");
        }

        if (historicalData.RetrievalStatus == HistoricalRetrievalStatus.MalformedResponse)
        {
            return BuildRejected(
                symbol,
                currentSessionId,
                session,
                historicalData,
                configuration,
                "Historical retrieval returned a malformed provider response and cannot be used for reconstruction.");
        }

        var eligibleTrades = new List<TradeEvent>();
        var byIdentity = new Dictionary<string, TradeEvent>(StringComparer.Ordinal);
        foreach (var marketEvent in historicalData.Events)
        {
            if (marketEvent is not TradeEvent trade)
            {
                return BuildRejected(
                    symbol,
                    currentSessionId,
                    session,
                    historicalData,
                    configuration,
                    "Historical trade retrieval included a non-trade canonical event.");
            }

            if (!trade.Provider.Equals(requirements.Provider, StringComparison.OrdinalIgnoreCase))
            {
                return BuildRejected(
                    symbol,
                    currentSessionId,
                    session,
                    historicalData,
                    configuration,
                    "A historical trade provider does not match the requested historical source.");
            }

            if (!trade.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)
                || !trade.SessionId.Equals(session.SessionId, StringComparison.Ordinal)
                || trade.EventTimeUtc < session.OpenAtUtc
                || trade.EventTimeUtc > session.CloseAtUtc)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(trade.SequenceId))
            {
                return BuildRejected(
                    symbol,
                    currentSessionId,
                    session,
                    historicalData,
                    configuration,
                    "A historical trade is missing stable canonical identity.");
            }

            var identity = MarketEventIdentity.Create(trade);
            if (byIdentity.TryGetValue(identity, out var existing))
            {
                if (ExistingMaterialDiffers(existing, trade))
                {
                    return BuildRejected(
                        symbol,
                        currentSessionId,
                        session,
                        historicalData,
                        configuration,
                        "Historical replay contains conflicting material records under one canonical identity.");
                }

                continue;
            }

            byIdentity.Add(identity, trade);
            eligibleTrades.Add(trade);
        }

        if (eligibleTrades.Count == 0)
        {
            return builder.BuildUnavailable(
                symbol,
                currentSessionId,
                $"No eligible historical trades are available inside prior session '{session.SessionId}'.");
        }

        byIdentity.Clear();
        byIdentity.TrimExcess();
        var normalized = historicalData with { Events = eligibleTrades };
        if (eligibleTrades.Any(trade => trade.Price <= 0 || trade.Size <= 0))
        {
            return builder.Build(
                symbol,
                currentSessionId,
                session.ToMarketSession(),
                normalized with { DeclaresCompleteCoverage = false },
                configuration);
        }

        if (historicalData.RetrievalStatus != HistoricalRetrievalStatus.SuccessPaginationExhausted
            || !historicalData.PaginationExhausted)
        {
            return builder.Build(
                symbol,
                currentSessionId,
                session.ToMarketSession(),
                normalized with { DeclaresCompleteCoverage = false },
                configuration,
                PriorSessionContextStatus.Partial,
                $"Historical transport was not exhausted successfully ({historicalData.RetrievalStatus}); retrieved observations are partial.");
        }

        var transportComplete = historicalData.RetrievalStatus
            == HistoricalRetrievalStatus.SuccessPaginationExhausted
            && historicalData.PaginationExhausted;
        var boundaryCoverage = historicalData.CoverageStartUtc is { } start
            && historicalData.CoverageEndUtc is { } end
            && start.ToUniversalTime() <= session.OpenAtUtc.ToUniversalTime()
            && end.ToUniversalTime() >= session.CloseAtUtc.ToUniversalTime();
        var analyticallyComplete = transportComplete
            && historicalData.DeclaresCompleteCoverage
            && boundaryCoverage
            && !historicalData.HasKnownGaps;
        if (!analyticallyComplete)
        {
            return builder.Build(
                symbol,
                currentSessionId,
                session.ToMarketSession(),
                normalized with { DeclaresCompleteCoverage = false },
                configuration,
                PriorSessionContextStatus.Partial,
                "Pagination completed, but gap-free full-session coverage was not established by the historical source.");
        }

        return builder.Build(
            symbol,
            currentSessionId,
            session.ToMarketSession(),
            normalized,
            configuration);
    }

    private PriorSessionContext BuildRejected(
        string symbol,
        string currentSessionId,
        MarketSessionDefinition session,
        HistoricalMarketSessionData historicalData,
        ScannerConfiguration configuration,
        string reason) =>
        builder.Build(
            symbol,
            currentSessionId,
            session.ToMarketSession(),
            historicalData with
            {
                Events = [],
                DeclaresCompleteCoverage = false
            },
            configuration,
            PriorSessionContextStatus.Invalid,
            reason);

    private static bool Matches(string left, string right) =>
        left.Equals(right, StringComparison.OrdinalIgnoreCase);

    private static bool ExistingMaterialDiffers(TradeEvent left, TradeEvent right) =>
        left.Symbol != right.Symbol
        || left.SessionId != right.SessionId
        || left.EventTimeUtc != right.EventTimeUtc
        || left.Provider != right.Provider
        || left.Price != right.Price
        || left.Size != right.Size;
}
