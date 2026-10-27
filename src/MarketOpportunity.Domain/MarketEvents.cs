using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MarketOpportunity.Domain;

public abstract record MarketEvent(
    string Symbol,
    string SessionId,
    DateTimeOffset EventTimeUtc,
    DateTimeOffset ReceivedTimeUtc,
    string Provider,
    string? SequenceId);

public sealed record TradeEvent(
    string Symbol,
    string SessionId,
    DateTimeOffset EventTimeUtc,
    DateTimeOffset ReceivedTimeUtc,
    string Provider,
    string? SequenceId,
    decimal Price,
    long Size)
    : MarketEvent(Symbol, SessionId, EventTimeUtc, ReceivedTimeUtc, Provider, SequenceId);

public sealed record QuoteEvent(
    string Symbol,
    string SessionId,
    DateTimeOffset EventTimeUtc,
    DateTimeOffset ReceivedTimeUtc,
    string Provider,
    string? SequenceId,
    decimal BidPrice,
    decimal AskPrice,
    long? BidSize,
    long? AskSize)
    : MarketEvent(Symbol, SessionId, EventTimeUtc, ReceivedTimeUtc, Provider, SequenceId);

public enum MarketDataCoverage
{
    Synthetic,
    Limited,
    Consolidated,
    ProviderDefined
}

public sealed record MarketDataCapabilities(
    string Provider,
    string Feed,
    MarketDataCoverage Coverage,
    bool IsRealTime,
    bool SupportsTrades,
    bool SupportsQuotes)
{
    public static MarketDataCapabilities Unknown { get; } = new(
        "Unknown",
        "Unknown",
        MarketDataCoverage.ProviderDefined,
        false,
        false,
        false);
}

public static class MarketEventIdentity
{
    public static string Create(MarketEvent marketEvent)
    {
        ArgumentNullException.ThrowIfNull(marketEvent);
        var kind = marketEvent switch
        {
            TradeEvent => "Trade",
            QuoteEvent => "Quote",
            _ => throw new NotSupportedException(
                $"Unsupported normalized market event type: {marketEvent.GetType().Name}.")
        };
        var identityPart = marketEvent.SequenceId is null
            ? EventContent(marketEvent)
            : $"sequence\u001f{marketEvent.SequenceId}";
        var canonical = string.Join(
            "\u001f",
            marketEvent.Provider,
            marketEvent.Symbol,
            marketEvent.SessionId,
            kind,
            identityPart);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static Guid ToGuid(string identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string EventContent(MarketEvent marketEvent) =>
        marketEvent switch
        {
            TradeEvent trade => string.Join(
                "\u001f",
                trade.EventTimeUtc.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture),
                trade.ReceivedTimeUtc.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture),
                trade.Price.ToString("G29", CultureInfo.InvariantCulture),
                trade.Size.ToString(CultureInfo.InvariantCulture)),
            QuoteEvent quote => string.Join(
                "\u001f",
                quote.EventTimeUtc.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture),
                quote.ReceivedTimeUtc.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture),
                quote.BidPrice.ToString("G29", CultureInfo.InvariantCulture),
                quote.AskPrice.ToString("G29", CultureInfo.InvariantCulture),
                quote.BidSize?.ToString(CultureInfo.InvariantCulture) ?? "",
                quote.AskSize?.ToString(CultureInfo.InvariantCulture) ?? ""),
            _ => throw new NotSupportedException(
                $"Unsupported normalized market event type: {marketEvent.GetType().Name}.")
        };
}

public enum DataQualityStatus
{
    Good,
    Degraded,
    Insufficient
}

public enum VwapSlopeDirection
{
    Rising,
    Falling,
    Flat,
    Insufficient
}

public enum PriceStructure
{
    HigherHighHigherLow,
    LowerHighLowerLow,
    Mixed,
    Flat
}

public enum AdxStatus
{
    Available,
    Insufficient,
    InvalidPriceData
}

public enum RealizedVolatilityStatus
{
    Available,
    Insufficient,
    InvalidPriceData
}

public enum TrendDirectionContext
{
    Bullish,
    Bearish,
    Neutral,
    Conflicting,
    Insufficient,
    Invalid
}

public enum MarketDataHealthStatus
{
    Connecting,
    Live,
    Stale,
    Offline,
    Error,
    Recorded
}

