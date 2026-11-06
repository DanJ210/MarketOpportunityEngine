using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MarketOpportunity.Application;
using MarketOpportunity.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace MarketOpportunity.IntegrationTests;

public sealed class StructureWebApplicationTests
{
    [Fact]
    public async Task Hosted_fixture_api_and_dashboard_keep_structure_descriptive_and_offline()
    {
        await using var factory = new StructureApplicationFactory(useFixtureSource: true);
        using var client = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        HttpResponseMessage? response = null;
        while (!timeout.IsCancellationRequested)
        {
            response?.Dispose();
            response = await client.GetAsync(
                "/api/v1/symbols/MOE/snapshot?sessionId=fixture-2026-10-22", timeout.Token);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                break;
            }

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
        }

        Assert.NotNull(response);
        using (response)
        using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token)))
        {
            var root = json.RootElement;
            Assert.Equal("MOE", root.GetProperty("symbol").GetString());
            Assert.Equal("fixture-2026-10-22", root.GetProperty("sessionId").GetString());
            Assert.Equal("features-v16", root.GetProperty("featureSetVersion").GetString());
            Assert.Equal("Available", root.GetProperty("marketStructureStatus").GetString());
            Assert.Equal("Insufficient", root.GetProperty("structureState").GetString());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("nearestSupportCandidate").ValueKind);
            Assert.Equal(JsonValueKind.Null, root.GetProperty("nearestResistanceCandidate").ValueKind);
        }

        using (var opportunitiesResponse = await client.GetAsync("/api/v1/opportunities", timeout.Token))
        using (var opportunitiesJson = JsonDocument.Parse(
                   await opportunitiesResponse.Content.ReadAsStringAsync(timeout.Token)))
        {
            Assert.Equal(HttpStatusCode.OK, opportunitiesResponse.StatusCode);
            var marketSituation = opportunitiesJson.RootElement.EnumerateArray()
                .Select(item => item.GetProperty("marketSituation"))
                .First(item => item.GetProperty("sessionId").GetString() == "fixture-2026-10-22");
            Assert.Equal("market-situation-v1", marketSituation.GetProperty("definitionVersion").GetString());
            Assert.Equal("CurrentSessionOnly", marketSituation.GetProperty("contextMode").GetString());
            Assert.DoesNotContain(
                marketSituation.GetProperty("eligibleLevels").EnumerateArray(),
                item => item.GetProperty("source").GetString() is "PriorSwingHigh"
                    or "PriorSwingLow" or "PriorSessionHigh" or "PriorSessionLow");
        }

        using (var priorResponse = await client.GetAsync(
                   "/api/v1/symbols/MOE/prior-context?sessionId=fixture-2026-10-23",
                   timeout.Token))
        {
            var priorJson = await priorResponse.Content.ReadAsStringAsync(timeout.Token);
            using var json = JsonDocument.Parse(priorJson);
            Assert.Equal(HttpStatusCode.OK, priorResponse.StatusCode);
            var contextView = json.RootElement;
            var context = contextView.GetProperty("context");
            Assert.Equal("fixture-2026-10-22", context.GetProperty("priorSessionId").GetString());
            Assert.Equal("Partial", context.GetProperty("completeness").GetString());
            Assert.Equal("prior-session-context-v1", context.GetProperty("contextDefinitionVersion").GetString());
            Assert.Equal("RecordedFixture", context.GetProperty("marketData").GetProperty("provider").GetString());
            Assert.Equal("recorded-fixture-v1", context.GetProperty("historicalSource").GetString());
            Assert.Equal(JsonValueKind.Null, context.GetProperty("priorSessionHigh").ValueKind);
            Assert.Equal(JsonValueKind.Null, context.GetProperty("priorSessionClose").ValueKind);
            Assert.True(context.GetProperty("observedHigh").GetDecimal() > 0);
            Assert.Equal("MOE", context.GetProperty("symbol").GetString());
            Assert.True(contextView.GetProperty("currentReferencePrice").GetDecimal() > 0);
            Assert.DoesNotContain("BUY", priorJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SELL", priorJson, StringComparison.OrdinalIgnoreCase);
        }

        using (var unavailableResponse = await client.GetAsync(
                   "/api/v1/symbols/MOE/prior-context?sessionId=fixture-2026-10-22",
                   timeout.Token))
        using (var json = JsonDocument.Parse(await unavailableResponse.Content.ReadAsStringAsync(timeout.Token)))
        {
            Assert.Equal(HttpStatusCode.OK, unavailableResponse.StatusCode);
            Assert.Equal(
                "Unavailable",
                json.RootElement.GetProperty("context").GetProperty("completeness").GetString());
        }

        using (var configurationResponse = await client.GetAsync(
                   "/api/v1/configuration/effective",
                   timeout.Token))
        using (var json = JsonDocument.Parse(await configurationResponse.Content.ReadAsStringAsync(timeout.Token)))
        {
            Assert.Equal(HttpStatusCode.OK, configurationResponse.StatusCode);
            Assert.Equal("scanner-config-v19", json.RootElement.GetProperty("version").GetString());
            var marketSituation = json.RootElement.GetProperty("marketSituation");
            Assert.Equal(0.1m, marketSituation.GetProperty("testZonePercent").GetDecimal());
            Assert.Equal(0.25m, marketSituation.GetProperty("approachDistancePercent").GetDecimal());
            var marketReaction = json.RootElement.GetProperty("marketReaction");
            Assert.Equal(0.1m, marketReaction.GetProperty("confirmationDistancePercent").GetDecimal());
            Assert.Equal(300, marketReaction.GetProperty("maxEpisodeDurationSeconds").GetInt32());
        }

        var html = await client.GetStringAsync("/", timeout.Token);
        Assert.Contains("MARKET STRUCTURE", html);
        Assert.Contains("TRADE PLAN", html);
        Assert.Contains("PRIOR SESSION", html);
        Assert.Contains("PRE-TRADE READINESS", html);
        Assert.Contains("MARKET SITUATION", html);
        Assert.Contains("No tracked candidate opportunity", html);
        Assert.Contains("Support and resistance are historical reference areas, not guarantees.", html);
        Assert.Contains("Insufficient", html);
        Assert.Contains("not a probability of profit or a trading recommendation", html);
        Assert.DoesNotContain("Buy now", html);
        Assert.DoesNotContain("Sell now", html);
        Assert.DoesNotContain("Short now", html);
        Assert.DoesNotContain("Guaranteed", html);
    }

    [Fact]
    public async Task Dashboard_and_snapshot_api_display_confirmed_structure_from_the_scanner()
    {
        await using var factory = new StructureApplicationFactory();
        using var client = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        HttpResponseMessage? response = null;
        while (!timeout.IsCancellationRequested)
        {
            response?.Dispose();
            response = await client.GetAsync(
                "/api/v1/symbols/STRUCT/snapshot?sessionId=hosted-structure", timeout.Token);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                break;
            }

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
        }

        Assert.NotNull(response);
        using (response)
        using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token)))
        {
            var root = json.RootElement;
            Assert.Equal("Available", root.GetProperty("marketStructureStatus").GetString());
            var high = root.GetProperty("latestConfirmedSwingHigh");
            var low = root.GetProperty("latestConfirmedSwingLow");
            Assert.Equal("ConfirmedSwingHigh", high.GetProperty("type").GetString());
            Assert.Equal("ConfirmedSwingLow", low.GetProperty("type").GetString());
            Assert.Equal(107m, high.GetProperty("price").GetDecimal());
            Assert.Equal(104m, low.GetProperty("price").GetDecimal());
            Assert.Equal("STRUCT", high.GetProperty("symbol").GetString());
            Assert.Equal("hosted-structure", high.GetProperty("sessionId").GetString());
            Assert.Equal("features-v16", high.GetProperty("featureSetVersion").GetString());
            Assert.Equal("session-swing-fractal-v1", high.GetProperty("algorithmVersion").GetString());
            Assert.NotEqual(Guid.Empty, high.GetProperty("id").GetGuid());
            Assert.Equal(new DateTimeOffset(2026, 10, 23, 14, 30, 7, TimeSpan.Zero),
                high.GetProperty("eventTimeUtc").GetDateTimeOffset());
            Assert.Equal(new DateTimeOffset(2026, 10, 23, 14, 30, 9, TimeSpan.Zero),
                high.GetProperty("confirmedAtEventTimeUtc").GetDateTimeOffset());
            Assert.Equal(MarketEventIdentity.Create(new TradeEvent(
                "STRUCT", "hosted-structure",
                new DateTimeOffset(2026, 10, 23, 14, 30, 7, TimeSpan.Zero),
                new DateTimeOffset(2026, 10, 23, 14, 30, 8, TimeSpan.Zero),
                "RecordedFixture", "hosted-007", 107m, 10)),
                high.GetProperty("originEventIdentity").GetString());
            Assert.Equal(MarketEventIdentity.Create(new TradeEvent(
                "STRUCT", "hosted-structure",
                new DateTimeOffset(2026, 10, 23, 14, 30, 9, TimeSpan.Zero),
                new DateTimeOffset(2026, 10, 23, 14, 30, 10, TimeSpan.Zero),
                "RecordedFixture", "hosted-009", 104m, 10)),
                high.GetProperty("confirmationEventIdentity").GetString());
            Assert.Equal(factory.Store.Snapshot!.Id, root.GetProperty("id").GetGuid());
            Assert.NotNull(factory.Store.TradePlan);
            Assert.Equal("Bullish", root.GetProperty("structureState").GetString());
            Assert.Equal("Higher", root.GetProperty("highSwingRelationship").GetString());
            Assert.Equal("Higher", root.GetProperty("lowSwingRelationship").GetString());
            Assert.Equal(105m, root.GetProperty("previousConfirmedSwingHigh").GetProperty("price").GetDecimal());
            Assert.Equal(102m, root.GetProperty("previousConfirmedSwingLow").GetProperty("price").GetDecimal());
            Assert.Equal(0m, root.GetProperty("distanceToNearestConfirmedSwingHigh").GetDecimal());
            Assert.Equal(1m, root.GetProperty("distanceToNearestConfirmedSwingLow").GetDecimal());
            Assert.Equal(104m, root.GetProperty("nearestSupportCandidate").GetProperty("price").GetDecimal());
            Assert.Equal(105m, root.GetProperty("nearestResistanceCandidate").GetProperty("price").GetDecimal());
        }

        using (var opportunities = await client.GetAsync("/api/v1/opportunities", timeout.Token))
        using (var json = JsonDocument.Parse(await opportunities.Content.ReadAsStringAsync(timeout.Token)))
        {
            Assert.Equal(HttpStatusCode.OK, opportunities.StatusCode);
            var analysis = Assert.Single(json.RootElement.EnumerateArray());
            var plan = analysis.GetProperty("tradePlan");
            Assert.Equal("trade-plan-v1", plan.GetProperty("planDefinitionVersion").GetString());
            Assert.Equal(factory.Store.TradePlan!.Id, plan.GetProperty("id").GetGuid());
            Assert.Equal(factory.Store.TradePlan.Status.ToString(), plan.GetProperty("status").GetString());
            Assert.Equal(factory.Store.TradePlan.AsOfUtc, plan.GetProperty("asOfUtc").GetDateTimeOffset());
            var marketSituation = analysis.GetProperty("marketSituation");
            Assert.Equal("market-situation-v1", marketSituation.GetProperty("definitionVersion").GetString());
            Assert.Equal("STRUCT", marketSituation.GetProperty("symbol").GetString());
            Assert.Equal("CurrentSessionOnly", marketSituation.GetProperty("contextMode").GetString());
            Assert.Contains(
                marketSituation.GetProperty("eligibleLevels").EnumerateArray(),
                item => item.GetProperty("source").GetString() == "CurrentSwingLow");
            Assert.Equal(
                "TestingResistance",
                marketSituation.GetProperty("resistanceInteraction").GetProperty("situationType").GetString());
            Assert.Contains(factory.MarketSituationStore.Persisted, item => item.Id == marketSituation.GetProperty("id").GetGuid());
        }

        var html = await client.GetStringAsync("/", timeout.Token);
        Assert.Contains("MARKET SITUATION", html);
        Assert.Contains("Testing Resistance", html);
        Assert.Contains("Latest swing high: $107.00", html);
        Assert.Contains("TRADE PLAN", html);
        Assert.Contains("Candidate geometry only", html);
        Assert.Contains("PRE-TRADE READINESS", html);
        Assert.Contains("Latest swing low: $104.00", html);
        Assert.Contains("MARKET STRUCTURE", html);
        Assert.Contains("Nearest resistance candidate: $105.00 · support candidate: $104.00", html);
        Assert.Contains("structure-observations", html);
        Assert.Contains("not a probability of profit or a trading recommendation", html);
        Assert.DoesNotContain("BREAKDOWN CONFIRMED", html);
        Assert.DoesNotContain("BREAKOUT CONFIRMED", html);
    }

    [Fact]
    public async Task Api_and_dashboard_preserve_simultaneous_support_and_resistance_interactions()
    {
        await using var factory = new StructureApplicationFactory(simultaneousSituationSource: true);
        using var client = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        HttpResponseMessage? response = null;
        while (!timeout.IsCancellationRequested)
        {
            response?.Dispose();
            response = await client.GetAsync(
                "/api/v1/symbols/STRUCT/snapshot?sessionId=hosted-structure",
                timeout.Token);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                break;
            }

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
        }

        Assert.NotNull(response);
        response.Dispose();
        using (var opportunities = await client.GetAsync("/api/v1/opportunities", timeout.Token))
        using (var json = JsonDocument.Parse(await opportunities.Content.ReadAsStringAsync(timeout.Token)))
        {
            Assert.Equal(HttpStatusCode.OK, opportunities.StatusCode);
            var marketSituation = Assert.Single(json.RootElement.EnumerateArray())
                .GetProperty("marketSituation");
            Assert.Equal(
                "ApproachingSupport",
                marketSituation.GetProperty("supportInteraction").GetProperty("situationType").GetString());
            Assert.Equal(
                "ApproachingResistance",
                marketSituation.GetProperty("resistanceInteraction").GetProperty("situationType").GetString());
            Assert.Equal("Support", marketSituation.GetProperty("primarySituation").GetProperty("side").GetString());
        }

        var html = await client.GetStringAsync("/", timeout.Token);
        Assert.Contains("Approaching Support", html);
        Assert.Contains("Approaching Resistance", html);
        Assert.DoesNotContain("BUY", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELL", html, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StructureApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly bool _useFixtureSource;
        private readonly bool _simultaneousSituationSource;

        public StructureApplicationFactory(
            bool useFixtureSource = false,
            bool simultaneousSituationSource = false)
        {
            _useFixtureSource = useFixtureSource;
            _simultaneousSituationSource = simultaneousSituationSource;
        }

        private static string WebProjectPath => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "MarketOpportunity.Web"));

        public CapturingStore Store { get; } = new();

        public InMemoryReadinessStore ReadinessStore { get; } = new();

        public InMemoryMarketSituationStore MarketSituationStore { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(configuration =>
                configuration.AddInMemoryCollection(Settings()));
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseContentRoot(WebProjectPath);
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(Settings()));
            builder.ConfigureServices(services =>
            {
                if (!_useFixtureSource)
                {
                    services.RemoveAll<IMarketDataSource>();
                    services.AddScoped<IMarketDataSource>(_ =>
                        new SyntheticStructureSource(_simultaneousSituationSource));
                }
                services.RemoveAll<IScannerStore>();
                services.AddSingleton<IScannerStore>(Store);
                services.RemoveAll<IPreTradeReadinessStore>();
                services.AddSingleton<IPreTradeReadinessStore>(ReadinessStore);
                services.RemoveAll<IMarketSituationStore>();
                services.AddSingleton<IMarketSituationStore>(MarketSituationStore);
            });
        }

        private static Dictionary<string, string?> Settings() => new()
        {
            ["MarketData:Provider"] = "Fixture",
            ["ConnectionStrings:MarketOpportunity"] =
                "Server=localhost;Database=UnusedHostedTest;Integrated Security=true;TrustServerCertificate=true"
        };
    }

    private sealed class CapturingStore : IScannerStore
    {
        public FeatureSnapshot? Snapshot { get; private set; }
        public TradePlan? TradePlan { get; private set; }

        public Task PersistRunAsync(
            ScannerConfiguration configuration,
            IReadOnlyCollection<MarketEvent> marketEvents,
            FeatureSnapshot featureSnapshot,
            Opportunity opportunity,
            TradePlan tradePlan,
            CancellationToken cancellationToken)
        {
            Snapshot = featureSnapshot;
            TradePlan = tradePlan;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryReadinessStore : IPreTradeReadinessStore
    {
        public List<(TrackedOpportunity Tracked, PreTradeReadinessAssessment? Assessment)> Persisted { get; } = [];

        public Task<IReadOnlyList<TrackedOpportunitySnapshot>> RestoreActiveAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TrackedOpportunitySnapshot>>([]);

        public Task PersistAsync(
            TrackedOpportunity tracked,
            PreTradeReadinessAssessment? assessment,
            CancellationToken cancellationToken)
        {
            Persisted.Add((tracked, assessment));
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryMarketSituationStore : IMarketSituationStore
    {
        public List<MarketSituationAssessment> Persisted { get; } = [];

        public Task PersistAsync(
            MarketSituationAssessment assessment,
            CancellationToken cancellationToken)
        {
            Persisted.Add(assessment);
            return Task.CompletedTask;
        }
    }

    private sealed class SyntheticStructureSource(bool simultaneousSituation = false) : IMarketDataSource
    {
        public MarketDataCapabilities Capabilities { get; } =
            new("RecordedFixture", "Synthetic", MarketDataCoverage.Synthetic, false, true, true);

        public async IAsyncEnumerable<MarketEvent> ReadEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var start = new DateTimeOffset(2026, 10, 23, 14, 30, 0, TimeSpan.Zero);
            decimal[] prices = simultaneousSituation
                ? [101m, 100m, 99.9m, 100.3m, 100.1m, 100.2m, 100m]
                : [101.5m, 101m, 105m, 103m, 102m, 104m, 103m, 107m, 105m, 104m, 106m, 105m];
            for (var index = 0; index < prices.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new TradeEvent(
                    "STRUCT", "hosted-structure", start.AddSeconds(index), start.AddSeconds(index + 1),
                    "RecordedFixture", $"hosted-{index:D3}", prices[index], 10);
            }

            await Task.CompletedTask;
        }
    }
}
