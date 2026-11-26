using System.Net;
using System.Text;
using MarketOpportunity.Application;
using MarketOpportunity.Domain;
using MarketOpportunity.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace MarketOpportunity.UnitTests;

public sealed class AlpacaHistoricalMarketDataTests
{
    private static readonly DateTimeOffset RetrievedAtUtc =
        new(2026, 10, 27, 1, 2, 3, TimeSpan.Zero);
    private static readonly MarketSession Session =
        new(
            "alpaca-us-regular-2026-10-23",
            new DateTimeOffset(2026, 10, 23, 13, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 23, 20, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Calendar_resolves_normal_previous_session_and_utc_bounds()
    {
        using var handler = new RecordingHandler(_ => JsonResponse(
            """
            [
              {"date":"2026-10-22","open":"09:30","close":"16:00"},
              {"date":"2026-10-23","open":"09:30","close":"16:00"}
            ]
            """));
        var calendar = CreateCalendar(handler);

        var result = await calendar.ResolvePreviousSessionAsync(
            "AAPL",
            "operator-current-session",
            new DateTimeOffset(2026, 10, 23, 14, 30, 0, TimeSpan.Zero),
            CancellationToken.None);

        Assert.Equal(MarketSessionCalendarStatus.Success, result.Status);
        Assert.Equal("operator-current-session", result.CurrentSession?.SessionId);
        Assert.Equal("alpaca-us-regular-2026-10-22", result.PreviousSession?.SessionId);
        Assert.Equal(
            new DateTimeOffset(2026, 10, 22, 13, 30, 0, TimeSpan.Zero),
            result.PreviousSession?.OpenAtUtc);
        Assert.Equal(
            new DateTimeOffset(2026, 10, 22, 20, 0, 0, TimeSpan.Zero),
            result.PreviousSession?.CloseAtUtc);
        Assert.Contains(
            handler.Requests,
            request => request.RequestUri?.Query.Contains("start=2026-10-09", StringComparison.Ordinal) == true
                && request.RequestUri.Query.Contains("end=2026-10-23", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Calendar_uses_provider_weekend_and_holiday_semantics()
    {
        using var handler = new RecordingHandler(request =>
        {
            var query = request.RequestUri!.Query;
            return JsonResponse(query.Contains("end=2026-10-26", StringComparison.Ordinal)
                ? """
                  [
                    {"date":"2026-10-23","open":"09:30","close":"16:00"},
                    {"date":"2026-10-26","open":"09:30","close":"16:00"}
                  ]
                  """
                : """
                  [
                    {"date":"2026-10-09","open":"09:30","close":"16:00"},
                    {"date":"2026-10-13","open":"09:30","close":"16:00"}
                  ]
                  """);
        });
        var calendar = CreateCalendar(handler);

        var afterWeekend = await calendar.ResolvePreviousSessionAsync(
            "AAPL", "monday", new DateTimeOffset(2026, 10, 26, 14, 0, 0, TimeSpan.Zero), CancellationToken.None);
        var afterHoliday = await calendar.ResolvePreviousSessionAsync(
            "AAPL", "tuesday", new DateTimeOffset(2026, 10, 13, 14, 0, 0, TimeSpan.Zero), CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 10, 23), afterWeekend.PreviousSession?.TradingDate);
        Assert.Equal(new DateOnly(2026, 10, 9), afterHoliday.PreviousSession?.TradingDate);
    }

    [Fact]
    public async Task Calendar_preserves_provider_early_close()
    {
        using var handler = new RecordingHandler(_ => JsonResponse(
            """
            [
              {"date":"2026-11-25","open":"09:30","close":"13:00"},
              {"date":"2026-11-27","open":"09:30","close":"13:00"}
            ]
            """));
        var calendar = CreateCalendar(handler);

        var result = await calendar.ResolvePreviousSessionAsync(
            "AAPL", "post-holiday", new DateTimeOffset(2026, 11, 27, 15, 0, 0, TimeSpan.Zero), CancellationToken.None);

        Assert.Equal(
            new DateTimeOffset(2026, 11, 25, 18, 0, 0, TimeSpan.Zero),
            result.PreviousSession?.CloseAtUtc);
    }

    [Fact]
    public async Task Calendar_reports_malformed_and_missing_previous_session()
    {
        using var malformedHandler = new RecordingHandler(_ => JsonResponse(
            """[{"date":"2026-10-23","open":"not-a-time","close":"16:00"}]"""));
        using var emptyHandler = new RecordingHandler(_ => JsonResponse(
            """[{"date":"2026-10-23","open":"09:30","close":"16:00"}]"""));
        var malformed = await CreateCalendar(malformedHandler).ResolvePreviousSessionAsync(
            "AAPL", "session", new DateTimeOffset(2026, 10, 23, 14, 0, 0, TimeSpan.Zero), CancellationToken.None);
        var missing = await CreateCalendar(emptyHandler).ResolvePreviousSessionAsync(
            "AAPL", "session", new DateTimeOffset(2026, 10, 23, 14, 0, 0, TimeSpan.Zero), CancellationToken.None);

        Assert.Equal(MarketSessionCalendarStatus.MalformedResponse, malformed.Status);
        Assert.Equal(MarketSessionCalendarStatus.NoPreviousSession, missing.Status);
        Assert.NotNull(missing.CurrentSession);
        Assert.Null(missing.PreviousSession);
    }

    [Fact]
    public async Task Calendar_reports_cancellation_and_provider_failures()
    {
        using var cancellationHandler = new RecordingHandler((_, cancellationToken) =>
            Task.FromCanceled<HttpResponseMessage>(cancellationToken));
        using var failureHandler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var cancelled = await CreateCalendar(cancellationHandler).ResolvePreviousSessionAsync(
            "AAPL", "session", new DateTimeOffset(2026, 10, 23, 14, 0, 0, TimeSpan.Zero), cancellation.Token);
        var failed = await CreateCalendar(failureHandler).ResolvePreviousSessionAsync(
            "AAPL", "session", new DateTimeOffset(2026, 10, 23, 14, 0, 0, TimeSpan.Zero), CancellationToken.None);

        Assert.Equal(MarketSessionCalendarStatus.Cancelled, cancelled.Status);
        Assert.Equal(MarketSessionCalendarStatus.ProviderFailure, failed.Status);
    }

    [Fact]
    public void Historical_identity_is_provider_feed_symbol_exchange_id_and_event_time_safe()
    {
        var first = new AlpacaHistoricalTradeDto
        {
            TradeId = 42,
            Price = 101.25m,
            Size = 100,
            Timestamp = "2026-10-23T14:00:00.123456789Z",
            Exchange = "V",
            Tape = "C",
            Conditions = ["@", "F"]
        };
        var same = AlpacaHistoricalTradeMapper.ToTrade(
            first, "AAPL", Session.SessionId, RetrievedAtUtc);
        var replay = AlpacaHistoricalTradeMapper.ToTrade(
            first, "AAPL", Session.SessionId, RetrievedAtUtc.AddHours(1));

        var otherExchange = AlpacaHistoricalTradeMapper.ToTrade(
            first with { Exchange = "P" }, "AAPL", Session.SessionId, RetrievedAtUtc);
        var otherTime = AlpacaHistoricalTradeMapper.ToTrade(
            first with { Timestamp = "2026-10-23T14:00:00.223456789Z" }, "AAPL", Session.SessionId, RetrievedAtUtc);
        var otherSymbol = AlpacaHistoricalTradeMapper.ToTrade(
            first, "MSFT", Session.SessionId, RetrievedAtUtc);

        Assert.Equal(same.SequenceId, replay.SequenceId);
        Assert.Equal(MarketEventIdentity.Create(same), MarketEventIdentity.Create(replay));
        Assert.NotEqual(same.SequenceId, otherExchange.SequenceId);
        Assert.NotEqual(same.SequenceId, otherTime.SequenceId);
        Assert.NotEqual(same.SequenceId, otherSymbol.SequenceId);
        var iexIdentity = HistoricalTradeIdentity.Create(
            "Alpaca", "iex", "AAPL", "V", "42", same.EventTimeUtc);
        var otherProvider = HistoricalTradeIdentity.Create(
            "Other", "sip", "AAPL", "V", "42", same.EventTimeUtc);
        Assert.NotEqual(same.SequenceId, iexIdentity);
        Assert.NotEqual(same.SequenceId, otherProvider);
    }

    [Fact]
    public async Task Historical_single_page_normalizes_and_declares_complete_only_after_pagination_exhaustion()
    {
        using var handler = new RecordingHandler(request =>
        {
            AssertSipOnly(request);
            return JsonResponse(
                """
                {
                  "trades":[
                    {"c":["@"],"i":20,"p":102.5,"s":2,"t":"2026-10-23T14:00:02Z","x":"V","z":"C"},
                    {"c":["F"],"i":10,"p":101.5,"s":1,"t":"2026-10-23T14:00:01Z","x":"V","z":"C"}
                  ],
                  "next_page_token":null
                }
                """);
        });
        var source = CreateSource(handler);

        var result = Assert.IsType<HistoricalMarketSessionData>(
            await source.ReadSessionAsync("aapl", Session, CancellationToken.None));
        var trades = result.Events.Cast<TradeEvent>().ToArray();

        Assert.Equal(HistoricalRetrievalStatus.SuccessPaginationExhausted, result.RetrievalStatus);
        Assert.True(result.PaginationExhausted);
        Assert.True(result.DeclaresCompleteCoverage);
        Assert.Equal(Session.StartUtc, result.CoverageStartUtc);
        Assert.Equal(Session.EndUtc, result.CoverageEndUtc);
        Assert.Equal("Alpaca", result.Provenance?.Provider);
        Assert.Equal("sip", result.Provenance?.Feed);
        Assert.Equal("historical-rest", result.Provenance?.SourceKind);
        Assert.Equal(1, result.Provenance?.PagesRetrieved);
        Assert.Equal(["AAPL", "AAPL"], trades.Select(item => item.Symbol));
        Assert.Equal([101.5m, 102.5m], trades.Select(item => item.Price));
        Assert.Equal([RetrievedAtUtc, RetrievedAtUtc], trades.Select(item => item.ReceivedTimeUtc));
        Assert.Equal(Session.StartUtc, result.Request?.StartUtc);
        Assert.Equal(Session.EndUtc, result.Request?.EndUtc);
    }

    [Fact]
    public async Task Historical_follows_eighty_one_like_pages_deterministically()
    {
        using var handler = new RecordingHandler(request =>
        {
            AssertSipOnly(request);
            var page = ParsePageNumber(request.RequestUri!.Query);
            var token = page < 81 ? $@"""page-{page + 1}""" : "null";
            return JsonResponse(
                $$"""
                {"trades":[{"i":{{page}},"p":{{page}},"s":1,"t":"2026-10-23T14:00:{{(page % 60):D2}}Z","x":"V"}],"next_page_token":{{token}}}
                """);
        }, maximumRequests: 81);
        var source = CreateSource(handler);

        var result = Assert.IsType<HistoricalMarketSessionData>(
            await source.ReadSessionAsync("AAPL", Session, CancellationToken.None));

        Assert.Equal(HistoricalRetrievalStatus.SuccessPaginationExhausted, result.RetrievalStatus);
        Assert.True(result.PaginationExhausted);
        Assert.Equal(81, result.Provenance?.PagesRetrieved);
        Assert.Equal(81, result.Events.Count);
        Assert.All(
            handler.Requests.Select(request => request.RequestUri!.Query),
            query => Assert.Contains("feed=sip", query, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Historical_rejects_repeated_token_and_page_or_record_limits()
    {
        using var repeatedHandler = new RecordingHandler(_ => JsonResponse(
            """{"trades":[{"i":1,"p":1,"s":1,"t":"2026-10-23T14:00:00Z","x":"V"}],"next_page_token":"same"}"""));
        using var pageLimitHandler = new RecordingHandler(request =>
        {
            var token = QueryValue(request.RequestUri!, "page_token") is null ? "first" : "second";
            return JsonResponse(
                $$"""{"trades":[],"next_page_token":"{{token}}"}""");
        });
        using var recordLimitHandler = new RecordingHandler(_ => JsonResponse(
            """
            {"trades":[
              {"i":1,"p":1,"s":1,"t":"2026-10-23T14:00:00Z","x":"V"},
              {"i":2,"p":2,"s":1,"t":"2026-10-23T14:00:01Z","x":"V"},
              {"i":3,"p":3,"s":1,"t":"2026-10-23T14:00:02Z","x":"V"}
            ],"next_page_token":null}
            """));
        var repeated = await CreateSource(repeatedHandler).ReadSessionAsync(
            "AAPL", Session, CancellationToken.None);
        var pageLimit = await CreateSource(pageLimitHandler, maximumPages: 2).ReadSessionAsync(
            "AAPL", Session, CancellationToken.None);
        var recordLimit = await CreateSource(recordLimitHandler, maximumRecords: 2).ReadSessionAsync(
            "AAPL", Session, CancellationToken.None);

        Assert.Equal(HistoricalRetrievalStatus.MalformedResponse, repeated?.RetrievalStatus);
        Assert.Contains("pagination token", repeated?.StatusMessage);
        Assert.Equal(HistoricalRetrievalStatus.LimitExceeded, pageLimit?.RetrievalStatus);
        Assert.Equal(HistoricalRetrievalStatus.LimitExceeded, recordLimit?.RetrievalStatus);
        Assert.Equal(2, recordLimit?.Events.Count);
        Assert.False(repeated?.PaginationExhausted);
        Assert.False(pageLimit?.PaginationExhausted);
    }

    [Fact]
    public async Task Historical_reports_cancellation_malformed_and_http_failures()
    {
        using var cancelledHandler = new RecordingHandler((_, cancellationToken) =>
            Task.FromCanceled<HttpResponseMessage>(cancellationToken));
        using var malformedHandler = new RecordingHandler(_ => JsonResponse(
            """{"trades":[{"i":1}],"next_page_token":null}"""));
        using var authHandler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var entitlementHandler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        using var providerHandler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var cancelled = await CreateSource(cancelledHandler).ReadSessionAsync("AAPL", Session, cancellation.Token);
        var malformed = await CreateSource(malformedHandler).ReadSessionAsync("AAPL", Session, CancellationToken.None);
        var auth = await CreateSource(authHandler).ReadSessionAsync("AAPL", Session, CancellationToken.None);
        var entitlement = await CreateSource(entitlementHandler).ReadSessionAsync("AAPL", Session, CancellationToken.None);
        var provider = await CreateSource(providerHandler).ReadSessionAsync("AAPL", Session, CancellationToken.None);

        Assert.Equal(HistoricalRetrievalStatus.Cancelled, cancelled?.RetrievalStatus);
        Assert.Equal(HistoricalRetrievalStatus.MalformedResponse, malformed?.RetrievalStatus);
        Assert.Equal(HistoricalRetrievalStatus.AuthenticationFailure, auth?.RetrievalStatus);
        Assert.Equal(HistoricalRetrievalStatus.EntitlementFailure, entitlement?.RetrievalStatus);
        Assert.Equal(HistoricalRetrievalStatus.ProviderFailure, provider?.RetrievalStatus);
    }

    [Fact]
    public async Task Historical_replay_is_idempotent_and_conflicting_identity_is_rejected()
    {
        const string duplicate = """
            {
              "trades":[
                {"i":7,"p":12.5,"s":10,"t":"2026-10-23T14:00:00Z","x":"V"},
                {"i":7,"p":12.5,"s":10,"t":"2026-10-23T14:00:00Z","x":"V"}
              ],
              "next_page_token":null
            }
            """;
        const string conflict = """
            {
              "trades":[
                {"i":7,"p":12.5,"s":10,"t":"2026-10-23T14:00:00Z","x":"V"},
                {"i":7,"p":13.5,"s":10,"t":"2026-10-23T14:00:00Z","x":"V"}
              ],
              "next_page_token":null
            }
            """;
        using var duplicateHandler = new RecordingHandler(_ => JsonResponse(duplicate));
        using var conflictHandler = new RecordingHandler(_ => JsonResponse(conflict));

        var replay = await CreateSource(duplicateHandler).ReadSessionAsync("AAPL", Session, CancellationToken.None);
        var conflicting = await CreateSource(conflictHandler).ReadSessionAsync("AAPL", Session, CancellationToken.None);

        Assert.Single(replay!.Events);
        Assert.Equal(1, replay.Provenance?.DuplicateRecordsIgnored);
        Assert.Equal(HistoricalRetrievalStatus.MalformedResponse, conflicting?.RetrievalStatus);
    }

    [Fact]
    public void Historical_options_reuse_credentials_and_validate_bounds()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MarketData:Alpaca:ApiKey"] = "key",
                ["MarketData:Alpaca:SecretKey"] = "secret",
                ["MarketData:Alpaca:Historical:MaximumPages"] = "81",
                ["MarketData:Alpaca:Historical:MaximumRecords"] = "900000",
                ["MarketData:Alpaca:Historical:PageSize"] = "10000"
            })
            .Build();

        var options = AlpacaHistoricalMarketDataOptions.FromConfiguration(configuration);

        Assert.Equal("key", options.ApiKey);
        Assert.Equal("secret", options.SecretKey);
        Assert.Equal(81, options.MaximumPages);
        Assert.Equal(900000, options.MaximumRecords);
        Assert.Equal(10000, options.PageSize);
    }

    private static AlpacaMarketSessionCalendar CreateCalendar(RecordingHandler handler)
    {
        var client = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://api.alpaca.markets/")
        };
        return new AlpacaMarketSessionCalendar(client, Options());
    }

    private static AlpacaHistoricalMarketDataSource CreateSource(
        RecordingHandler handler,
        int maximumPages = 100,
        int maximumRecords = 1_000_000)
    {
        var client = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://data.alpaca.markets/")
        };
        return new AlpacaHistoricalMarketDataSource(
            client,
            Options() with { MaximumPages = maximumPages, MaximumRecords = maximumRecords },
            new FixedTimeProvider(RetrievedAtUtc));
    }

    private static AlpacaHistoricalMarketDataOptions Options() =>
        new("test-key", "test-secret");

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static void AssertSipOnly(HttpRequestMessage request)
    {
        Assert.Equal("sip", QueryValue(request.RequestUri!, "feed"));
        Assert.Equal("asc", QueryValue(request.RequestUri!, "sort"));
        Assert.Equal(["test-key"], request.Headers.GetValues("APCA-API-KEY-ID"));
        Assert.Equal(["test-secret"], request.Headers.GetValues("APCA-API-SECRET-KEY"));
        Assert.DoesNotContain("iex", request.RequestUri!.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
    }

    private static string? QueryValue(Uri uri, string key) =>
        uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2
                && pair[0].Equals(key, StringComparison.OrdinalIgnoreCase))
            .Select(pair => Uri.UnescapeDataString(pair[1]))
            .SingleOrDefault();

    private static int ParsePageNumber(string query)
    {
        var token = QueryValue(new Uri($"https://example.test/{query}"), "page_token");
        return token is null ? 1 : int.Parse(token["page-".Length..]);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _response;
        private readonly int _maximumRequests;

        public RecordingHandler(
            Func<HttpRequestMessage, HttpResponseMessage> response,
            int maximumRequests = 1_000)
            : this((request, _) => Task.FromResult(response(request)), maximumRequests)
        {
        }

        public RecordingHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response,
            int maximumRequests = 1_000)
        {
            _response = response;
            _maximumRequests = maximumRequests;
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (Requests.Count >= _maximumRequests)
            {
                throw new InvalidOperationException("The test HTTP handler exceeded its request budget.");
            }

            Requests.Add(request);
            return await _response(request, cancellationToken);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
