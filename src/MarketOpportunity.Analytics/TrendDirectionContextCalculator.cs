using MarketOpportunity.Domain;

namespace MarketOpportunity.Analytics;

public static class TrendDirectionContextCalculator
{
    public static TrendDirectionContext Calculate(
        decimal? fastEma,
        decimal? mediumEma,
        decimal? fastMediumEmaSpreadPercent,
        decimal? fastEmaSlopePerMinute,
        decimal? mediumEmaSlopePerMinute,
        decimal? sessionVwapSlopePerMinute,
        VwapSlopeDirection sessionVwapSlopeDirection,
        PriceStructure? sessionPriceStructure,
        decimal? sessionAdx,
        AdxStatus sessionAdxStatus)
    {
        if (!IsValidEmaInput(
                fastEma,
                mediumEma,
                fastMediumEmaSpreadPercent,
                fastEmaSlopePerMinute,
                mediumEmaSlopePerMinute)
            || !IsValidVwapInput(sessionVwapSlopePerMinute, sessionVwapSlopeDirection)
            || !IsValidPriceStructure(sessionPriceStructure)
            || !IsValidAdxInput(sessionAdx, sessionAdxStatus))
        {
            return TrendDirectionContext.Invalid;
        }

        if (sessionAdxStatus == AdxStatus.InvalidPriceData)
        {
            return TrendDirectionContext.Invalid;
        }

        var bullish = false;
        var bearish = false;
        var hasDirectionalInput = false;
        var conflictingStructure = false;

        AddDirection(fastMediumEmaSpreadPercent, ref bullish, ref bearish, ref hasDirectionalInput);
        AddDirection(fastEmaSlopePerMinute, ref bullish, ref bearish, ref hasDirectionalInput);
        AddDirection(mediumEmaSlopePerMinute, ref bullish, ref bearish, ref hasDirectionalInput);

        switch (sessionVwapSlopeDirection)
        {
            case VwapSlopeDirection.Rising:
                bullish = true;
                hasDirectionalInput = true;
                break;
            case VwapSlopeDirection.Falling:
                bearish = true;
                hasDirectionalInput = true;
                break;
            case VwapSlopeDirection.Flat:
                hasDirectionalInput = true;
                break;
        }

        switch (sessionPriceStructure)
        {
            case PriceStructure.HigherHighHigherLow:
                bullish = true;
                hasDirectionalInput = true;
                break;
            case PriceStructure.LowerHighLowerLow:
                bearish = true;
                hasDirectionalInput = true;
                break;
            case PriceStructure.Mixed:
                hasDirectionalInput = true;
                conflictingStructure = true;
                break;
            case PriceStructure.Flat:
                hasDirectionalInput = true;
                break;
        }

        if (conflictingStructure || (bullish && bearish))
        {
            return TrendDirectionContext.Conflicting;
        }

        if (bullish)
        {
            return TrendDirectionContext.Bullish;
        }

        if (bearish)
        {
            return TrendDirectionContext.Bearish;
        }

        return hasDirectionalInput
            ? TrendDirectionContext.Neutral
            : TrendDirectionContext.Insufficient;
    }

    private static bool IsValidEmaInput(
        decimal? fastEma,
        decimal? mediumEma,
        decimal? spreadPercent,
        decimal? fastSlope,
        decimal? mediumSlope)
    {
        if (fastEma is <= 0m || mediumEma is <= 0m)
        {
            return false;
        }

        if (spreadPercent.HasValue != (fastEma.HasValue && mediumEma.HasValue)
            || (fastSlope.HasValue && !fastEma.HasValue)
            || (mediumSlope.HasValue && !mediumEma.HasValue))
        {
            return false;
        }

        return true;
    }

    private static bool IsValidVwapInput(
        decimal? slopePerMinute,
        VwapSlopeDirection direction) =>
        direction switch
        {
            VwapSlopeDirection.Rising => slopePerMinute is > 0m,
            VwapSlopeDirection.Falling => slopePerMinute is < 0m,
            VwapSlopeDirection.Flat => slopePerMinute is 0m,
            VwapSlopeDirection.Insufficient => !slopePerMinute.HasValue,
            _ => false
        };

    private static bool IsValidPriceStructure(PriceStructure? structure) =>
        structure is null
        or PriceStructure.HigherHighHigherLow
        or PriceStructure.LowerHighLowerLow
        or PriceStructure.Mixed
        or PriceStructure.Flat;

    private static bool IsValidAdxInput(decimal? adx, AdxStatus status) =>
        status switch
        {
            AdxStatus.Available => adx is >= 0m and <= 100m,
            AdxStatus.Insufficient or AdxStatus.InvalidPriceData => !adx.HasValue,
            _ => false
        };

    private static void AddDirection(
        decimal? value,
        ref bool bullish,
        ref bool bearish,
        ref bool hasDirectionalInput)
    {
        if (!value.HasValue)
        {
            return;
        }

        hasDirectionalInput = true;
        bullish |= value.Value > 0m;
        bearish |= value.Value < 0m;
    }
}
