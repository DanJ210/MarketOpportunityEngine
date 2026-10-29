using MarketOpportunity.Domain;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MarketOpportunity.Analytics;

public sealed class SymbolEventState
{
    private readonly int _maximumEvents;
    private readonly List<MarketEvent> _events = [];
    private readonly HashSet<string> _eventIdentities = new(StringComparer.Ordinal);

    public SymbolEventState(string symbol, string sessionId, int maximumEvents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumEvents, 1);
        Symbol = symbol;
        SessionId = sessionId;
        _maximumEvents = maximumEvents;
    }

    public string Symbol { get; }
    public string SessionId { get; }
    public bool HasEvictedEvents { get; private set; }

    public bool Add(MarketEvent marketEvent)
    {
        ArgumentNullException.ThrowIfNull(marketEvent);
        if (!string.Equals(marketEvent.Symbol, Symbol, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(marketEvent.SessionId, SessionId, StringComparison.Ordinal))
        {
            throw new ArgumentException("The event symbol or session does not match this state.", nameof(marketEvent));
        }

        var identity = MarketEventIdentity.Create(marketEvent);
        if (!_eventIdentities.Add(identity))
        {
            return false;
        }

        var index = _events.BinarySearch(marketEvent, EventComparer.Instance);
        if (index < 0)
        {
            index = ~index;
        }

        _events.Insert(index, marketEvent);
        if (_events.Count > _maximumEvents)
        {
            var removed = _events[0];
            _events.RemoveAt(0);
            HasEvictedEvents = true;
            _eventIdentities.Remove(MarketEventIdentity.Create(removed));
        }

        return true;
    }

    public IReadOnlyList<MarketEvent> GetEventsAsOf(DateTimeOffset asOfUtc) =>
        _events.Where(marketEvent => marketEvent.EventTimeUtc <= asOfUtc).ToArray();

    private sealed class EventComparer : IComparer<MarketEvent>
    {
        public static EventComparer Instance { get; } = new();

        public int Compare(MarketEvent? left, MarketEvent? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            var timeOrder = left.EventTimeUtc.CompareTo(right.EventTimeUtc);
            if (timeOrder != 0)
            {
                return timeOrder;
            }

            var providerOrder = string.Compare(left.Provider, right.Provider, StringComparison.Ordinal);
            if (providerOrder != 0)
            {
                return providerOrder;
            }

            var sequenceOrder = string.Compare(left.SequenceId, right.SequenceId, StringComparison.Ordinal);
            return sequenceOrder != 0
                ? sequenceOrder
                : string.Compare(
                    MarketEventIdentity.Create(left),
                    MarketEventIdentity.Create(right),
                    StringComparison.Ordinal);
        }
    }

}

