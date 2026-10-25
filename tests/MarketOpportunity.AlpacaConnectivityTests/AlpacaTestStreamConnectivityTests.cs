using MarketOpportunity.Domain;
using MarketOpportunity.Infrastructure;
using Microsoft.Extensions.Configuration;
using Xunit.Sdk;

namespace MarketOpportunity.AlpacaConnectivityTests;

public sealed class AlpacaConnectivityFactAttribute : FactAttribute
{
    public AlpacaConnectivityFactAttribute()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets("432c7414-ecd9-4c77-aff5-c0c7cf12d5e7")
            .AddEnvironmentVariables()
            .Build();
        if (string.IsNullOrWhiteSpace(configuration["MarketData:Alpaca:ApiKey"])
            || string.IsNullOrWhiteSpace(configuration["MarketData:Alpaca:SecretKey"]))
        {
            Skip = "Opt-in connectivity test skipped: configure Alpaca API key and secret in User Secrets.";
        }
    }
}

public sealed class AlpacaTestStreamConnectivityTests
{
    [AlpacaConnectivityFact]
    public async Task Test_feed_emits_canonical_market_events()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets("432c7414-ecd9-4c77-aff5-c0c7cf12d5e7")
            .AddEnvironmentVariables()
            .Build();
        var options = AlpacaMarketDataOptions.FromConfiguration(configuration);
        if (options.Feed != "test" || !options.Symbols.Contains("FAKEPACA", StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The opt-in connectivity test is restricted to Alpaca's test feed and FAKEPACA symbol.");
        }

        var source = new AlpacaMarketDataSource(options, TimeProvider.System);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await foreach (var marketEvent in source.ReadEventsAsync(timeout.Token)
                           .WithCancellation(timeout.Token))
        {
            Assert.Equal("Alpaca", marketEvent.Provider);
            Assert.Equal("FAKEPACA", marketEvent.Symbol);
            Assert.NotEqual(default, marketEvent.EventTimeUtc);
            Assert.NotEqual(default, marketEvent.ReceivedTimeUtc);
            Assert.IsAssignableFrom<MarketEvent>(marketEvent);
            return;
        }

        throw new InvalidOperationException("Alpaca test stream completed without emitting a market event.");
    }
}
