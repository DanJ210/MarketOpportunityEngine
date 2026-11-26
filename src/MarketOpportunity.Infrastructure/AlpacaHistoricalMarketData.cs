using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MarketOpportunity.Application;
using MarketOpportunity.Domain;
using Microsoft.Extensions.Configuration;

namespace MarketOpportunity.Infrastructure;

public sealed record AlpacaHistoricalMarketDataOptions(
    string ApiKey,
    string SecretKey,
    int MaximumPages = 100,
    int MaximumRecords = 1_000_000,
    int PageSize = 10_000,
    int CalendarLookbackDays = 14)
{
    public static AlpacaHistoricalMarketDataOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection("MarketData:Alpaca");
        var options = new AlpacaHistoricalMarketDataOptions(
            section["ApiKey"] ?? "",
            section["SecretKey"] ?? "",
            ReadInt(section["Historical:MaximumPages"], 100),
            ReadInt(section["Historical:MaximumRecords"], 1_000_000),
            ReadInt(section["Historical:PageSize"], 10_000),
            ReadInt(section["Historical:CalendarLookbackDays"], 14));
        options.Validate();
        return options;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                "Alpaca historical access requires MarketData:Alpaca:ApiKey.");
        }

        if (string.IsNullOrWhiteSpace(SecretKey))
        {
            throw new InvalidOperationException(
                "Alpaca historical access requires MarketData:Alpaca:SecretKey.");
        }

        if (MaximumPages < 1
            || MaximumRecords < 1
            || PageSize < 1
            || PageSize > 10_000
            || CalendarLookbackDays < 1)
        {
            throw new InvalidOperationException(
                "Alpaca historical limits must be positive, and PageSize cannot exceed 10000.");
        }
    }

    private static int ReadInt(string? configured, int defaultValue) =>
        configured is null
            ? defaultValue
            : int.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : throw new InvalidOperationException("Alpaca historical numeric configuration must contain integers.");
}

