using MarketOpportunity.Application;
using Microsoft.Extensions.Configuration;

namespace MarketOpportunity.Infrastructure;

public static class MarketDataSourceFactory
{
    public static IMarketDataSource Create(
        string provider,
        IConfiguration configuration,
        string fixturePath,
        TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(fixturePath);
        ArgumentNullException.ThrowIfNull(timeProvider);

        return provider.ToUpperInvariant() switch
        {
            "FIXTURE" => new FixtureMarketDataSource(fixturePath),
            "MASSIVE" => new MassiveMarketDataSource(
                MassiveMarketDataOptions.FromConfiguration(configuration),
                timeProvider),
            "ALPACA" => new AlpacaMarketDataSource(
                AlpacaMarketDataOptions.FromConfiguration(configuration),
                timeProvider),
            _ => throw new InvalidOperationException(
                $"Unsupported MarketData:Provider '{provider}'. Use Fixture, Alpaca, or Massive.")
        };
    }
}
