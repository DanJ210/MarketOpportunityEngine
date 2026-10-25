using System.Text.Json;
using MarketOpportunity.Domain;
using MarketOpportunity.Infrastructure;

namespace MarketOpportunity.UnitTests;

public sealed class MassiveMarketDataParserTests
{
    private static readonly DateTimeOffset ReceivedTime =
        new(2026, 10, 23, 14, 30, 1, TimeSpan.Zero);

    [Theory]
    [InlineData("connected")]
    [InlineData("auth_success")]
    [InlineData("success")]
    public void Accepts_non_error_status(string status)
    {
        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(new { ev = "status", status }));

        Assert.Equal(status, MassiveMarketDataParser.ReadStatus(document.RootElement));
    }

    [Theory]
    [InlineData("auth_failed", "Massive authentication failed.")]
    [InlineData("error", "connection or subscription error")]
    public void Reports_status_failures_without_echoing_provider_message(string status, string expected)
    {
        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(new { ev = "status", status, message = "sensitive-provider-message" }));

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = MassiveMarketDataParser.ReadStatus(document.RootElement);
        });

        Assert.Contains(expected, exception.Message);
        Assert.DoesNotContain("sensitive-provider-message", exception.Message);
    }

    [Theory]
    [InlineData("Your plan doesn't include websocket access. Visit https://massive.com/pricing to upgrade.")]
    [InlineData("YOUR PLAN DOESN'T INCLUDE WEBSOCKET ACCESS.")]
    public void Distinguishes_websocket_plan_rejection_from_an_invalid_key(string message)
    {
        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(new
            {
                ev = "status",
                status = "auth_failed",
                message = $"{message} sensitive-provider-message"
            }));

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = MassiveMarketDataParser.ReadStatus(document.RootElement);
        });

        Assert.Contains("account plan does not include it", exception.Message);
        Assert.Contains("valid for REST without WebSocket access", exception.Message);
        Assert.DoesNotContain("Massive authentication failed", exception.Message);
        Assert.DoesNotContain("sensitive-provider-message", exception.Message);
    }

    [Theory]
    [InlineData("""{"ev":"status","status":"auth_failed"}""")]
    [InlineData("""{"ev":"status","status":"auth_failed","message":null}""")]
    [InlineData("""{"ev":"status","status":"auth_failed","message":123}""")]
    public void Reports_generic_authentication_failure_without_a_string_message(string json)
    {
        using var document = JsonDocument.Parse(json);

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = MassiveMarketDataParser.ReadStatus(document.RootElement);
        });

        Assert.Contains("Massive authentication failed", exception.Message);
        Assert.Contains("valid REST key alone", exception.Message);
    }

    [Fact]
    public void Rejects_status_without_code()
    {
        using var document = JsonDocument.Parse("""{"ev":"status"}""");

        Assert.Throws<InvalidDataException>(() =>
        {
            _ = MassiveMarketDataParser.ReadStatus(document.RootElement);
        });
    }

    [Fact]
    public void Parses_trade_using_participant_time_and_explicit_session()
    {
        using var document = JsonDocument.Parse(
            """{"ev":"T","sym":"AAPL","i":"trade-123","p":251.25,"s":100,"t":1792765800000,"pt":1792765799500}""");

        var marketEvent = Assert.IsType<TradeEvent>(
            Assert.Single(MassiveMarketDataParser.ParseEvents(
                document.RootElement,
                "manual-session-2026-10-23",
                ReceivedTime)));

        Assert.Equal("AAPL", marketEvent.Symbol);
        Assert.Equal("manual-session-2026-10-23", marketEvent.SessionId);
        Assert.Equal("Massive", marketEvent.Provider);
        Assert.Equal("trade-123", marketEvent.SequenceId);
        Assert.Equal(251.25m, marketEvent.Price);
        Assert.Equal(100L, marketEvent.Size);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1792765799500), marketEvent.EventTimeUtc);
        Assert.Equal(ReceivedTime, marketEvent.ReceivedTimeUtc);
    }

    [Fact]
    public void Parses_quote_and_preserves_missing_side_as_invalid_zero_price()
    {
        using var document = JsonDocument.Parse(
            """{"ev":"Q","sym":"MSFT","q":42,"ap":514.10,"as":12,"t":1792765800123}""");

        var marketEvent = Assert.IsType<QuoteEvent>(
            Assert.Single(MassiveMarketDataParser.ParseEvents(
                document.RootElement,
                "manual-session",
                ReceivedTime)));

        Assert.Equal(0m, marketEvent.BidPrice);
        Assert.Equal(514.10m, marketEvent.AskPrice);
        Assert.Null(marketEvent.BidSize);
        Assert.Equal(12L, marketEvent.AskSize);
        Assert.Equal("42", marketEvent.SequenceId);
    }

    [Fact]
    public void Rejects_malformed_trade_instead_of_silently_discarding_it()
    {
        using var document = JsonDocument.Parse(
            """{"ev":"T","sym":"AAPL","i":"trade-123","s":100,"t":1792765800000}""");

        Assert.Throws<InvalidDataException>(() =>
            MassiveMarketDataParser.ParseEvents(document.RootElement, "manual-session", ReceivedTime));
    }
}