public sealed class AlpacaMarketSessionCalendar(
    HttpClient httpClient,
    AlpacaHistoricalMarketDataOptions options) : IMarketSessionCalendar
{
    public const string CalendarId = "alpaca-us-equity-calendar-v2";
    public const string CalendarVersion = "alpaca-calendar-api-v2";
    private const int MaximumCalendarResponseBytes = 1_048_576;
    private static readonly TimeZoneInfo EasternTimeZone = FindEasternTimeZone();

    public async Task<MarketSessionCalendarResolution> ResolvePreviousSessionAsync(
        string symbol,
        string currentSessionId,
        DateTimeOffset currentEventTimeUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentSessionId);
        options.Validate();
        var referenceDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(currentEventTimeUtc, EasternTimeZone).DateTime);
        var rangeStart = referenceDate.AddDays(-options.CalendarLookbackDays);
        var uri = new Uri(
            $"v2/calendar?start={rangeStart:yyyy-MM-dd}&end={referenceDate:yyyy-MM-dd}",
            UriKind.Relative);
        try
        {
            using var request = CreateAuthenticatedRequest(options, HttpMethod.Get, uri);
            using var response = await httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var failure = CalendarFailure(response.StatusCode);
            if (failure is { } failureStatus)
            {
                return Resolution(failureStatus, $"Alpaca calendar request failed with HTTP {(int)response.StatusCode}.");
            }

            var payload = await AlpacaHistoricalHttp.ReadLimitedAsync(
                response.Content,
                MaximumCalendarResponseBytes,
                cancellationToken);
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Resolution(
                    MarketSessionCalendarStatus.MalformedResponse,
                    "Alpaca calendar response must be an array.");
            }

            var sessions = ParseSessions(document.RootElement);
            if (sessions.GroupBy(item => item.TradingDate).Any(group => group.Count() != 1))
            {
                return Resolution(
                    MarketSessionCalendarStatus.MalformedResponse,
                    "Alpaca calendar response contains duplicate trading dates.");
            }

            var current = sessions.SingleOrDefault(item => item.TradingDate == referenceDate);
            if (current is null)
            {
                return Resolution(
                    MarketSessionCalendarStatus.InvalidReference,
                    $"The Alpaca calendar has no regular session for reference date {referenceDate:yyyy-MM-dd} in the returned range.");
            }

            var currentWithConfiguredId = current with { SessionId = currentSessionId };
            var previous = sessions
                .Where(item => item.TradingDate < referenceDate)
                .OrderByDescending(item => item.TradingDate)
                .FirstOrDefault();
            return new MarketSessionCalendarResolution(
                currentWithConfiguredId,
                previous,
                CalendarId,
                previous is null
                    ? "No immediately preceding valid regular session is available in the returned calendar range."
                    : null,
                previous is null
                    ? MarketSessionCalendarStatus.NoPreviousSession
                    : MarketSessionCalendarStatus.Success);
        }
        catch (OperationCanceledException)
        {
            return Resolution(MarketSessionCalendarStatus.Cancelled, "Alpaca calendar request was cancelled.");
        }
        catch (Exception exception) when (exception is JsonException
                                          or InvalidDataException
                                          or FormatException
                                          or OverflowException)
        {
            return Resolution(
                MarketSessionCalendarStatus.MalformedResponse,
                $"Alpaca calendar response is malformed ({exception.GetType().Name}).");
        }
        catch (HttpRequestException exception)
        {
            return Resolution(
                MarketSessionCalendarStatus.ProviderFailure,
                $"Alpaca calendar request failed ({exception.GetType().Name}).");
        }
    }

    private static MarketSessionCalendarResolution Resolution(
        MarketSessionCalendarStatus status,
        string message) =>
        new(null, null, CalendarId, message, status);

    private static MarketSessionCalendarStatus? CalendarFailure(HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.OK => null,
            HttpStatusCode.Unauthorized => MarketSessionCalendarStatus.AuthenticationFailure,
            HttpStatusCode.Forbidden => MarketSessionCalendarStatus.EntitlementFailure,
            HttpStatusCode.RequestedRangeNotSatisfiable => MarketSessionCalendarStatus.LimitExceeded,
            _ => MarketSessionCalendarStatus.ProviderFailure
        };

    private static MarketSessionDefinition[] ParseSessions(JsonElement root)
    {
        var sessions = new List<MarketSessionDefinition>();
        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Alpaca calendar entries must be objects.");
            }

            var dateText = RequiredString(item, "date");
            var openText = RequiredString(item, "open");
            var closeText = RequiredString(item, "close");
            if (!DateOnly.TryParseExact(
                    dateText,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var tradingDate)
                || !TimeOnly.TryParseExact(
                    openText,
                    "HH:mm",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var open)
                || !TimeOnly.TryParseExact(
                    closeText,
                    "HH:mm",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var close))
            {
                throw new InvalidDataException("Alpaca calendar entry has invalid date or regular-session times.");
            }

            var openAtUtc = ToUtc(tradingDate, open);
            var closeAtUtc = ToUtc(tradingDate, close);
            if (openAtUtc >= closeAtUtc)
            {
                throw new InvalidDataException("Alpaca calendar entry has non-positive regular-session bounds.");
            }

            sessions.Add(new MarketSessionDefinition(
                $"alpaca-us-regular-{tradingDate:yyyy-MM-dd}",
                tradingDate,
                openAtUtc,
                closeAtUtc,
                "Alpaca",
                "market-calendar-rest",
                CalendarVersion,
                MarketSessionType.Regular,
                MarketSessionValidity.Valid));
        }

        return sessions
            .OrderBy(item => item.TradingDate)
            .ToArray();
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeOnly localTime)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(localTime), DateTimeKind.Unspecified);
        return new DateTimeOffset(
            TimeZoneInfo.ConvertTimeToUtc(local, EasternTimeZone),
            TimeSpan.Zero);
    }

    private static TimeZoneInfo FindEasternTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(
        AlpacaHistoricalMarketDataOptions options,
        HttpMethod method,
        Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("APCA-API-KEY-ID", options.ApiKey);
        request.Headers.TryAddWithoutValidation("APCA-API-SECRET-KEY", options.SecretKey);
        return request;
    }

    private static string RequiredString(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()!
            : throw new InvalidDataException($"Alpaca calendar entry is missing '{propertyName}'.");
}

