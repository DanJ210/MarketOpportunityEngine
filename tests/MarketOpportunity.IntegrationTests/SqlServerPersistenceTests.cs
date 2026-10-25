using MarketOpportunity.Analytics;
using MarketOpportunity.Domain;
using MarketOpportunity.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace MarketOpportunity.IntegrationTests;

public sealed class SqlServerPersistenceTests
{
    [Fact]
    public async Task Sql_persistence_is_session_scoped_immutable_and_replay_idempotent()
    {
        var connectionString = GetTestConnectionString();
        var token = Guid.NewGuid().ToString("N")[..16];
        var symbol = $"IT{token}";
        var otherSymbol = $"IT{token}X";
        var sessionId = $"it-session-{token}";
        var otherSessionId = $"it-session2-{token}";
        var firstConfiguration = new ScannerConfiguration
        {
            Version = $"it-config-{token}",
            FeatureSetVersion = $"it-features-{token}",
            SetupDefinitionVersion = $"it-setup-{token}"
        };
        var changedConfiguration = firstConfiguration with
        {
            Version = $"it-config2-{token}",
            MomentumScoreWeight = 49,
            VwapScoreWeight = 36
        };
        var initialEvents = CreateEvents(symbol, sessionId);
        var schemaReady = false;
        try
        {
            await using (var setupContext = CreateContext(connectionString))
            {
                if (!await setupContext.Database.EnsureCreatedAsync())
                {
                    await setupContext.Database.MigrateAsync();
                }
            }
            schemaReady = true;

            var first = Analyze(initialEvents, firstConfiguration);
            await Persist(connectionString, firstConfiguration, initialEvents, first);
            await Persist(connectionString, firstConfiguration, initialEvents, first);

            var conflictingConfiguration = firstConfiguration with { SecondaryMomentumWindowSeconds = 31 };
            var conflictingAnalysis = Analyze(initialEvents, conflictingConfiguration);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Persist(connectionString, conflictingConfiguration, initialEvents, conflictingAnalysis));

            var changedConfig = Analyze(initialEvents, changedConfiguration);
            await Persist(connectionString, changedConfiguration, initialEvents, changedConfig);

            var changedEvents = initialEvents
                .Append(new TradeEvent(
                    symbol,
                    sessionId,
                    first.Features.AsOfUtc.AddSeconds(1),
                    first.Features.AsOfUtc.AddSeconds(2),
                    "IntegrationFixture",
                    $"late-input-{token}",
                    103.50m,
                    700))
                .ToArray();
            var changedInput = Analyze(changedEvents, changedConfiguration);
            await Persist(connectionString, changedConfiguration, changedEvents, changedInput);

            var secondSessionEvents = CreateEvents(symbol, otherSessionId, 300m);
            var secondSession = Analyze(secondSessionEvents, firstConfiguration);
            await Persist(connectionString, firstConfiguration, secondSessionEvents, secondSession);

            var otherSymbolEvents = CreateEvents(otherSymbol, sessionId, 200m);
            var otherSymbolAnalysis = Analyze(otherSymbolEvents, firstConfiguration);
            await Persist(connectionString, firstConfiguration, otherSymbolEvents, otherSymbolAnalysis);

            await using var verifyContext = CreateContext(connectionString);
            var storedEvents = await verifyContext.MarketEvents
                .Where(item => item.Symbol == symbol)
                .ToListAsync();
            var storedFeatures = await verifyContext.FeatureSnapshots
                .Where(item => item.Symbol == symbol)
                .ToListAsync();
            var storedOpportunities = await verifyContext.Opportunities
                .Where(item => item.Symbol == symbol)
                .ToListAsync();
            var otherSymbolOpportunityCount = await verifyContext.Opportunities
                .CountAsync(item => item.Symbol == otherSymbol && item.SessionId == sessionId);
            var storedEvidence = await verifyContext.OpportunityEvidence
                .Where(item => storedOpportunities.Select(opportunity => opportunity.Id).Contains(item.OpportunityId))
                .ToListAsync();
            var storedConfigurations = await verifyContext.ConfigurationSnapshots
                .Where(item => item.Version == firstConfiguration.Version
                    || item.Version == changedConfiguration.Version)
                .ToListAsync();

            Assert.Equal(changedEvents.Length + secondSessionEvents.Length, storedEvents.Count);
            Assert.Equal(secondSessionEvents.Length, storedEvents.Count(item => item.SessionId == otherSessionId));
            Assert.All(storedEvents.Where(item => item.SessionId == sessionId), item => Assert.Equal(symbol, item.Symbol));
            Assert.Equal(4, storedFeatures.Count);
            Assert.Equal(4, storedOpportunities.Count);
            Assert.Equal(1, otherSymbolOpportunityCount);
            Assert.Equal(2, storedConfigurations.Count);
            var originalConfiguration = Assert.Single(storedConfigurations,
                item => item.Version == firstConfiguration.Version);
            Assert.Equal(firstConfiguration, JsonSerializer.Deserialize<ScannerConfiguration>(
                originalConfiguration.ConfigurationJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var originalFeatures = Assert.Single(storedFeatures, item => item.Id == first.Features.Id);
            Assert.Equal(first.Features, JsonSerializer.Deserialize<FeatureSnapshot>(
                originalFeatures.SnapshotJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            Assert.DoesNotContain(storedFeatures, item => item.Id == conflictingAnalysis.Features.Id);
            Assert.Contains(storedEvidence, item => item.Kind == "Supporting");
            Assert.Contains(storedEvidence, item => item.Kind == "Invalidation");
            Assert.All(storedOpportunities, item =>
            {
                Assert.Equal("IntegrationFixture", item.DataProvider);
                Assert.Equal("Synthetic", item.DataFeed);
                Assert.Equal("Synthetic", item.DataCoverage);
                Assert.False(item.IsRealTimeData);
            });
            Assert.Equal(4, storedFeatures.Select(item => item.Id).Distinct().Count());
            Assert.Equal(4, storedOpportunities.Select(item => item.Id).Distinct().Count());
            Assert.Equal(2, storedOpportunities.Count(item => item.ConfigurationVersion == changedConfiguration.Version));
        }
        finally
        {
            if (schemaReady)
            {
                await Cleanup(
                    connectionString,
                    [symbol, otherSymbol],
                    firstConfiguration.Version,
                    changedConfiguration.Version);
            }
        }
    }

    private static string GetTestConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("MARKETOPPORTUNITY_SQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "SQL Server integration test was not run: set MARKETOPPORTUNITY_SQL_TEST_CONNECTION to a dedicated SQL Server test database.");
        }

        var builder = new SqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.InitialCatalog)
            || !builder.InitialCatalog.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "SQL Server integration tests require a dedicated database whose name contains 'test'.");
        }