public sealed record FeatureSnapshot(
    Guid Id,
    string Symbol,
    string SessionId,
    DateTimeOffset AsOfUtc,
    string FeatureSetVersion,
    decimal? ReferencePrice,
    decimal? BidPrice,
    decimal? AskPrice,
    decimal? SpreadBasisPoints,
    decimal? SessionVwap,
    decimal? SessionVwapSlopePerMinute,
    VwapSlopeDirection SessionVwapSlopeDirection,
    decimal? PriceVsVwapPercent,
    decimal? ShortHorizonReturnPercent,
    decimal? SecondaryHorizonReturnPercent,
    long? ShortHorizonVolume,
    long? SessionCumulativeVolume,
    decimal? SessionHighPrice,
    decimal? SessionLowPrice,
    decimal? SessionRangePositionPercent,
    PriceStructure? SessionPriceStructure,
    decimal? SessionAdx,
    AdxStatus SessionAdxStatus,
    decimal? SessionRealizedVolatilityPercent,
    RealizedVolatilityStatus SessionRealizedVolatilityStatus,
    TrendDirectionContext SessionTrendDirection,
    decimal? FastEma,
    decimal? MediumEma,
    decimal? FastMediumEmaSpreadPercent,
    decimal? FastEmaSlopePerMinute,
    decimal? MediumEmaSlopePerMinute,
    TimeSpan? QuoteAge,
    bool IsQuoteValid,
    bool IsPriceDataValid,
    bool HasRequiredData,
    bool HasCompleteSessionData,
    DataQualityStatus DataQuality,
    MarketDataCapabilities MarketData);

public enum OpportunityClassification
{
    ProbableLong,
    ProbableShort,
    Watch,
    AvoidNoTrade
}

public enum SetupBias
{
    Long,
    Short,
    Neutral
}

public enum OpportunityLifecycleState
{
    Active,
    Suspended,
    Expired,
    Invalidated,
    NotQualified
}

public sealed record PublicationDecision(
    OpportunityLifecycleState State,
    string ReasonCode,
    string Reason,
    DateTimeOffset EvaluatedAtUtc);

public sealed record OpportunityEvidence(string Code, string Description);

public sealed record Opportunity(
    Guid Id,
    string Symbol,
    string SessionId,
    DateTimeOffset AsOfUtc,
    DateTimeOffset ValidUntilUtc,
    string SetupType,
    SetupBias Bias,
    OpportunityClassification Classification,
    int QualityScore,
    IReadOnlyList<OpportunityEvidence> SupportingEvidence,
    IReadOnlyList<OpportunityEvidence> ContradictoryEvidence,
    IReadOnlyList<string> ActiveVetoes,
    string InvalidationCondition,
    decimal? ReferencePrice,
    string FeatureSetVersion,
    string SetupDefinitionVersion,
    string ConfigurationVersion,
    MarketDataCapabilities MarketData);

public sealed record ScannerConfiguration
{
    public string Version { get; init; } = "scanner-config-v13";
    public string FeatureSetVersion { get; init; } = "features-v13";
    public string SetupDefinitionVersion { get; init; } = "momentum-continuation-v1";
    public int MaximumStateEvents { get; init; } = 10_000;
    public int MomentumWindowSeconds { get; init; } = 60;
    public int SecondaryMomentumWindowSeconds { get; init; } = 30;
    public int EmaFastPeriod { get; init; } = 5;
    public int EmaMediumPeriod { get; init; } = 20;
    public int TrendStrengthPeriod { get; init; } = 14;
    public int MaximumQuoteAgeSeconds { get; init; } = 15;
    public int MaximumLiveFeedStalenessSeconds { get; init; } = 30;
    public decimal MaximumSpreadBasisPoints { get; init; } = 25m;
    public decimal MinimumDirectionalReturnPercent { get; init; } = 0.05m;
    public int MinimumRequiredTrades { get; init; } = 3;
    public int WatchScoreThreshold { get; init; } = 35;
    public int ProbableScoreThreshold { get; init; } = 75;
    public int OpportunityExpirationSeconds { get; init; } = 120;
    public int MomentumScoreWeight { get; init; } = 50;
    public int VwapScoreWeight { get; init; } = 35;
    public int ActivityScoreWeight { get; init; } = 15;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Version);
        ArgumentException.ThrowIfNullOrWhiteSpace(FeatureSetVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(SetupDefinitionVersion);
        if (MaximumStateEvents < 1
            || MomentumWindowSeconds < 1
            || SecondaryMomentumWindowSeconds < 1
            || SecondaryMomentumWindowSeconds >= MomentumWindowSeconds
            || EmaFastPeriod < 1
            || EmaMediumPeriod <= EmaFastPeriod
            || TrendStrengthPeriod < 1
            || MaximumQuoteAgeSeconds < 0
            || MaximumLiveFeedStalenessSeconds < 1
            || MaximumSpreadBasisPoints <= 0
            || MinimumDirectionalReturnPercent <= 0
            || MinimumRequiredTrades < 2
            || OpportunityExpirationSeconds < 1
            || WatchScoreThreshold is < 0 or > 100
            || ProbableScoreThreshold is < 0 or > 100
            || WatchScoreThreshold > ProbableScoreThreshold
            || MomentumScoreWeight is < 0 or > 100
            || VwapScoreWeight is < 0 or > 100
            || ActivityScoreWeight is < 0 or > 100
            || (long)MomentumScoreWeight + VwapScoreWeight + ActivityScoreWeight != 100)
        {
            throw new InvalidOperationException(
                $"Scanner configuration '{Version}' has invalid thresholds or scoring weights.");
        }
    }
}
