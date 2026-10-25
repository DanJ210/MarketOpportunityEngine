using System.Text.Json;
using MarketOpportunity.Analytics;
using MarketOpportunity.Domain;
using MarketOpportunity.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace MarketOpportunity.UnitTests;

public sealed class AlpacaMarketDataTests
{
    private static readonly DateTimeOffset ReceivedAtUtc =
        new(2026, 10, 24, 14, 31, 0, TimeSpan.Zero);

    [Fact]
    public void Parses_trade_with_provider_identity_and_nanosecond_event_time()
    {
        using var document = JsonDocument.Parse(
            """{"T":"t","i":96921,"S":"AAPL","p":126.55,"s":25,"t":"2026-10-24T14:30:59.123456789Z"}""");

        var trade = Assert.IsType<TradeEvent>(AlpacaMarketDataParser.ParseEvent(
            document.RootElement,
            "alpaca-session",
            ReceivedAtUtc));

        Assert.Equal("AAPL", trade.Symbol);
        Assert.Equal("alpaca-session", trade.SessionId);
        Assert.Equal("Alpaca", trade.Provider);
        Assert.Equal("96921", trade.SequenceId);
        Assert.Equal(126.55m, trade.Price);
        Assert.Equal(25L, trade.Size);
        Assert.Equal(
            new DateTimeOffset(2026, 10, 24, 14, 30, 59, TimeSpan.Zero).AddTicks(1_234_567),
            trade.EventTimeUtc);
        Assert.Equal(ReceivedAtUtc, trade.ReceivedTimeUtc);
    }

    [Fact]
    public void Parses_quote_prices_and_round_lot_sizes_without_inventing_sequence_identity()
    {
        using var document = JsonDocument.Parse(
            """{"T":"q","S":"MSFT","bp":87.66,"bs":1,"ap":87.68,"as":4,"t":"2026-10-24T14:30:59.335689322Z"}""");

        var quote = Assert.IsType<QuoteEvent>(AlpacaMarketDataParser.ParseEvent(
            document.RootElement,
            "alpaca-session",
            ReceivedAtUtc));

        Assert.Equal("MSFT", quote.Symbol);
        Assert.Equal("Alpaca", quote.Provider);
        Assert.Null(quote.SequenceId);
        Assert.Equal(87.66m, quote.BidPrice);
        Assert.Equal(87.68m, quote.AskPrice);
        Assert.Equal(1L, quote.BidSize);
        Assert.Equal(4L, quote.AskSize);
        Assert.Equal(ReceivedAtUtc, quote.ReceivedTimeUtc);
    }

    [Fact]
    public void Preserves_invalid_market_values_for_downstream_data_quality_vetoes()
    {
        using var tradeDocument = JsonDocument.Parse(
            """{"T":"t","i":1,"S":"AAPL","p":-1,"s":0,"t":"2026-10-24T14:30:59Z"}""");
        using var quoteDocument = JsonDocument.Parse(
            """{"T":"q","S":"AAPL","bp":0,"bs":-1,"ap":-2,"as":0,"t":"2026-10-24T14:30:59Z"}""");

        var trade = Assert.IsType<TradeEvent>(AlpacaMarketDataParser.ParseEvent(
            tradeDocument.RootElement,
            "session",
            ReceivedAtUtc));
        var quote = Assert.IsType<QuoteEvent>(AlpacaMarketDataParser.ParseEvent(
            quoteDocument.RootElement,
            "session",
            ReceivedAtUtc));

        Assert.Equal(-1m, trade.Price);
        Assert.Equal(0L, trade.Size);
        Assert.Equal(0m, quote.BidPrice);
        Assert.Equal(-2m, quote.AskPrice);
        Assert.Equal(-1L, quote.BidSize);
        Assert.Equal(0L, quote.AskSize);
    }