        return builder.ConnectionString;
    }

    private static MarketOpportunityDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<MarketOpportunityDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new MarketOpportunityDbContext(options);
    }

    private static async Task Persist(
        string connectionString,
        ScannerConfiguration configuration,
        IReadOnlyCollection<MarketEvent> events,
        (FeatureSnapshot Features, Opportunity Opportunity) result)
    {
        await using var context = CreateContext(connectionString);
        var store = new EfScannerStore(context);
        await store.PersistRunAsync(
            configuration,
            events,
            result.Features,
            result.Opportunity,
            CancellationToken.None);
    }

    private static (FeatureSnapshot Features, Opportunity Opportunity) Analyze(
        IReadOnlyCollection<MarketEvent> events,
        ScannerConfiguration configuration)
    {
        var first = events.First();
        var state = new SymbolEventState(first.Symbol, first.SessionId, configuration.MaximumStateEvents);
        foreach (var marketEvent in events)
        {
            state.Add(marketEvent);
        }

        var asOfUtc = events.Max(item => item.EventTimeUtc);
        var features = new FeatureCalculator().Calculate(
            state,
            asOfUtc,
            configuration,
            new MarketDataCapabilities(
                "IntegrationFixture",
                "Synthetic",
                MarketDataCoverage.Synthetic,
                false,
                true,
                true));
        var opportunity = new MomentumContinuationDetector().Evaluate(features, configuration);
        return (features, opportunity);
    }

    private static MarketEvent[] CreateEvents(string symbol, string sessionId, decimal startPrice = 100m)
    {
        var asOfUtc = new DateTimeOffset(2026, 10, 23, 14, 30, 42, TimeSpan.Zero);
        return
        [
            new TradeEvent(symbol, sessionId, asOfUtc.AddSeconds(-30), asOfUtc, "IntegrationFixture", $"trade-1-{sessionId}", startPrice, 10),
            new TradeEvent(symbol, sessionId, asOfUtc.AddSeconds(-20), asOfUtc, "IntegrationFixture", $"trade-2-{sessionId}", startPrice + 1m, 20),
            new TradeEvent(symbol, sessionId, asOfUtc.AddSeconds(-10), asOfUtc, "IntegrationFixture", $"trade-3-{sessionId}", startPrice + 2m, 30),
            new TradeEvent(symbol, sessionId, asOfUtc.AddSeconds(-1), asOfUtc, "IntegrationFixture", $"trade-4-{sessionId}", startPrice + 3m, 40),
            new QuoteEvent(symbol, sessionId, asOfUtc, asOfUtc, "IntegrationFixture", $"quote-1-{sessionId}", startPrice + 2.99m, startPrice + 3.01m, 500, 500)
        ];
    }

    private static async Task Cleanup(
        string connectionString,
        IReadOnlyCollection<string> symbols,
        string configurationVersion,
        string changedConfigurationVersion)
    {
        await using var context = CreateContext(connectionString);
        await context.Opportunities.Where(item => symbols.Contains(item.Symbol)).ExecuteDeleteAsync();
        await context.FeatureSnapshots.Where(item => symbols.Contains(item.Symbol)).ExecuteDeleteAsync();
        await context.MarketEvents.Where(item => symbols.Contains(item.Symbol)).ExecuteDeleteAsync();
        await context.ConfigurationSnapshots
            .Where(item => item.Version == configurationVersion || item.Version == changedConfigurationVersion)
            .ExecuteDeleteAsync();
    }
}