public sealed class AlpacaHistoricalMarketDataSource(
    HttpClient httpClient,
    AlpacaHistoricalMarketDataOptions options,
    TimeProvider timeProvider) : IHistoricalMarketDataSource
{
    public const string Feed = "sip";
    public const string SourceKind = "historical-rest";
    private const int MaximumPageResponseBytes = 8_388_608;

    public async Task<HistoricalMarketSessionData?> ReadSessionAsync(
        string symbol,
        MarketSession session,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeProvider);
        options.Validate();
        symbol = symbol.Trim().ToUpperInvariant();
        var request = new HistoricalDataRequest(
            symbol, session.SessionId, session.StartUtc.ToUniversalTime(), session.EndUtc.ToUniversalTime());
        var retrievedAtUtc = timeProvider.GetUtcNow();
        var records = new Dictionary<string, TradeEvent>(StringComparer.Ordinal);
        var repeatedTokens = new HashSet<string>(StringComparer.Ordinal);
        string? pageToken = null;
        var pages = 0;
        var duplicates = 0;
        var status = HistoricalRetrievalStatus.SuccessPaginationExhausted;
        string? statusMessage = null;
        var paginationExhausted = false;

        while (true)
        {
            if (pages >= options.MaximumPages)
            {
                status = HistoricalRetrievalStatus.LimitExceeded;
                statusMessage = $"Alpaca historical pagination exceeded the configured maximum of {options.MaximumPages} pages.";
                break;
            }

            try
            {
                var uri = CreateTradesUri(request, pageToken);
                using var httpRequest = CreateAuthenticatedRequest(HttpMethod.Get, uri);
                using var response = await httpClient.SendAsync(
                    httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                pages++;
                var failure = HistoricalFailure(response.StatusCode);
                if (failure is { } failureStatus)
                {
                    status = failureStatus;
                    statusMessage = $"Alpaca historical trade request failed with HTTP {(int)response.StatusCode}.";
                    break;
                }

                var payload = await AlpacaHistoricalHttp.ReadLimitedAsync(
                    response.Content,
                    MaximumPageResponseBytes,
                    cancellationToken);
                using var document = JsonDocument.Parse(payload);
                var page = ParsePage(document.RootElement);
                var pageReceivedAtUtc = timeProvider.GetUtcNow();
                foreach (var dto in page.Trades)
                {
                    var trade = AlpacaHistoricalTradeMapper.ToTrade(
                        dto, symbol, session.SessionId, pageReceivedAtUtc);
                    if (trade.EventTimeUtc < request.StartUtc || trade.EventTimeUtc > request.EndUtc)
                    {
                        status = HistoricalRetrievalStatus.MalformedResponse;
                        statusMessage = "Alpaca returned a historical trade outside the requested regular-session bounds.";
                        break;
                    }

                    if (records.TryGetValue(trade.SequenceId!, out var existing))
                    {
                        if (!SameHistoricalRecord(existing, trade))
                        {
                            status = HistoricalRetrievalStatus.MalformedResponse;
                            statusMessage = "Alpaca returned conflicting records for one canonical historical trade identity.";
                            break;
                        }

                        duplicates++;
                        continue;
                    }

                    if (records.Count >= options.MaximumRecords)
                    {
                        status = HistoricalRetrievalStatus.LimitExceeded;
                        statusMessage = $"Alpaca historical retrieval exceeded the configured maximum of {options.MaximumRecords} records.";
                        break;
                    }

                    records.Add(trade.SequenceId!, trade);
                }

                if (status != HistoricalRetrievalStatus.SuccessPaginationExhausted)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(page.NextPageToken))
                {
                    paginationExhausted = true;
                    break;
                }

                if (!repeatedTokens.Add(page.NextPageToken))
                {
                    status = HistoricalRetrievalStatus.MalformedResponse;
                    statusMessage = "Alpaca repeated a historical pagination token.";
                    break;
                }

                pageToken = page.NextPageToken;
            }
            catch (OperationCanceledException)
            {
                status = HistoricalRetrievalStatus.Cancelled;
                statusMessage = "Alpaca historical trade request was cancelled.";
                break;
            }
            catch (HttpRequestException exception)
            {
                status = HistoricalRetrievalStatus.ProviderFailure;
                statusMessage = $"Alpaca historical trade request failed ({exception.GetType().Name}).";
                break;
            }
            catch (Exception exception) when (exception is JsonException
                                              or InvalidDataException
                                              or FormatException
                                              or OverflowException)
            {
                status = HistoricalRetrievalStatus.MalformedResponse;
                statusMessage = $"Alpaca historical trade response is malformed ({exception.GetType().Name}).";
                break;
            }
        }

        var events = records.Values
            .OrderBy(item => item.EventTimeUtc)
            .ThenBy(item => item.SequenceId, StringComparer.Ordinal)
            .ToArray();
        var transportComplete = status == HistoricalRetrievalStatus.SuccessPaginationExhausted
            && paginationExhausted;
        return new HistoricalMarketSessionData(
            events,
            new MarketDataCapabilities(
                "Alpaca",
                Feed,
                MarketDataCoverage.Consolidated,
                false,
                true,
                false),
            SourceKind,
            transportComplete ? request.StartUtc : events.Length == 0 ? null : events[0].EventTimeUtc,
            transportComplete ? request.EndUtc : events.Length == 0 ? null : events[^1].EventTimeUtc,
            DeclaresCompleteCoverage: transportComplete,
            HasKnownGaps: false,
            Request: request,
            RetrievalStatus: status,
            Provenance: new HistoricalDataProvenance(
                "Alpaca",
                Feed,
                SourceKind,
                retrievedAtUtc,
                pages,
                duplicates),
            PaginationExhausted: paginationExhausted,
            StatusMessage: statusMessage);
    }

    private Uri CreateTradesUri(HistoricalDataRequest request, string? pageToken)
    {
        var query = new StringBuilder($"v2/stocks/{Uri.EscapeDataString(request.Symbol)}/trades?");
        query.Append("feed=sip&sort=asc");
        query.Append("&start=").Append(Uri.EscapeDataString(FormatTimestamp(request.StartUtc)));
        query.Append("&end=").Append(Uri.EscapeDataString(FormatTimestamp(request.EndUtc)));
        query.Append("&limit=").Append(options.PageSize.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(pageToken))
        {
            query.Append("&page_token=").Append(Uri.EscapeDataString(pageToken));
        }

        return new Uri(query.ToString(), UriKind.Relative);
    }

    private HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("APCA-API-KEY-ID", options.ApiKey);
        request.Headers.TryAddWithoutValidation("APCA-API-SECRET-KEY", options.SecretKey);
        return request;
    }

    private static HistoricalRetrievalStatus? HistoricalFailure(HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.OK => null,
            HttpStatusCode.Unauthorized => HistoricalRetrievalStatus.AuthenticationFailure,
            HttpStatusCode.Forbidden => HistoricalRetrievalStatus.EntitlementFailure,
            _ => HistoricalRetrievalStatus.ProviderFailure
        };

    private static AlpacaHistoricalTradesPage ParsePage(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("trades", out var trades)
            || trades.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Alpaca historical trade page is missing its trades array.");
        }

        var normalized = new List<AlpacaHistoricalTradeDto>();
        foreach (var item in trades.EnumerateArray())
        {
            normalized.Add(JsonSerializer.Deserialize<AlpacaHistoricalTradeDto>(item.GetRawText())
                ?? throw new InvalidDataException("Alpaca returned an empty historical trade."));
        }

        string? nextPageToken = null;
        if (root.TryGetProperty("next_page_token", out var token)
            && token.ValueKind != JsonValueKind.Null)
        {
            if (token.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(token.GetString()))
            {
                throw new InvalidDataException("Alpaca historical trade page has an invalid pagination token.");
            }

            nextPageToken = token.GetString();
        }

        return new AlpacaHistoricalTradesPage(normalized, nextPageToken);
    }

    private static bool SameHistoricalRecord(TradeEvent left, TradeEvent right) =>
        left.Symbol == right.Symbol
        && left.SessionId == right.SessionId
        && left.EventTimeUtc == right.EventTimeUtc
        && left.Provider == right.Provider
        && left.Price == right.Price
        && left.Size == right.Size
        && left.SequenceId == right.SequenceId;

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
}