    [Theory]
    [InlineData("""{"T":"t","S":"AAPL","p":1,"s":1,"t":"2026-10-24T14:30:59Z"}""")]
    [InlineData("""{"T":"q","S":"AAPL","bp":1,"t":"2026-10-24T14:30:59Z"}""")]
    public void Rejects_malformed_trade_or_quote(string json)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Throws<InvalidDataException>(() => AlpacaMarketDataParser.ParseEvent(
            document.RootElement,
            "session",
            ReceivedAtUtc));
    }

    [Fact]
    public void Rejects_unsupported_market_data_message_types()
    {
        using var document = JsonDocument.Parse(
            """{"T":"b","S":"AAPL","t":"2026-10-24T14:30:59Z"}""");

        var exception = Assert.Throws<InvalidDataException>(() =>
            AlpacaMarketDataParser.ParseEvent(document.RootElement, "session", ReceivedAtUtc));

        Assert.Contains("unsupported", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reads_namespaced_provider_configuration_and_normalizes_symbols()
    {
        var configuration = AlpacaConfiguration(
            feed: "IEX",
            symbols: " AAPL,MSFT,aapl,,SPY ");

        var options = AlpacaMarketDataOptions.FromConfiguration(configuration);

        Assert.Equal("test-key", options.ApiKey);
        Assert.Equal("test-secret", options.SecretKey);
        Assert.Equal("iex", options.Feed);
        Assert.Equal("operator-session", options.SessionId);
        Assert.Equal(["AAPL", "MSFT", "SPY"], options.Symbols);
        Assert.Equal(MarketDataCoverage.Limited, options.Capabilities.Coverage);
        Assert.True(options.Capabilities.IsRealTime);
        Assert.True(options.Capabilities.SupportsTrades);
        Assert.True(options.Capabilities.SupportsQuotes);
    }

    [Theory]
    [InlineData("test", MarketDataCoverage.Synthetic, true)]
    [InlineData("iex", MarketDataCoverage.Limited, true)]
    [InlineData("sip", MarketDataCoverage.Consolidated, true)]
    [InlineData("delayed_sip", MarketDataCoverage.Consolidated, false)]
    public void Describes_feed_coverage_without_provider_name_heuristics(
        string feed,
        MarketDataCoverage coverage,
        bool isRealTime)
    {
        var options = AlpacaMarketDataOptions.FromConfiguration(AlpacaConfiguration(feed));

        Assert.Equal("Alpaca", options.Capabilities.Provider);
        Assert.Equal(feed, options.Capabilities.Feed);
        Assert.Equal(coverage, options.Capabilities.Coverage);
        Assert.Equal(isRealTime, options.Capabilities.IsRealTime);
    }

    [Fact]
    public void Rejects_unsupported_feed_without_echoing_secret()
    {
        var builder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MarketData:Alpaca:ApiKey"] = "test-key",
                ["MarketData:Alpaca:SecretKey"] = "sensitive-secret",
                ["MarketData:Alpaca:Feed"] = "unknown",
                ["MarketData:Alpaca:SessionId"] = "session",
                ["MarketData:Alpaca:Symbols"] = "FAKEPACA"
            });

        var badFeed = Assert.Throws<InvalidOperationException>(
            () => AlpacaMarketDataOptions.FromConfiguration(builder.Build()));
        Assert.Contains("Feed", badFeed.Message);
        Assert.DoesNotContain("sensitive-secret", badFeed.Message);
    }

    [Theory]
    [InlineData("MarketData:Alpaca:ApiKey", "ApiKey")]
    [InlineData("MarketData:Alpaca:SecretKey", "SecretKey")]
    [InlineData("MarketData:Alpaca:SessionId", "SessionId")]
    [InlineData("MarketData:Alpaca:Symbols", "Symbols")]
    public void Requires_credentials_session_and_symbols(
        string missingSetting,
        string expectedSetting)
    {
        var settings = new Dictionary<string, string?>
        {
            ["MarketData:Alpaca:ApiKey"] = "test-key",
            ["MarketData:Alpaca:SecretKey"] = "test-secret",
            ["MarketData:Alpaca:Feed"] = "test",
            ["MarketData:Alpaca:SessionId"] = "operator-session",
            ["MarketData:Alpaca:Symbols"] = "FAKEPACA"
        };
        settings.Remove(missingSetting);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            AlpacaMarketDataOptions.FromConfiguration(
                new ConfigurationBuilder().AddInMemoryCollection(settings).Build()));

        Assert.Contains(expectedSetting, exception.Message);
        Assert.DoesNotContain("test-secret", exception.Message);
    }

    [Fact]
    public void Provider_factory_selects_fixture_alpaca_and_massive_without_analytics_changes()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MarketData:Alpaca:ApiKey"] = "alpaca-key",
                ["MarketData:Alpaca:SecretKey"] = "alpaca-secret",
                ["MarketData:Alpaca:Feed"] = "test",
                ["MarketData:Alpaca:SessionId"] = "session",
                ["MarketData:Alpaca:Symbols"] = "FAKEPACA",
                ["MarketData:Massive:ApiKey"] = "massive-key",
                ["MarketData:Massive:SessionId"] = "session",
                ["MarketData:Massive:Symbols"] = "AAPL"
            })
            .Build();
        var fixturePath = Path.GetTempFileName();
        try
        {
            var fixture = MarketDataSourceFactory.Create(
                "Fixture", configuration, fixturePath, TimeProvider.System);
            var alpaca = MarketDataSourceFactory.Create(
                "Alpaca", configuration, fixturePath, TimeProvider.System);
            var massive = MarketDataSourceFactory.Create(
                "Massive", configuration, fixturePath, TimeProvider.System);

            Assert.IsType<FixtureMarketDataSource>(fixture);
            Assert.IsType<AlpacaMarketDataSource>(alpaca);
            Assert.IsType<MassiveMarketDataSource>(massive);
            Assert.Equal("Alpaca", alpaca.Capabilities.Provider);
            Assert.Equal("Massive", massive.Capabilities.Provider);
        }
        finally
        {
            File.Delete(fixturePath);
        }
    }

    [Fact]
    public void Feature_and_opportunity_keep_the_selected_feed_provenance()
    {
        var capabilities = AlpacaMarketDataOptions.FromConfiguration(
            AlpacaConfiguration("iex")).Capabilities;
        var configuration = new ScannerConfiguration();
        var state = new SymbolEventState("AAPL", "session", configuration.MaximumStateEvents);
        var asOf = ReceivedAtUtc;
        state.Add(new TradeEvent("AAPL", "session", asOf.AddSeconds(-3), asOf, "Alpaca", "1", 100m, 10));
        state.Add(new TradeEvent("AAPL", "session", asOf.AddSeconds(-2), asOf, "Alpaca", "2", 101m, 10));
        state.Add(new TradeEvent("AAPL", "session", asOf.AddSeconds(-1), asOf, "Alpaca", "3", 102m, 10));
        state.Add(new QuoteEvent("AAPL", "session", asOf, asOf, "Alpaca", null, 101.99m, 102.01m, 1, 1));

        var features = new FeatureCalculator().Calculate(state, asOf, configuration, capabilities);
        var opportunity = new MomentumContinuationDetector().Evaluate(features, configuration);

        Assert.Equal(capabilities, features.MarketData);
        Assert.Equal(capabilities, opportunity.MarketData);
    }

    private static IConfiguration AlpacaConfiguration(
        string feed = "test",
        string symbols = "FAKEPACA") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MarketData:Alpaca:ApiKey"] = "test-key",
                ["MarketData:Alpaca:SecretKey"] = "test-secret",
                ["MarketData:Alpaca:Feed"] = feed,
                ["MarketData:Alpaca:SessionId"] = "operator-session",
                ["MarketData:Alpaca:Symbols"] = symbols
            })
            .Build();
}