public sealed class FeatureCalculator
{
    public FeatureSnapshot Calculate(
        SymbolEventState state,
        DateTimeOffset asOfUtc,
        ScannerConfiguration configuration,
        MarketDataCapabilities? marketData = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(configuration);
        marketData ??= MarketDataCapabilities.Unknown;

        var events = state.GetEventsAsOf(asOfUtc);
        var trades = events.OfType<TradeEvent>().ToArray();
        var quote = events.OfType<QuoteEvent>().LastOrDefault();
        var latestTrade = trades.LastOrDefault();
        var windowStart = asOfUtc.AddSeconds(-configuration.MomentumWindowSeconds);
        var windowTrades = trades.Where(trade => trade.EventTimeUtc >= windowStart).ToArray();
        var (shortHorizonDollarVolumeUsd, shortHorizonDollarVolumeStatus) =
            CalculateShortHorizonDollarVolume(windowTrades);

        var hasValidSessionPrices = trades.Length > 0 && trades.All(trade => trade.Price > 0);
        decimal? sessionHighPrice = hasValidSessionPrices ? trades.Max(trade => trade.Price) : null;
        decimal? sessionLowPrice = hasValidSessionPrices ? trades.Min(trade => trade.Price) : null;
        decimal? sessionRangePositionPercent =
            latestTrade?.Price is > 0
            && sessionHighPrice.HasValue
            && sessionLowPrice.HasValue
            && sessionHighPrice.Value > sessionLowPrice.Value
                ? (latestTrade.Price - sessionLowPrice.Value)
                    / (sessionHighPrice.Value - sessionLowPrice.Value)
                    * 100m
                : null;
        var sessionPriceStructure = CalculatePriceStructure(trades, hasValidSessionPrices);
        var (sessionAdx, sessionAdxStatus) = CalculateAdx(
            trades,
            configuration.TrendStrengthPeriod,
            hasValidSessionPrices);
        var (sessionRealizedVolatilityPercent, sessionRealizedVolatilityStatus) =
            CalculateSessionRealizedVolatility(trades, hasValidSessionPrices);

        var emaPricesAreValid = trades.Length > 0 && trades.All(trade => trade.Price > 0);
        var (fastEma, fastEmaSlopePerMinute) = CalculateEma(
            trades,
            configuration.EmaFastPeriod,
            asOfUtc.AddSeconds(-configuration.MomentumWindowSeconds),
            emaPricesAreValid);
        var (mediumEma, mediumEmaSlopePerMinute) = CalculateEma(
            trades,
            configuration.EmaMediumPeriod,
            asOfUtc.AddSeconds(-configuration.MomentumWindowSeconds),
            emaPricesAreValid);
        decimal? fastMediumEmaSpreadPercent = fastEma.HasValue && mediumEma is > 0
            ? (fastEma.Value - mediumEma.Value) / mediumEma.Value * 100m
            : null;
        decimal? vwap = null;
        var totalVolume = trades.Sum(trade => trade.Size);
        long? sessionCumulativeVolume = trades.Length > 0 && trades.All(trade => trade.Size > 0)
            ? totalVolume
            : null;
        if (totalVolume > 0)
        {
            vwap = trades.Sum(trade => trade.Price * trade.Size) / totalVolume;
        }

        decimal? sessionVwapSlopePerMinute = null;
        var sessionVwapSlopeDirection = VwapSlopeDirection.Insufficient;
        var slopeWindowStart = asOfUtc.AddSeconds(-configuration.MomentumWindowSeconds);
        decimal? firstWindowVwap = null;
        decimal? lastWindowVwap = null;
        DateTimeOffset? firstWindowVwapTime = null;
        DateTimeOffset? lastWindowVwapTime = null;
        decimal cumulativeVolume = 0;
        decimal cumulativePriceVolume = 0;
        foreach (var timeGroup in trades.GroupBy(trade => trade.EventTimeUtc))
        {
            foreach (var trade in timeGroup)
            {
                cumulativeVolume += trade.Size;
                cumulativePriceVolume += trade.Price * trade.Size;
            }

            if (timeGroup.Key < slopeWindowStart || cumulativeVolume <= 0)
            {
                continue;
            }

            var cumulativeVwap = cumulativePriceVolume / cumulativeVolume;
            firstWindowVwap ??= cumulativeVwap;
            firstWindowVwapTime ??= timeGroup.Key;
            lastWindowVwap = cumulativeVwap;
            lastWindowVwapTime = timeGroup.Key;
        }

        if (firstWindowVwap.HasValue
            && lastWindowVwap.HasValue
            && firstWindowVwapTime.HasValue
            && lastWindowVwapTime.HasValue)
        {
            var elapsedTicks = (lastWindowVwapTime.Value - firstWindowVwapTime.Value).Ticks;
            if (elapsedTicks > 0)
            {
                sessionVwapSlopePerMinute =
                    (lastWindowVwap.Value - firstWindowVwap.Value)
                    * TimeSpan.TicksPerMinute
                    / elapsedTicks;
                sessionVwapSlopeDirection = sessionVwapSlopePerMinute.Value switch
                {
                    > 0 => VwapSlopeDirection.Rising,
                    < 0 => VwapSlopeDirection.Falling,
                    _ => VwapSlopeDirection.Flat
                };
            }
        }

        var sessionTrendDirection = TrendDirectionContextCalculator.Calculate(
            fastEma,
            mediumEma,
            fastMediumEmaSpreadPercent,
            fastEmaSlopePerMinute,
            mediumEmaSlopePerMinute,
            sessionVwapSlopePerMinute,
            sessionVwapSlopeDirection,
            sessionPriceStructure,
            sessionAdx,
            sessionAdxStatus);

        decimal? returnPercent = null;
        if (windowTrades.Length >= 2 && windowTrades[0].Price > 0 && latestTrade is not null)
        {
            returnPercent = (latestTrade.Price - windowTrades[0].Price) / windowTrades[0].Price * 100m;
        }

        var secondaryWindowStart = asOfUtc.AddSeconds(-configuration.SecondaryMomentumWindowSeconds);
        var secondaryWindowTrades = trades.Where(trade => trade.EventTimeUtc >= secondaryWindowStart).ToArray();
        decimal? secondaryReturnPercent = null;
        if (secondaryWindowTrades.Length >= 2 && secondaryWindowTrades.All(trade => trade.Price > 0))
        {
            var firstPrice = secondaryWindowTrades[0].Price;
            secondaryReturnPercent = (secondaryWindowTrades[^1].Price - firstPrice) / firstPrice * 100m;
        }

        var quoteValid = quote is not null
            && quote.BidPrice > 0
            && quote.AskPrice > 0
            && quote.AskPrice >= quote.BidPrice;
        var priceDataValid = trades.All(trade => trade.Price > 0 && trade.Size > 0);
        decimal? spreadBps = quoteValid
            ? (quote!.AskPrice - quote.BidPrice) / ((quote.AskPrice + quote.BidPrice) / 2m) * 10_000m
            : null;
        TimeSpan? quoteAge = quote is null ? null : asOfUtc - quote.EventTimeUtc;
        var hasRequiredData = latestTrade is not null
            && vwap.HasValue
            && windowTrades.Length >= configuration.MinimumRequiredTrades
            && quote is not null;
        var quality = !hasRequiredData
            ? DataQualityStatus.Insufficient
            : state.HasEvictedEvents
                || !quoteValid
                || !priceDataValid
                || quoteAge is null
                || quoteAge > TimeSpan.FromSeconds(configuration.MaximumQuoteAgeSeconds)
                ? DataQualityStatus.Degraded
                : DataQualityStatus.Good;

        var symbol = state.Symbol;
        var featureValues = new[]
        {
            symbol,
            state.SessionId,
            asOfUtc.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture),
            configuration.FeatureSetVersion,
            Format(latestTrade?.Price),
            Format(quote?.BidPrice),
            Format(quote?.AskPrice),
            Format(spreadBps),
            Format(vwap),
            Format(sessionVwapSlopePerMinute),
            sessionVwapSlopeDirection.ToString(),
            Format(latestTrade is not null && vwap is > 0
                ? (latestTrade.Price - vwap.Value) / vwap.Value * 100m
                : null),
            Format(returnPercent),
            Format(secondaryReturnPercent),
            windowTrades.Sum(trade => trade.Size).ToString(CultureInfo.InvariantCulture),
            Format(shortHorizonDollarVolumeUsd),
            shortHorizonDollarVolumeStatus.ToString(),
            sessionCumulativeVolume?.ToString(CultureInfo.InvariantCulture) ?? "",
            Format(sessionHighPrice),
            Format(sessionLowPrice),
            Format(sessionRangePositionPercent),
            sessionPriceStructure?.ToString() ?? "",
            Format(sessionAdx),
            sessionAdxStatus.ToString(),
            Format(sessionRealizedVolatilityPercent),
            sessionRealizedVolatilityStatus.ToString(),
            sessionTrendDirection.ToString(),
            Format(fastEma),
            Format(mediumEma),
            Format(fastMediumEmaSpreadPercent),
            Format(fastEmaSlopePerMinute),
            Format(mediumEmaSlopePerMinute),
            quoteAge?.Ticks.ToString(CultureInfo.InvariantCulture) ?? "",
            quoteValid.ToString(),
            priceDataValid.ToString(),
            hasRequiredData.ToString(),
            (!state.HasEvictedEvents).ToString(),
            quality.ToString(),
            JsonSerializer.Serialize(marketData)
        };
        var inputIdentity = string.Join(
            "\u001f",
            JsonSerializer.Serialize(configuration),
            string.Join("\u001e", events.Select(MarketEventIdentity.Create)),
            string.Join("\u001f", featureValues));

