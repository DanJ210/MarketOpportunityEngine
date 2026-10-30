using MarketOpportunity.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace MarketOpportunity.UnitTests;

public sealed class MassiveMarketDataOptionsTests
{
    [Fact]
    public void Reads_provider_settings_from_configuration_and_normalizes_symbols()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MarketData:Massive:ApiKey"] = "test-key",
                ["MarketData:Massive:SessionId"] = "operator-session",
                ["Scanner:Universe:Symbols"] = " aapl,MSFT,AAPL,SPY "
            })
            .Build();

        var options = MassiveMarketDataOptions.FromConfiguration(configuration);

        Assert.Equal("test-key", options.ApiKey);
        Assert.Equal("operator-session", options.SessionId);
        Assert.Equal(["AAPL", "MSFT", "SPY"], options.Symbols);
    }

    [Fact]
    public void Reports_missing_api_key_without_falling_back_to_environment_specific_behavior()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MarketData:Massive:SessionId"] = "operator-session",
                ["Scanner:Universe:Symbols"] = "AAPL"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => { _ = MassiveMarketDataOptions.FromConfiguration(configuration); });

        Assert.Contains("MarketData:Massive:ApiKey", exception.Message);
    }
}
