using System.Text.Json;
using MarketOpportunity.Analytics;
using MarketOpportunity.Application;
using MarketOpportunity.Domain;
using MarketOpportunity.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace MarketOpportunity.UnitTests;

public sealed class ScannerUniverseTests
{
    [Fact]
    public void Normalizes_case_and_whitespace_and_preserves_first_occurrence_order()
    {
        var symbols = ScannerUniverse.FromConfiguration(Configuration(" tsla, aapl,TSLA, MsFt, aapl "));

        Assert.Equal(["TSLA", "AAPL", "MSFT"], symbols);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)symbols).Add("OTHER"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(",")]
    [InlineData(",AAPL")]
    [InlineData("AAPL,")]
    [InlineData("AAPL,,MSFT")]
    [InlineData("AAPL,  ,MSFT")]
    public void Rejects_missing_or_empty_entries_without_echoing_configuration(string? symbols)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ScannerUniverse.FromConfiguration(Configuration(symbols)));

        Assert.Contains("Scanner:Universe:Symbols", exception.Message);
        Assert.DoesNotContain("private-api-key", exception.Message);
        Assert.DoesNotContain("private-secret", exception.Message);
    }

    [Fact]
    public void Both_live_adapters_use_the_same_universe_and_ignore_legacy_provider_lists()
    {
        var configuration = Configuration(
            " msft,AaPl,MSFT ",
            legacyMassive: "OLDMASSIVE",
            legacyAlpaca: "OLDALPACA");

        var alpaca = AlpacaMarketDataOptions.FromConfiguration(configuration);
        var massive = MassiveMarketDataOptions.FromConfiguration(configuration);

        Assert.Equal(["MSFT", "AAPL"], alpaca.Symbols);
        Assert.Equal(alpaca.Symbols, massive.Symbols);
        Assert.DoesNotContain("OLDMASSIVE", massive.Symbols);
        Assert.DoesNotContain("OLDALPACA", alpaca.Symbols);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Legacy_provider_lists_do_not_supply_or_merge_a_missing_scanner_universe(string? universe)
    {
        var configuration = Configuration(universe, legacyMassive: "AAPL", legacyAlpaca: "FAKEPACA");

        var alpacaError = Assert.Throws<InvalidOperationException>(
            () => AlpacaMarketDataOptions.FromConfiguration(configuration));
        var massiveError = Assert.Throws<InvalidOperationException>(
            () => MassiveMarketDataOptions.FromConfiguration(configuration));

        Assert.Contains("Scanner:Universe:Symbols", alpacaError.Message);
        Assert.Contains("Scanner:Universe:Symbols", massiveError.Message);
        Assert.DoesNotContain("private-secret", alpacaError.Message);
        Assert.DoesNotContain("private-api-key", massiveError.Message);
    }

    [Fact]
    public void Fixture_selection_does_not_validate_or_depend_on_the_live_universe()
    {
        var fixture = MarketDataSourceFactory.Create(
            "Fixture",
            Configuration("invalid,,universe"),
            "fixture.json",
            TimeProvider.System);

        Assert.IsType<FixtureMarketDataSource>(fixture);
        Assert.Equal("RecordedFixture", fixture.Capabilities.Provider);
    }

    [Fact]
    public async Task Recorded_fixture_replay_is_identical_for_different_and_invalid_live_universes()
    {
        var fixturePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "MarketOpportunity.Web", "Fixtures", "momentum-continuation.json"));
        var firstState = new ScannerState();
        var secondState = new ScannerState();
        var first = Pipeline(fixturePath, Configuration("AAPL,MSFT"), firstState);
        var second = Pipeline(fixturePath, Configuration("invalid,,universe"), secondState);

        await first.RunFixtureAsync(CancellationToken.None);
        await second.RunFixtureAsync(CancellationToken.None);

        Assert.Equal(4, firstState.Read().Results.Count);
        Assert.Equal(JsonSerializer.Serialize(firstState.Read()), JsonSerializer.Serialize(secondState.Read()));
        var expectedStatuses = new Dictionary<(string Symbol, string SessionId), MarketStructureStatus>
        {
            [("MOE", "fixture-2026-10-22")] = MarketStructureStatus.Available,
            [("MOE", "fixture-2026-10-23")] = MarketStructureStatus.Insufficient,
            [("QNT", "fixture-2026-10-22")] = MarketStructureStatus.Insufficient,
            [("QNT", "fixture-2026-10-23")] = MarketStructureStatus.Available
        };
        foreach (var result in firstState.Read().Results)
        {
            var features = result.Features;
            Assert.Equal(expectedStatuses[(features.Symbol, features.SessionId)], features.MarketStructureStatus);
            Assert.Null(features.LatestConfirmedSwingHigh);
            Assert.Null(features.LatestConfirmedSwingLow);
        }
    }

    private static ScannerPipeline Pipeline(string fixturePath, IConfiguration settings, ScannerState state) =>
        new(
            MarketDataSourceFactory.Create("Fixture", settings, fixturePath, TimeProvider.System),
            new RecordingStore(),
            state,
            new ScannerConfiguration(),
            new FeatureCalculator(),
            new MomentumContinuationDetector(),
            TimeProvider.System);

    private sealed class RecordingStore : IScannerStore
    {
        public Task PersistRunAsync(
            ScannerConfiguration configuration,
            IReadOnlyCollection<MarketEvent> marketEvents,
            FeatureSnapshot featureSnapshot,
            Opportunity opportunity,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static IConfiguration Configuration(
        string? universe,
        string? legacyMassive = null,
        string? legacyAlpaca = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Scanner:Universe:Symbols"] = universe,
                ["MarketData:Alpaca:ApiKey"] = "private-api-key",
                ["MarketData:Alpaca:SecretKey"] = "private-secret",
                ["MarketData:Alpaca:Feed"] = "test",
                ["MarketData:Alpaca:SessionId"] = "alpaca-session",
                ["MarketData:Alpaca:Symbols"] = legacyAlpaca,
                ["MarketData:Massive:ApiKey"] = "private-api-key",
                ["MarketData:Massive:SessionId"] = "massive-session",
                ["MarketData:Massive:Symbols"] = legacyMassive
            })
            .Build();
}
