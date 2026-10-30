using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MarketOpportunity.Application;
using MarketOpportunity.Domain;
using Microsoft.Extensions.Configuration;

namespace MarketOpportunity.Infrastructure;

public sealed record MassiveMarketDataOptions(
    string ApiKey,
    string SessionId,
    IReadOnlyList<string> Symbols)
{
    public static MassiveMarketDataOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection("MarketData:Massive");
        var apiKey = section["ApiKey"];
        var sessionId = section["SessionId"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Massive is selected but MarketData:Massive:ApiKey is not configured.");
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException(
                "Massive is selected but MarketData:Massive:SessionId is not configured.");
        }

        return new MassiveMarketDataOptions(apiKey, sessionId, ScannerUniverse.FromConfiguration(configuration));
    }
}

public sealed class MassiveMarketDataSource(
    MassiveMarketDataOptions options,
    TimeProvider timeProvider) : ILiveMarketDataSource
{
    private static readonly Uri StreamUri = new("wss://socket.massive.com/stocks");

    public MarketDataCapabilities Capabilities { get; } = new(
        "Massive",
        "stocks",
        MarketDataCoverage.ProviderDefined,
        true,
        true,
        true);

    public async IAsyncEnumerable<MarketEvent> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(StreamUri, cancellationToken);
        await SendAsync(socket, new
        {
            action = "auth",
            @params = options.ApiKey
        }, cancellationToken);

        var subscribed = false;
        while (socket.State == WebSocketState.Open)
        {
            var payload = await ReceiveMessageAsync(socket, cancellationToken);
            using var document = JsonDocument.Parse(payload);
            foreach (var item in GetItems(document.RootElement))
            {
                if (item.TryGetProperty("ev", out var eventType)
                    && eventType.ValueKind == JsonValueKind.String
                    && eventType.GetString() == "status")
                {
                    var status = MassiveMarketDataParser.ReadStatus(item);

                    if (!subscribed && status == "auth_success")
                    {
                        var channels = options.Symbols
                            .SelectMany(symbol => new[] { $"T.{symbol}", $"Q.{symbol}" });
                        await SendAsync(socket, new
                        {
                            action = "subscribe",
                            @params = string.Join(',', channels)
                        }, cancellationToken);
                        subscribed = true;
                    }

                    continue;
                }

                foreach (var marketEvent in MassiveMarketDataParser.ParseEvents(
                             item,
                             options.SessionId,
                             timeProvider.GetUtcNow()))
                {
                    yield return marketEvent;
                }
            }
        }

        throw new InvalidOperationException("Massive closed the market-data WebSocket unexpectedly.");
    }

    private static IEnumerable<JsonElement> GetItems(JsonElement root) =>
        root.ValueKind == JsonValueKind.Array ? root.EnumerateArray() : [root];

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
                throw new InvalidOperationException("Massive closed the market-data WebSocket unexpectedly.");
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                throw new InvalidDataException("Massive sent a non-text market-data message.");
            }

            if (message.Length + result.Count > maximumMessageBytes)
            {
                throw new InvalidDataException("Massive market-data message exceeded the 1 MB safety limit.");
            }

            message.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(message.GetBuffer(), 0, checked((int)message.Length));
    }
}

public static class MassiveMarketDataParser
{
    public static string ReadStatus(JsonElement item)
    {
        if (!item.TryGetProperty("status", out var statusValue)
            || statusValue.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(statusValue.GetString()))
        {
            throw new InvalidDataException("Massive sent a status message without a status code.");
        }

        var status = statusValue.GetString()!;
        if (status == "auth_failed")
        {
            if (item.TryGetProperty("message", out var messageValue)
                && messageValue.ValueKind == JsonValueKind.String
                && messageValue.GetString()!.Contains(
                    "Your plan doesn't include websocket access",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Massive denied WebSocket access because the account plan does not include it. "
                    + "An API key can be valid for REST without WebSocket access. Verify the plan "
                    + "for the account owning the configured key at https://massive.com/pricing "
                    + "or contact Massive support. This adapter requires real-time stocks trades "
                    + "and quotes; it does not switch to delayed data.");
            }

            throw new InvalidOperationException(
                "Massive authentication failed. Verify the active API key in MarketData:Massive:ApiKey "
                + "(User Secrets in Development). Environment variable MarketData__Massive__ApiKey "
                + "overrides User Secrets. Also verify that the account plan includes WebSocket "
                + "access; a valid REST key alone does not establish WebSocket access.");
        }

        if (status == "error")
        {
            throw new InvalidOperationException(
                "Massive reported a WebSocket connection or subscription error. Check real-time "
                + "stocks trade/quote entitlements and whether another client holds the connection. "
                + "This adapter uses wss://socket.massive.com/stocks; it does not switch to delayed data.");
        }

        return status;
    }

    public static IReadOnlyList<MarketEvent> ParseEvents(
        JsonElement item,
        string sessionId,
        DateTimeOffset receivedTimeUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (!item.TryGetProperty("ev", out var eventType)
            || eventType.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("Massive market-data message has no event type.");
        }

        var symbol = RequiredString(item, "sym");
        return eventType.GetString() switch
        {
            "T" =>
            [
                new TradeEvent(
                    symbol,
                    sessionId,
                    GetTimestamp(item, "pt", "t"),
                    receivedTimeUtc,
                    "Massive",
                    OptionalScalar(item, "i"),
                    RequiredDecimal(item, "p"),
                    RequiredInt64(item, "s"))
            ],
            "Q" =>
            [
                new QuoteEvent(
                    symbol,
                    sessionId,
                    GetTimestamp(item, "t"),
                    receivedTimeUtc,
                    "Massive",
                    OptionalScalar(item, "q"),
                    OptionalDecimal(item, "bp") ?? 0m,
                    OptionalDecimal(item, "ap") ?? 0m,
                    OptionalInt64(item, "bs"),
                    OptionalInt64(item, "as"))
            ],
            var unsupported => throw new InvalidDataException(
                $"Massive sent unsupported market-data event type '{unsupported}'.")
        };
    }

    private static string RequiredString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException($"Massive market-data message is missing '{propertyName}'.");
        }

        return value.GetString()!;
    }

    private static DateTimeOffset GetTimestamp(
        JsonElement item,
        string primary,
        string? fallback = null)
    {
        if (!TryGetInt64(item, primary, out var timestamp)
            && (fallback is null || !TryGetInt64(item, fallback, out timestamp)))
        {
            throw new InvalidDataException($"Massive market-data message is missing timestamp '{primary}'.");
        }

        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(timestamp);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("Massive sent an out-of-range market-data timestamp.", exception);
        }
    }

    private static decimal RequiredDecimal(JsonElement item, string propertyName) =>
        OptionalDecimal(item, propertyName)
        ?? throw new InvalidDataException($"Massive market-data message is missing '{propertyName}'.");

    private static decimal? OptionalDecimal(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDecimal(out var result)
                ? result
                : null;

    private static long RequiredInt64(JsonElement item, string propertyName) =>
        OptionalInt64(item, propertyName)
        ?? throw new InvalidDataException($"Massive market-data message is missing '{propertyName}'.");

    private static long? OptionalInt64(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var result)
                ? result
                : null;

    private static bool TryGetInt64(JsonElement item, string propertyName, out long result)
    {
        result = default;
        return item.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out result);
    }

    private static string? OptionalScalar(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }
}
