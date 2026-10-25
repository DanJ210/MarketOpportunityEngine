using System.Runtime.CompilerServices;
using System.Text.Json;
using MarketOpportunity.Application;
using MarketOpportunity.Domain;

namespace MarketOpportunity.Infrastructure;

public sealed class FixtureMarketDataSource(string fixturePath) : IMarketDataSource
{
    public MarketDataCapabilities Capabilities { get; } = new(
        "RecordedFixture",
        "Synthetic",
        MarketDataCoverage.Synthetic,
        false,
        true,
        true);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async IAsyncEnumerable<MarketEvent> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(fixturePath);
        var fixtureEvents = await JsonSerializer.DeserializeAsync<List<FixtureMarketEvent>>(
            stream,
            SerializerOptions,
            cancellationToken) ?? throw new InvalidDataException("The market-data fixture is empty or invalid.");

        foreach (var item in fixtureEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentException.ThrowIfNullOrWhiteSpace(item.Symbol);
            ArgumentException.ThrowIfNullOrWhiteSpace(item.SessionId);
            ArgumentException.ThrowIfNullOrWhiteSpace(item.Provider);
            yield return item.EventType switch
            {
                "Trade" when item.TradePrice.HasValue && item.TradeSize.HasValue =>
                    new TradeEvent(
                        item.Symbol,
                        item.SessionId,
                        item.EventTimeUtc,
                        item.ReceivedTimeUtc,
                        item.Provider,
                        item.SequenceId,
                        item.TradePrice.Value,
                        item.TradeSize.Value),
                "Quote" when item.BidPrice.HasValue && item.AskPrice.HasValue =>
                    new QuoteEvent(
                        item.Symbol,
                        item.SessionId,
                        item.EventTimeUtc,
                        item.ReceivedTimeUtc,
                        item.Provider,
                        item.SequenceId,
                        item.BidPrice.Value,
                        item.AskPrice.Value,
                        item.BidSize,
                        item.AskSize),
                _ => throw new InvalidDataException(
                    $"Fixture event '{item.SequenceId}' has an unsupported type or missing required fields.")
            };
        }
    }

    private sealed record FixtureMarketEvent(
        string EventType,
        string Symbol,
        string SessionId,
        DateTimeOffset EventTimeUtc,
        DateTimeOffset ReceivedTimeUtc,
        string Provider,
        string? SequenceId,
        decimal? TradePrice,
        long? TradeSize,
        decimal? BidPrice,
        decimal? AskPrice,
        long? BidSize,
        long? AskSize);
}
