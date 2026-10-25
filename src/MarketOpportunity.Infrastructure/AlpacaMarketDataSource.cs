using System.Globalization;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MarketOpportunity.Application;
using MarketOpportunity.Domain;
using Microsoft.Extensions.Configuration;

namespace MarketOpportunity.Infrastructure;

public sealed record AlpacaMarketDataOptions(
    string ApiKey,
    string SecretKey,
    string Feed,
    string SessionId,
    IReadOnlyList<string> Symbols)
{
    private static readonly HashSet<string> SupportedFeeds = new(StringComparer.OrdinalIgnoreCase)
    {
        "test",
        "iex",
        "sip",
        "delayed_sip"
    };

    public static AlpacaMarketDataOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection("MarketData:Alpaca");
        var apiKey = section["ApiKey"];
        var secretKey = section["SecretKey"];
        var feed = section["Feed"] ?? "test";
        var sessionId = section["SessionId"];
        var symbols = section["Symbols"]?
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Alpaca is selected but MarketData:Alpaca:ApiKey is not configured.");
        }

        if (string.IsNullOrWhiteSpace(secretKey))
        {
            throw new InvalidOperationException(
                "Alpaca is selected but MarketData:Alpaca:SecretKey is not configured.");
        }

        if (!SupportedFeeds.Contains(feed))
        {
            throw new InvalidOperationException(
                "MarketData:Alpaca:Feed must be one of test, iex, sip, or delayed_sip.");
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException(
                "Alpaca is selected but MarketData:Alpaca:SessionId is not configured.");
        }

        if (symbols.Length == 0)
        {
            throw new InvalidOperationException(
                "Alpaca is selected but MarketData:Alpaca:Symbols has no configured symbols.");
        }

        return new AlpacaMarketDataOptions(apiKey, secretKey, feed.ToLowerInvariant(), sessionId, symbols);
    }

    public MarketDataCapabilities Capabilities => Feed switch
    {
        "test" => new MarketDataCapabilities(
            "Alpaca", Feed, MarketDataCoverage.Synthetic, true, true, true),
        "iex" => new MarketDataCapabilities(
            "Alpaca", Feed, MarketDataCoverage.Limited, true, true, true),
        "sip" => new MarketDataCapabilities(
            "Alpaca", Feed, MarketDataCoverage.Consolidated, true, true, true),
        "delayed_sip" => new MarketDataCapabilities(
            "Alpaca", Feed, MarketDataCoverage.Consolidated, false, true, true),
        _ => throw new InvalidOperationException("The Alpaca feed options were not validated.")
    };
}

public sealed class AlpacaMarketDataSource(
    AlpacaMarketDataOptions options,
    TimeProvider timeProvider) : ILiveMarketDataSource
{
    private static readonly Uri StreamBaseUri = new("wss://stream.data.alpaca.markets/v2/");

    public MarketDataCapabilities Capabilities => options.Capabilities;

    public async IAsyncEnumerable<MarketEvent> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        var streamUri = new Uri(StreamBaseUri, options.Feed);
        await socket.ConnectAsync(streamUri, cancellationToken);
        await SendAsync(socket, new
        {
            action = "auth",
            key = options.ApiKey,
            secret = options.SecretKey
        }, cancellationToken);

        var authenticated = false;
        while (socket.State == WebSocketState.Open)
        {
            var payload = await ReceiveMessageAsync(socket, cancellationToken);
            using var document = JsonDocument.Parse(payload);
            foreach (var item in AlpacaMarketDataParser.GetItems(document.RootElement))
            {
                var eventType = AlpacaMarketDataParser.RequiredString(item, "T");
                if (eventType == "success")
                {
                    var message = AlpacaMarketDataParser.RequiredString(item, "msg");
                    if (message == "authenticated" && !authenticated)
                    {
                        await SendAsync(socket, new
                        {
                            action = "subscribe",
                            trades = options.Symbols,
                            quotes = options.Symbols
                        }, cancellationToken);
                        authenticated = true;
                    }

                    continue;
                }

                if (eventType == "error")
                {
                    throw new InvalidOperationException(
                        "Alpaca rejected the market-data WebSocket connection or subscription. "
                        + "Verify the configured feed entitlement and connection limit.");
                }

                if (eventType == "subscription")
                {
                    continue;
                }

                yield return AlpacaMarketDataParser.ParseEvent(
                    item,
                    options.SessionId,
                    timeProvider.GetUtcNow());
            }
        }

        throw new InvalidOperationException("Alpaca closed the market-data WebSocket unexpectedly.");
    }

    private static async Task SendAsync<T>(
        ClientWebSocket socket,
        T message,
        CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task<string> ReceiveMessageAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        const int maximumMessageBytes = 1_048_576;
        var buffer = new byte[8_192];
        using var message = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException("Alpaca closed the market-data WebSocket unexpectedly.");
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                throw new InvalidDataException("Alpaca sent a non-text market-data message.");
            }

            if (message.Length + result.Count > maximumMessageBytes)
            {
                throw new InvalidDataException("Alpaca market-data message exceeded the 1 MB safety limit.");
            }

            message.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(message.GetBuffer(), 0, checked((int)message.Length));
    }
}