        return new FeatureSnapshot(
            DeterministicId.From(inputIdentity),
            symbol,
            state.SessionId,
            asOfUtc,
            configuration.FeatureSetVersion,
            latestTrade?.Price,
            quote?.BidPrice,
            quote?.AskPrice,
            spreadBps,
            vwap,
            sessionVwapSlopePerMinute,
            sessionVwapSlopeDirection,
            latestTrade is not null && vwap is > 0
                ? (latestTrade.Price - vwap.Value) / vwap.Value * 100m
                : null,
            returnPercent,
            secondaryReturnPercent,
            windowTrades.Sum(trade => trade.Size),
            shortHorizonDollarVolumeUsd,
            shortHorizonDollarVolumeStatus,
            sessionCumulativeVolume,
            sessionHighPrice,
            sessionLowPrice,
            sessionRangePositionPercent,
            sessionPriceStructure,
            sessionAdx,
            sessionAdxStatus,
            sessionRealizedVolatilityPercent,
            sessionRealizedVolatilityStatus,
            sessionTrendDirection,
            fastEma,
            mediumEma,
            fastMediumEmaSpreadPercent,
            fastEmaSlopePerMinute,
            mediumEmaSlopePerMinute,
            quoteAge,
            quoteValid,
            priceDataValid,
            hasRequiredData,
            !state.HasEvictedEvents,
            quality,
            marketData);
    }

    private static (decimal? Value, ShortHorizonDollarVolumeStatus Status) CalculateShortHorizonDollarVolume(
        IReadOnlyList<TradeEvent> trades)
    {
        if (trades.Count == 0)
        {
            return (null, ShortHorizonDollarVolumeStatus.Insufficient);
        }

        if (trades.Any(trade => trade.Price <= 0 || trade.Size <= 0))
        {
            return (null, ShortHorizonDollarVolumeStatus.InvalidTradeData);
        }

        decimal dollarVolume = 0m;
        foreach (var trade in trades)
        {
            dollarVolume += trade.Price * trade.Size;
        }

        return (dollarVolume, ShortHorizonDollarVolumeStatus.Available);
    }

    private static (decimal? Value, AdxStatus Status) CalculateAdx(
        IReadOnlyList<TradeEvent> trades,
        int period,
        bool pricesAreValid)
    {
        if (trades.Count == 0)
        {
            return (null, AdxStatus.Insufficient);
        }

        if (!pricesAreValid)
        {
            return (null, AdxStatus.InvalidPriceData);
        }

        decimal? smoothedTrueRange = null;
        decimal smoothedPositiveMovement = 0m;
        decimal smoothedNegativeMovement = 0m;
        decimal initialAdx = 0m;
        decimal? adx = null;
        var movementCount = 0;
        var dxCount = 0;
        var previousPrice = trades[0].Price;

        for (var index = 1; index < trades.Count; index++)
        {
            var price = trades[index].Price;
            var priceChange = price - previousPrice;
            var positiveMovement = priceChange > 0m ? priceChange : 0m;
            var negativeMovement = priceChange < 0m ? -priceChange : 0m;
            var trueRange = Math.Abs(priceChange);
            movementCount++;

            if (movementCount <= period)
            {
                smoothedTrueRange = UpdateAverage(smoothedTrueRange ?? 0m, trueRange, movementCount);
                smoothedPositiveMovement =
                    UpdateAverage(smoothedPositiveMovement, positiveMovement, movementCount);
                smoothedNegativeMovement =
                    UpdateAverage(smoothedNegativeMovement, negativeMovement, movementCount);
            }
            else
            {
                smoothedTrueRange = UpdateWilderAverage(smoothedTrueRange!.Value, trueRange, period);
                smoothedPositiveMovement =
                    UpdateWilderAverage(smoothedPositiveMovement, positiveMovement, period);
                smoothedNegativeMovement =
                    UpdateWilderAverage(smoothedNegativeMovement, negativeMovement, period);
            }

            if (movementCount >= period)
            {
                var positiveIndex = smoothedTrueRange is > 0m
                    ? smoothedPositiveMovement / smoothedTrueRange.Value * 100m
                    : 0m;
                var negativeIndex = smoothedTrueRange is > 0m
                    ? smoothedNegativeMovement / smoothedTrueRange.Value * 100m
                    : 0m;
                var directionalIndexSum = positiveIndex + negativeIndex;
                var dx = directionalIndexSum == 0m
                    ? 0m
                    : Math.Abs(positiveIndex - negativeIndex) / directionalIndexSum * 100m;
                dxCount++;
                if (adx.HasValue)
                {
                    adx = UpdateWilderAverage(adx.Value, dx, period);
                }
                else
                {
                    initialAdx = UpdateAverage(initialAdx, dx, dxCount);
                    if (dxCount == period)
                    {
                        adx = initialAdx;
                    }
                }
            }

            previousPrice = price;
        }

        return adx.HasValue
            ? (adx.Value, AdxStatus.Available)
            : (null, AdxStatus.Insufficient);
    }

    private static (decimal? Value, RealizedVolatilityStatus Status) CalculateSessionRealizedVolatility(
        IReadOnlyList<TradeEvent> trades,
        bool pricesAreValid)
    {
        if (trades.Count == 0)
        {
            return (null, RealizedVolatilityStatus.Insufficient);
        }

        if (!pricesAreValid)
        {
            return (null, RealizedVolatilityStatus.InvalidPriceData);
        }

        if (trades.Count < 2)
        {
            return (null, RealizedVolatilityStatus.Insufficient);
        }

        var sumSquaredLogReturns = 0d;
        var previousPrice = (double)trades[0].Price;
        for (var index = 1; index < trades.Count; index++)
        {
            var price = (double)trades[index].Price;
            var logReturn = Math.Log(price / previousPrice);
            sumSquaredLogReturns += logReturn * logReturn;
            previousPrice = price;
        }

        var volatilityPercent = Math.Sqrt(sumSquaredLogReturns) * 100d;
        return (
            decimal.Round((decimal)volatilityPercent, 9, MidpointRounding.ToEven),
            RealizedVolatilityStatus.Available);
    }

    private static decimal UpdateAverage(decimal average, decimal value, int count) =>
        average + (value - average) / count;

    private static decimal UpdateWilderAverage(decimal average, decimal value, int period) =>
        average + (value - average) / period;

    private static (decimal? Value, decimal? SlopePerMinute) CalculateEma(
        IReadOnlyList<TradeEvent> trades,
        int period,
        DateTimeOffset windowStart,
        bool pricesAreValid)
    {
        if (!pricesAreValid || trades.Count < period)
        {
            return (null, null);
        }

        var alpha = 2m / (period + 1m);
        decimal seedSum = 0;
        decimal? ema = null;
        var samples = new List<(DateTimeOffset EventTimeUtc, decimal Value)>();
        for (var index = 0; index < trades.Count; index++)
        {
            var trade = trades[index];
            if (!ema.HasValue)
            {
                seedSum += trade.Price;
                if (index == period - 1)
                {
                    ema = seedSum / period;
                }
            }
            else
            {
                ema = (trade.Price * alpha) + (ema.Value * (1m - alpha));
            }

            if (!ema.HasValue)
            {
                continue;
            }

            if (samples.Count > 0 && samples[^1].EventTimeUtc == trade.EventTimeUtc)
            {
                samples[^1] = (trade.EventTimeUtc, ema.Value);
            }
            else
            {
                samples.Add((trade.EventTimeUtc, ema.Value));
            }
        }

        var windowSamples = samples
            .Where(sample => sample.EventTimeUtc >= windowStart)
            .ToArray();
        if (windowSamples.Length < 2)
        {
            return (ema, null);
        }

        var first = windowSamples[0];
        var last = windowSamples[^1];
        var elapsedTicks = (last.EventTimeUtc - first.EventTimeUtc).Ticks;
        decimal? slopePerMinute = elapsedTicks > 0
            ? (last.Value - first.Value) * TimeSpan.TicksPerMinute / elapsedTicks
            : null;
        return (ema, slopePerMinute);
    }

    private static PriceStructure? CalculatePriceStructure(
        IReadOnlyList<TradeEvent> trades,
        bool pricesAreValid)
    {
        const int pivotStrength = 2;
        if (!pricesAreValid || trades.Count < (pivotStrength * 2) + 1)
        {
            return null;
        }

        List<decimal> pivotHighs = [];
        List<decimal> pivotLows = [];
        for (var index = pivotStrength; index < trades.Count - pivotStrength; index++)
        {
            var price = trades[index].Price;
            var isHigh = true;
            var isLow = true;
            for (var offset = 1; offset <= pivotStrength; offset++)
            {
                if (price <= trades[index - offset].Price)
                {
                    isHigh = false;
                }

                if (price >= trades[index - offset].Price)
                {
                    isLow = false;
                }

                if (price < trades[index + offset].Price)
                {
                    isHigh = false;
                }

                if (price > trades[index + offset].Price)
                {
                    isLow = false;
                }
            }

            if (isHigh)
            {
                pivotHighs.Add(price);
            }

            if (isLow)
            {
                pivotLows.Add(price);
            }
        }

        if (pivotHighs.Count < 2 || pivotLows.Count < 2)
        {
            return null;
        }

        var highDirection = pivotHighs[^1].CompareTo(pivotHighs[^2]);
        var lowDirection = pivotLows[^1].CompareTo(pivotLows[^2]);
        if (highDirection == 0 || lowDirection == 0)
        {
            return PriceStructure.Flat;
        }

        if (highDirection > 0 && lowDirection > 0)
        {
            return PriceStructure.HigherHighHigherLow;
        }

        if (highDirection < 0 && lowDirection < 0)
        {
            return PriceStructure.LowerHighLowerLow;
        }

        return PriceStructure.Mixed;
    }

    private static string Format(decimal? value) =>
        value?.ToString("G29", CultureInfo.InvariantCulture) ?? "";
}

internal static class DeterministicId
{
    public static Guid From(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }
}