public sealed record AlpacaHistoricalTradeDto
{
    [JsonPropertyName("c")]
    public string[]? Conditions { get; init; }

    [JsonPropertyName("i")]
    [JsonRequired]
    public long TradeId { get; init; }

    [JsonPropertyName("p")]
    [JsonRequired]
    public decimal Price { get; init; }

    [JsonPropertyName("s")]
    [JsonRequired]
    public long Size { get; init; }

    [JsonPropertyName("t")]
    [JsonRequired]
    public string Timestamp { get; init; } = "";

    [JsonPropertyName("x")]
    [JsonRequired]
    public string Exchange { get; init; } = "";

    [JsonPropertyName("z")]
    public string? Tape { get; init; }
}

public static class AlpacaHistoricalTradeMapper
{
    public static TradeEvent ToTrade(
        AlpacaHistoricalTradeDto dto,
        string symbol,
        string sessionId,
        DateTimeOffset retrievedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (dto.TradeId < 0)
        {
            throw new InvalidDataException("Alpaca historical trade has an invalid trade ID.");
        }

        if (string.IsNullOrWhiteSpace(dto.Timestamp)
            || string.IsNullOrWhiteSpace(dto.Exchange))
        {
            throw new InvalidDataException("Alpaca historical trade is missing timestamp or exchange.");
        }

        var eventTime = ParseTimestamp(dto.Timestamp);
        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        var tradeId = dto.TradeId.ToString(CultureInfo.InvariantCulture);
        var sequenceId = HistoricalTradeIdentity.Create(
            "Alpaca",
            AlpacaHistoricalMarketDataSource.Feed,
            normalizedSymbol,
            dto.Exchange,
            tradeId,
            eventTime);
        return new TradeEvent(
            normalizedSymbol,
            sessionId,
            eventTime,
            retrievedAtUtc.ToUniversalTime(),
            "Alpaca",
            sequenceId,
            dto.Price,
            dto.Size);
    }

    private static DateTimeOffset ParseTimestamp(string timestamp)
    {
        var decimalPoint = timestamp.IndexOf('.', StringComparison.Ordinal);
        if (decimalPoint >= 0)
        {
            var timezoneStart = timestamp.IndexOfAny(['Z', '+', '-'], decimalPoint + 1);
            var fractionEnd = timezoneStart < 0 ? timestamp.Length : timezoneStart;
            if (fractionEnd - decimalPoint - 1 > 7)
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
            throw new InvalidDataException("Alpaca historical trade has an invalid RFC-3339 timestamp.");
        }

        return parsed;
    }
}

internal sealed record AlpacaHistoricalTradesPage(
    IReadOnlyList<AlpacaHistoricalTradeDto> Trades,
    string? NextPageToken);

internal static class AlpacaHistoricalHttp
{
    public static async Task<byte[]> ReadLimitedAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        do
        {
            read = await source.ReadAsync(chunk, cancellationToken);
            if (buffer.Length + read > maximumBytes)
            {
                throw new InvalidDataException(
                    $"Alpaca response exceeded the {maximumBytes}-byte safety limit.");
            }

            buffer.Write(chunk, 0, read);
        }
        while (read > 0);

        return buffer.ToArray();
    }
}
