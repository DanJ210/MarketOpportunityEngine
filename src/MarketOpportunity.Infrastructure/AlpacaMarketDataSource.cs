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

        return new AlpacaMarketDataOptions(
            apiKey, secretKey, feed.ToLowerInvariant(), sessionId, ScannerUniverse.FromConfiguration(configuration));
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
    TimeProvider timeProvider,
    LiveValidationDiagnostics? diagnostics = null) : IReconnectableMarketDataSource
{
    private static readonly Uri StreamBaseUri = new("wss://stream.data.alpaca.markets/v2/");
    private int _connectionAttempts;

    public MarketDataCapabilities Capabilities => options.Capabilities;

    public async IAsyncEnumerable<MarketEvent> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        diagnostics?.Configure(options.SessionId, options.Feed, options.Symbols);
        var reconnect = Interlocked.Increment(ref _connectionAttempts) > 1;
        var connectionId = diagnostics?.Connecting() ?? Guid.Empty;
        if (reconnect)
        {
            diagnostics?.ReconnectAttempt();
            diagnostics?.Transition(connectionId, "ReconnectAttempt");
        }
        using var socket = new ClientWebSocket();
        try
        {
            var streamUri = new Uri(StreamBaseUri, options.Feed);
            await socket.ConnectAsync(streamUri, cancellationToken);
            diagnostics?.Connected(connectionId);
            await SendAsync(socket, new
            {
                action = "auth",
                key = options.ApiKey,
                secret = options.SecretKey
            }, cancellationToken);

            var authenticated = false;
            var live = false;
            while (socket.State == WebSocketState.Open)
            {
                var message = await ReceiveMessageAsync(socket, timeProvider, cancellationToken, diagnostics);
                using var document = ParseDocument(message.Payload, connectionId);
                var marketDataMessage = false;
                foreach (var item in AlpacaMarketDataParser.GetItems(document.RootElement))
                {
                    var eventType = ReadString(item, "T", connectionId);
                    if (eventType == "success")
                    {
                        var success = ReadString(item, "msg", connectionId);
                        if (success == "authenticated" && !authenticated)
                        {
                            diagnostics?.Transition(connectionId, "Authenticated");
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
                        if (diagnostics is not null)
                        {
                            diagnostics.Subscription(ReadSymbols(item, "trades"), ReadSymbols(item, "quotes"));
                            diagnostics.Transition(connectionId, "Subscribed");
                        }
                        continue;
                    }

                    if (!marketDataMessage && eventType is "t" or "q")
                    {
                        diagnostics?.MarketDataMessage();
                        marketDataMessage = true;
                    }
                    MarketEvent marketEvent;
                    try
                    {
                        marketEvent = AlpacaMarketDataParser.ParseEvent(
                            item, options.SessionId, timeProvider.GetUtcNow());
                        diagnostics?.Produced(
                            marketEvent, connectionId, message.ReceivedUtc,
                            AlpacaMarketDataParser.ReadDiagnosticMetadata(item), message.ReceivedTimestamp);
                    }
                    catch (Exception error) when (error is JsonException or InvalidDataException)
                    {
                        diagnostics?.ParserFailed(connectionId, error);
                        throw;
                    }
                    if (!live)
                    {
                        diagnostics?.Transition(connectionId, reconnect ? "Reconnected" : "Live");
                        if (reconnect) { diagnostics?.Transition(connectionId, "Live"); }
                        live = true;
                    }
                    yield return marketEvent;
                }
            }

            throw new InvalidOperationException("Alpaca closed the market-data WebSocket unexpectedly.");
        }
        finally
        {
            diagnostics?.Transition(connectionId, "Disconnected",
                cancellationToken.IsCancellationRequested ? "CancellationRequested" : "StreamEndedOrFailed");
        }
    }

    private JsonDocument ParseDocument(string payload, Guid connection)
    {
        try { return JsonDocument.Parse(payload); }
        catch (JsonException error)
        {
            diagnostics?.ParserFailed(connection, error);
            throw;
        }
    }

    private string ReadString(JsonElement item, string property, Guid connection)
    {
        try { return AlpacaMarketDataParser.RequiredString(item, property); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException)
        {
            diagnostics?.ParserFailed(connection, error);
            throw;
        }
    }

    private static string[] ReadSymbols(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(symbol => symbol.ValueKind == JsonValueKind.String)
                .Take(256).Select(symbol => LiveValidationDiagnostics.SafeText(symbol.GetString()!)).ToArray()
            : [];

    private static async Task SendAsync<T>(
        ClientWebSocket socket,
        T message,
        CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    public static async Task<(string Payload, DateTimeOffset ReceivedUtc, long ReceivedTimestamp)> ReceiveMessageAsync(
        WebSocket socket,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        LiveValidationDiagnostics? diagnostics = null)
    {
        const int maximumMessageBytes = 1_048_576;
        var buffer = new byte[8_192];
        using var message = new MemoryStream();
        WebSocketReceiveResult result;
        var receivedUtc = default(DateTimeOffset);
        long receivedTimestamp = 0;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.EndOfMessage)
            {
                receivedUtc = timeProvider.GetUtcNow();
                receivedTimestamp = timeProvider.GetTimestamp();
            }
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

        diagnostics?.MessageReceived();
        return (Encoding.UTF8.GetString(message.GetBuffer(), 0, checked((int)message.Length)),
            receivedUtc, receivedTimestamp);
    }
}

public static class AlpacaMarketDataParser
{
    public static ProviderEventMetadata ReadDiagnosticMetadata(JsonElement item)
    {
        var incomplete = false;
        string? ReadString(string name)
        {
            if (!item.TryGetProperty(name, out var value)) { return null; }
            if (value.ValueKind != JsonValueKind.String)
            {
                incomplete = true;
                return null;
            }
            var text = value.GetString()!;
            var safe = LiveValidationDiagnostics.SafeText(text);
            incomplete |= safe != text;
            return safe;
        }
        var bid = ReadString("bx");
        var ask = ReadString("ax");
        var tape = ReadString("z");
        var conditions = new List<string>();
        var conditionsSupplied = item.TryGetProperty("c", out var array);
        if (conditionsSupplied)
        {
            if (array.ValueKind != JsonValueKind.Array) { incomplete = true; }
            else
            {
                foreach (var value in array.EnumerateArray())
                {
                    if (conditions.Count == 16) { incomplete = true; break; }
                    if (value.ValueKind != JsonValueKind.String) { incomplete = true; continue; }
                    var text = value.GetString()!;
                    var safe = LiveValidationDiagnostics.SafeText(text);
                    incomplete |= safe != text;
                    conditions.Add(safe);
                }
            }
        }
        return new ProviderEventMetadata(bid, ask, conditions.ToArray(), tape, incomplete, conditionsSupplied);
    }

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