public static class AlpacaMarketDataParser
{
    public static IReadOnlyList<JsonElement> GetItems(JsonElement root) =>
        root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray().Select(item => item.Clone()).ToArray()
            : [root.Clone()];

    public static MarketEvent ParseEvent(
        JsonElement item,
        string sessionId,
        DateTimeOffset receivedTimeUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var symbol = RequiredString(item, "S");
        var timestamp = ParseTimestamp(RequiredString(item, "t"));
        var eventType = RequiredString(item, "T");
        return eventType switch
        {
            "t" => new TradeEvent(
                symbol,
                sessionId,
                timestamp,
                receivedTimeUtc,
                "Alpaca",
                RequiredInt64(item, "i").ToString(CultureInfo.InvariantCulture),
                RequiredDecimal(item, "p"),
                RequiredInt64(item, "s")),
            "q" => new QuoteEvent(
                symbol,
                sessionId,
                timestamp,
                receivedTimeUtc,
                "Alpaca",
                null,
                RequiredDecimal(item, "bp"),
                RequiredDecimal(item, "ap"),
                OptionalInt64(item, "bs"),
                OptionalInt64(item, "as")),
            _ => throw new InvalidDataException(
                $"Alpaca sent unsupported market-data event type '{eventType}'.")
        };
    }

    private static DateTimeOffset ParseTimestamp(string timestamp)
    {
        var decimalPoint = timestamp.IndexOf('.');
        if (decimalPoint >= 0)
        {
            var timezoneStart = timestamp.IndexOfAny(['Z', '+', '-'], decimalPoint + 1);
            var fractionEnd = timezoneStart < 0 ? timestamp.Length : timezoneStart;
            var fractionLength = fractionEnd - decimalPoint - 1;
            if (fractionLength > 7)
            {
                timestamp = string.Concat(
                    timestamp.AsSpan(0, decimalPoint + 8),
                    timestamp.AsSpan(fractionEnd));
            }
        }

        if (!DateTimeOffset.TryParse(
                timestamp,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            throw new InvalidDataException("Alpaca sent an invalid RFC-3339 event timestamp.");
        }

        return parsed;
    }

    internal static string RequiredString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException($"Alpaca message is missing '{propertyName}'.");
        }

        return value.GetString()!;
    }

    private static decimal RequiredDecimal(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDecimal(out var result)
            ? result
            : throw new InvalidDataException($"Alpaca message is missing numeric '{propertyName}'.");

    private static long RequiredInt64(JsonElement item, string propertyName) =>
        OptionalInt64(item, propertyName)
        ?? throw new InvalidDataException($"Alpaca message is missing integer '{propertyName}'.");

    private static long? OptionalInt64(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var result)
            ? result
            : null;
}
