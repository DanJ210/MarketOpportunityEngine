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
    private const int SwingPivotStrength = 2;
    private const string SwingAlgorithmVersion = "session-swing-fractal-v1";
    private const string StructureAlgorithmVersion = "session-market-structure-v1";

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
        var structure = CalculateMarketStructure(
            trades,
            hasValidSessionPrices,
            latestTrade?.Price,
            state.Symbol,
            state.SessionId,
            configuration.FeatureSetVersion);
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
            structure.PriceStructure,
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
            structure.PriceStructure?.ToString() ?? "",
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
            JsonSerializer.Serialize(marketData),
            JsonSerializer.Serialize(structure.LatestHigh),
            JsonSerializer.Serialize(structure.LatestLow),
            structure.Status.ToString(),
            JsonSerializer.Serialize(structure.PreviousHigh),
            JsonSerializer.Serialize(structure.PreviousLow),
            structure.State.ToString(),
            structure.HighRelationship.ToString(),
            structure.LowRelationship.ToString(),
            JsonSerializer.Serialize(structure.NearestSupport),
            JsonSerializer.Serialize(structure.NearestResistance),
            Format(structure.DistanceToNearestHigh),
            Format(structure.DistanceToNearestHighPercent),
            Format(structure.DistanceToNearestLow),
            Format(structure.DistanceToNearestLowPercent)
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
            structure.PriceStructure,
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
            marketData,
            structure.LatestHigh,
            structure.LatestLow,
            structure.Status,
            structure.PreviousHigh,
            structure.PreviousLow,
            structure.State,
            structure.HighRelationship,
            structure.LowRelationship,
            structure.NearestSupport,
            structure.NearestResistance,
            structure.DistanceToNearestHigh,
            structure.DistanceToNearestHighPercent,
            structure.DistanceToNearestLow,
            structure.DistanceToNearestLowPercent);
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

    private static CalculatedMarketStructure CalculateMarketStructure(
        IReadOnlyList<TradeEvent> trades,
        bool pricesAreValid,
        decimal? referencePrice,
        string symbol,
        string sessionId,
        string featureSetVersion)
    {
        if (trades.Count == 0)
        {
            return CalculatedMarketStructure.Insufficient;
        }

        if (!pricesAreValid)
        {
            return CalculatedMarketStructure.InvalidPriceData;
        }

        if (trades.Count < (SwingPivotStrength * 2) + 1)
        {
            return CalculatedMarketStructure.Insufficient;
        }

        var highIndexes = new List<int>();
        var lowIndexes = new List<int>();
        for (var index = SwingPivotStrength; index < trades.Count - SwingPivotStrength; index++)
        {
            var price = trades[index].Price;
            var isHigh = true;
            var isLow = true;
            for (var offset = 1; offset <= SwingPivotStrength; offset++)
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
                highIndexes.Add(index);
            }

            if (isLow)
            {
                lowIndexes.Add(index);
            }
        }

        var highs = highIndexes
            .Select(index => CreateConfirmedSwing(
                trades, index, ConfirmedSwingType.ConfirmedSwingHigh, symbol, sessionId, featureSetVersion)!)
            .ToArray();
        var lows = lowIndexes
            .Select(index => CreateConfirmedSwing(
                trades, index, ConfirmedSwingType.ConfirmedSwingLow, symbol, sessionId, featureSetVersion)!)
            .ToArray();
        var latestHigh = highs.LastOrDefault();
        var previousHigh = highs.Length > 1 ? highs[^2] : null;
        var latestLow = lows.LastOrDefault();
        var previousLow = lows.Length > 1 ? lows[^2] : null;
        PriceStructure? priceStructure = null;
        var highRelationship = SwingRelationship.Insufficient;
        var lowRelationship = SwingRelationship.Insufficient;
        var structureState = MarketStructureState.Insufficient;
        if (latestHigh is not null && previousHigh is not null)
        {
            highRelationship = CompareSwingPrices(latestHigh.Price, previousHigh.Price);
        }

        if (latestLow is not null && previousLow is not null)
        {
            lowRelationship = CompareSwingPrices(latestLow.Price, previousLow.Price);
        }

        if (latestHigh is not null
            && previousHigh is not null
            && latestLow is not null
            && previousLow is not null)
        {
            priceStructure = (highRelationship, lowRelationship) switch
            {
                (SwingRelationship.Equal, _) or (_, SwingRelationship.Equal) => PriceStructure.Flat,
                (SwingRelationship.Higher, SwingRelationship.Higher) => PriceStructure.HigherHighHigherLow,
                (SwingRelationship.Lower, SwingRelationship.Lower) => PriceStructure.LowerHighLowerLow,
                _ => PriceStructure.Mixed
            };
            structureState = (highRelationship, lowRelationship) switch
            {
                (SwingRelationship.Higher, SwingRelationship.Higher) => MarketStructureState.Bullish,
                (SwingRelationship.Lower, SwingRelationship.Lower) => MarketStructureState.Bearish,
                (SwingRelationship.Equal, SwingRelationship.Equal) => MarketStructureState.Range,
                _ => MarketStructureState.Mixed
            };
        }

        var nearestHigh = referencePrice is > 0 && highs.Length > 0
            ? highs.MinBy(swing => Math.Abs(swing.Price - referencePrice.Value))
            : null;
        var nearestLow = referencePrice is > 0 && lows.Length > 0
            ? lows.MinBy(swing => Math.Abs(swing.Price - referencePrice.Value))
            : null;
        var nearestSupport = referencePrice is > 0
            ? lows.Where(swing => swing.Price <= referencePrice.Value)
                .OrderByDescending(swing => swing.Price)
                .ThenByDescending(swing => swing.EventTimeUtc)
                .ThenBy(swing => swing.Id)
                .Select(swing => CreateStructuralLevel(swing, StructuralLevelType.Support))
                .FirstOrDefault()
            : null;
        var nearestResistance = referencePrice is > 0
            ? highs.Where(swing => swing.Price >= referencePrice.Value)
                .OrderBy(swing => swing.Price)
                .ThenByDescending(swing => swing.EventTimeUtc)
                .ThenBy(swing => swing.Id)
                .Select(swing => CreateStructuralLevel(swing, StructuralLevelType.Resistance))
                .FirstOrDefault()
            : null;

        return new CalculatedMarketStructure(
            priceStructure,
            latestHigh,
            latestLow,
            previousHigh,
            previousLow,
            structureState,
            highRelationship,
            lowRelationship,
            nearestSupport,
            nearestResistance,
            Distance(referencePrice, nearestHigh?.Price),
            DistancePercent(referencePrice, nearestHigh?.Price),
            Distance(referencePrice, nearestLow?.Price),
            DistancePercent(referencePrice, nearestLow?.Price),
            MarketStructureStatus.Available);
    }

    private static SwingRelationship CompareSwingPrices(decimal latest, decimal previous) =>
        latest.CompareTo(previous) switch
        {
            > 0 => SwingRelationship.Higher,
            < 0 => SwingRelationship.Lower,
            _ => SwingRelationship.Equal
        };

    private static decimal? Distance(decimal? referencePrice, decimal? levelPrice) =>
        referencePrice is > 0 && levelPrice.HasValue
            ? Math.Abs(referencePrice.Value - levelPrice.Value)
            : null;

    private static decimal? DistancePercent(decimal? referencePrice, decimal? levelPrice) =>
        referencePrice is > 0 && levelPrice.HasValue
            ? Math.Abs(referencePrice.Value - levelPrice.Value) / referencePrice.Value * 100m
            : null;

    private static StructuralLevelCandidate CreateStructuralLevel(
        ConfirmedSwing swing,
        StructuralLevelType type)
    {
        var identity = string.Join(
            "\u001f",
            "structural-level",
            type.ToString(),
            swing.Id.ToString("N"),
            StructureAlgorithmVersion);
        return new StructuralLevelCandidate(
            DeterministicId.From(identity),
            type,
            swing.Symbol,
            swing.SessionId,
            swing.Price,
            swing.EventTimeUtc,
            swing.ConfirmedAtEventTimeUtc,
            swing.Id,
            swing.OriginEventIdentity,
            swing.ConfirmationEventIdentity,
            swing.FeatureSetVersion,
            StructureAlgorithmVersion);
    }

    private static ConfirmedSwing? CreateConfirmedSwing(
        IReadOnlyList<TradeEvent> trades,
        int index,
        ConfirmedSwingType type,
        string symbol,
        string sessionId,
        string featureSetVersion)
    {
        if (index < 0)
        {
            return null;
        }

        var origin = trades[index];
        var confirmation = trades[index + SwingPivotStrength];
        var originIdentity = MarketEventIdentity.Create(origin);
        var confirmationIdentity = MarketEventIdentity.Create(confirmation);
        var identity = string.Join(
            "\u001f",
            "confirmed-swing",
            symbol,
            sessionId,
            type.ToString(),
            Format(origin.Price),
            origin.EventTimeUtc.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture),
            confirmation.EventTimeUtc.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture),
            originIdentity,
            confirmationIdentity,
            featureSetVersion,
            SwingAlgorithmVersion);
        return new ConfirmedSwing(
            DeterministicId.From(identity),
            symbol,
            sessionId,
            origin.Price,
            origin.EventTimeUtc.ToUniversalTime(),
            confirmation.EventTimeUtc.ToUniversalTime(),
            originIdentity,
            confirmationIdentity,
            type,
            featureSetVersion,
            SwingAlgorithmVersion);
    }

    private static string Format(decimal? value) =>
        value?.ToString("G29", CultureInfo.InvariantCulture) ?? "";

    private sealed record CalculatedMarketStructure(
        PriceStructure? PriceStructure,
        ConfirmedSwing? LatestHigh,
        ConfirmedSwing? LatestLow,
        ConfirmedSwing? PreviousHigh,
        ConfirmedSwing? PreviousLow,
        MarketStructureState State,
        SwingRelationship HighRelationship,
        SwingRelationship LowRelationship,
        StructuralLevelCandidate? NearestSupport,
        StructuralLevelCandidate? NearestResistance,
        decimal? DistanceToNearestHigh,
        decimal? DistanceToNearestHighPercent,
        decimal? DistanceToNearestLow,
        decimal? DistanceToNearestLowPercent,
        MarketStructureStatus Status)
    {
        public static CalculatedMarketStructure Insufficient { get; } = new(
            null, null, null, null, null,
            MarketStructureState.Insufficient,
            SwingRelationship.Insufficient,
            SwingRelationship.Insufficient,
            null, null, null, null, null, null,
            MarketStructureStatus.Insufficient);

        public static CalculatedMarketStructure InvalidPriceData { get; } = new(
            null, null, null, null, null,
            MarketStructureState.Insufficient,
            SwingRelationship.Insufficient,
            SwingRelationship.Insufficient,
            null, null, null, null, null, null,
            MarketStructureStatus.InvalidPriceData);
    }
}

internal static class DeterministicId
{
    public static Guid From(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }
}
