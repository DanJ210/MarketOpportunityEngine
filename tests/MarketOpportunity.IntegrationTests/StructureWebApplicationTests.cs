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

        var html = await client.GetStringAsync("/", timeout.Token);
        Assert.Contains("MARKET STRUCTURE", html);
        Assert.Contains("TRADE PLAN", html);
        Assert.Contains("Insufficient", html);
        Assert.Contains("not a probability of profit or a trading recommendation", html);
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
            var plan = Assert.Single(json.RootElement.EnumerateArray())
                .GetProperty("tradePlan");
            Assert.Equal("trade-plan-v1", plan.GetProperty("planDefinitionVersion").GetString());
            Assert.Equal(factory.Store.TradePlan!.Id, plan.GetProperty("id").GetGuid());
            Assert.Equal(factory.Store.TradePlan.Status.ToString(), plan.GetProperty("status").GetString());
            Assert.Equal(factory.Store.TradePlan.AsOfUtc, plan.GetProperty("asOfUtc").GetDateTimeOffset());
        }

        var html = await client.GetStringAsync("/", timeout.Token);
        Assert.Contains("Latest swing high: $107.00", html);
        Assert.Contains("TRADE PLAN", html);
        Assert.Contains("Candidate geometry only", html);
        Assert.Contains("Latest swing low: $104.00", html);
        Assert.Contains("MARKET STRUCTURE", html);
        Assert.Contains("Nearest resistance candidate: $105.00 · support candidate: $104.00", html);
        Assert.Contains("structure-observations", html);
        Assert.Contains("not a probability of profit or a trading recommendation", html);
    }

    private sealed class StructureApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly bool _useFixtureSource;

        public StructureApplicationFactory(bool useFixtureSource = false)
        {
            _useFixtureSource = useFixtureSource;
        }

        private static string WebProjectPath => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "MarketOpportunity.Web"));

        public CapturingStore Store { get; } = new();

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
                    services.AddScoped<IMarketDataSource>(_ => new SyntheticStructureSource());
                }
                services.RemoveAll<IScannerStore>();
                services.AddSingleton<IScannerStore>(Store);
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

    private sealed class SyntheticStructureSource : IMarketDataSource
    {
        public MarketDataCapabilities Capabilities { get; } =
            new("RecordedFixture", "Synthetic", MarketDataCoverage.Synthetic, false, true, true);

        public async IAsyncEnumerable<MarketEvent> ReadEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var start = new DateTimeOffset(2026, 10, 23, 14, 30, 0, TimeSpan.Zero);
            decimal[] prices = [101.5m, 101m, 105m, 103m, 102m, 104m, 103m, 107m, 105m, 104m, 106m, 105m];
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
