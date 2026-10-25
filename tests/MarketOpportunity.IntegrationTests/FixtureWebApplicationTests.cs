using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using MarketOpportunity.Analytics;
using MarketOpportunity.Application;
using MarketOpportunity.Domain;
using MarketOpportunity.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace MarketOpportunity.IntegrationTests;

public sealed class FixtureWebApplicationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static string WebProjectPath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "MarketOpportunity.Web"));

    [Fact]
    public async Task Fixture_run_is_persisted_and_exposed_by_the_hosted_api()
    {
        var connectionString = GetTestConnectionString();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(WebProjectPath)
            .AddJsonFile("appsettings.json")
            .Build()
            .GetSection("Scanner")
            .Get<ScannerConfiguration>();
        Assert.NotNull(configuration);
        configuration.Validate();
        var source = new FixtureMarketDataSource(
            Path.Combine(WebProjectPath, "Fixtures", "momentum-continuation.json"));
        Assert.Equal(new MarketDataCapabilities(
            "RecordedFixture", "Synthetic", MarketDataCoverage.Synthetic, false, true, true),
            source.Capabilities);
        var fixtureEvents = new List<MarketEvent>();
        await foreach (var marketEvent in source.ReadEventsAsync(CancellationToken.None))
        {
            fixtureEvents.Add(marketEvent);
        }

        Assert.Equal(26, fixtureEvents.Count);
        var expectedEvents = fixtureEvents.DistinctBy(MarketEventIdentity.Create).ToArray();
        Assert.Equal(26, expectedEvents.Length);
        var expectedGroups = new Dictionary<(string Symbol, string SessionId), (int Count, OpportunityClassification Classification)>
        {
            [("MOE", "fixture-2026-10-22")] = (7, OpportunityClassification.ProbableLong),
            [("MOE", "fixture-2026-10-23")] = (6, OpportunityClassification.ProbableShort),
            [("QNT", "fixture-2026-10-22")] = (6, OpportunityClassification.ProbableShort),
            [("QNT", "fixture-2026-10-23")] = (7, OpportunityClassification.ProbableLong)
        };
        var groups = expectedEvents.GroupBy(item => (item.Symbol, item.SessionId)).ToArray();
        Assert.Equal(expectedGroups.Keys.OrderBy(key => key), groups.Select(group => group.Key).OrderBy(key => key));
        var expectedSnapshots = new Dictionary<(string Symbol, string SessionId), FeatureSnapshot>();
        var expectedOpportunities = new Dictionary<Guid, Opportunity>();
        foreach (var group in groups)
        {
            Assert.Equal(expectedGroups[group.Key].Count, group.Count());
            var state = new SymbolEventState(group.Key.Symbol, group.Key.SessionId, configuration.MaximumStateEvents);
            foreach (var marketEvent in group)
            {
                state.Add(marketEvent);
            }

            var features = new FeatureCalculator().Calculate(
                state, group.Max(item => item.EventTimeUtc), configuration, source.Capabilities);
            var opportunity = new MomentumContinuationDetector().Evaluate(features, configuration);
            Assert.Equal(expectedGroups[group.Key].Classification, opportunity.Classification);
            expectedSnapshots.Add(group.Key, features);
            expectedOpportunities.Add(opportunity.Id, opportunity);
        }

        var replayTime = expectedEvents.Max(item => item.EventTimeUtc);
        await using (var setupContext = new MarketOpportunityDbContext(
            new DbContextOptionsBuilder<MarketOpportunityDbContext>().UseSqlServer(connectionString).Options))
        {
            if (!await setupContext.Database.EnsureCreatedAsync())
            {
                await setupContext.Database.MigrateAsync();
            }
        }

        await using var factory = new FixtureWebApplicationFactory(connectionString, configuration);
        using var client = factory.CreateClient();

        using (var effectiveResponse = await client.GetAsync("/api/v1/configuration/effective"))
        {
            Assert.Equal(HttpStatusCode.OK, effectiveResponse.StatusCode);
            var effective = JsonSerializer.Deserialize<ScannerConfiguration>(
                await effectiveResponse.Content.ReadAsStringAsync(), JsonOptions);
            Assert.Equal(configuration, effective);
            Assert.Equal(30, effective!.SecondaryMomentumWindowSeconds);
        }

        using var status = await GetJsonWithRetryAsync(
            client,
            "/api/v1/system/status",
            root => root.GetProperty("eventsProcessed").GetInt32() == expectedEvents.Length);
        Assert.Equal("Recorded", status.RootElement.GetProperty("feedStatus").GetString());
        Assert.Equal("RecordedFixture", status.RootElement.GetProperty("source").GetString());
        Assert.Equal(replayTime, status.RootElement.GetProperty("latestEventTimeUtc").GetDateTimeOffset());

        using var opportunities = await GetJsonWithRetryAsync(
            client,
            "/api/v1/opportunities",
            root => root.GetArrayLength() == 4);
        var opportunityItems = opportunities.RootElement.EnumerateArray().ToArray();
        Assert.Equal(4, opportunityItems.Length);
        Assert.Equal(expectedOpportunities.Keys.OrderBy(id => id),
            opportunityItems.Select(item => item.GetProperty("opportunity").GetProperty("id").GetGuid()).OrderBy(id => id));

        var opportunityIds = new HashSet<Guid>();
        var httpOpportunities = new Dictionary<Guid, Opportunity>();
        var httpSnapshots = new Dictionary<Guid, FeatureSnapshot>();
        foreach (var item in opportunityItems)
        {
            var result = item.Deserialize<OpportunityPublicationReadModel>(JsonOptions);
            Assert.NotNull(result);
            var opportunity = result.Opportunity;
            Assert.True(opportunityIds.Add(opportunity.Id));
            var expectedOpportunity = expectedOpportunities[opportunity.Id];
            AssertJsonEqual(expectedOpportunity, opportunity);
            httpOpportunities.Add(opportunity.Id, opportunity);

            var expired = opportunity.SessionId == "fixture-2026-10-22";
            Assert.Equal(expired ? OpportunityLifecycleState.Expired : OpportunityLifecycleState.Active,
                result.Publication.State);
            Assert.Equal(expired ? "OpportunityExpired" : "PublicationAllowed", result.Publication.ReasonCode);
            Assert.Equal(replayTime, result.Publication.EvaluatedAtUtc);
            Assert.Equal(new OpportunityPublicationGate().Evaluate(
                expectedOpportunity, MarketDataHealthStatus.Recorded, replayTime), result.Publication);

            using var detail = await client.GetAsync($"/api/v1/opportunities/{opportunity.Id}");
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            using var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
            Assert.True(JsonElement.DeepEquals(item, detailJson.RootElement),
                $"List/detail payload mismatch for {opportunity.Id}.");

            using var snapshotResponse = await client.GetAsync(
                $"/api/v1/symbols/{opportunity.Symbol}/snapshot?sessionId={opportunity.SessionId}");
            Assert.Equal(HttpStatusCode.OK, snapshotResponse.StatusCode);
            using var snapshotJson = JsonDocument.Parse(await snapshotResponse.Content.ReadAsStringAsync());
            var snapshot = snapshotJson.RootElement.Deserialize<FeatureSnapshot>(JsonOptions);
            Assert.NotNull(snapshot);
            Assert.Equal(opportunity.Symbol, snapshot.Symbol);
            Assert.Equal(opportunity.SessionId, snapshot.SessionId);
            Assert.Equal("features-v5", snapshot.FeatureSetVersion);
            Assert.Equal("features-v5", opportunity.FeatureSetVersion);
            Assert.Equal("momentum-continuation-v1", opportunity.SetupDefinitionVersion);
            Assert.Equal("scanner-config-v5", opportunity.ConfigurationVersion);
            Assert.Equal(expectedSnapshots[(opportunity.Symbol, opportunity.SessionId)], snapshot);
            httpSnapshots.Add(snapshot.Id, snapshot);
        }

        Assert.Equal(expectedOpportunities.Keys.OrderBy(id => id), opportunityIds.OrderBy(id => id));
        Assert.Equal(expectedGroups.Keys.OrderBy(key => key),
            httpOpportunities.Values.Select(item => (item.Symbol, item.SessionId)).OrderBy(key => key));
        Assert.Equal(expectedSnapshots.Values.Select(item => item.Id).OrderBy(id => id),
            httpSnapshots.Keys.OrderBy(id => id));

        var unknownOpportunityId = Guid.NewGuid();
        Assert.DoesNotContain(unknownOpportunityId, expectedOpportunities.Keys);
        using (var unknownOpportunity = await client.GetAsync(
            $"/api/v1/opportunities/{unknownOpportunityId}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, unknownOpportunity.StatusCode);
        }

        const string unknownSymbol = "UNKNOWN-FIXTURE-SYMBOL";
        Assert.DoesNotContain(unknownSymbol, expectedGroups.Keys.Select(key => key.Symbol));
        using (var missingSymbolSnapshot = await client.GetAsync(
            $"/api/v1/symbols/{unknownSymbol}/snapshot"))
        {
            Assert.Equal(HttpStatusCode.NotFound, missingSymbolSnapshot.StatusCode);
        }

        const string unknownSession = "fixture-unknown-session";
        Assert.DoesNotContain(unknownSession, expectedGroups.Keys.Select(key => key.SessionId));
        foreach (var symbol in new[] { "MOE", "QNT" })
        {
            using var missingSessionSnapshot = await client.GetAsync(
                $"/api/v1/symbols/{symbol}/snapshot?sessionId={Uri.EscapeDataString(unknownSession)}");
            Assert.Equal(HttpStatusCode.NotFound, missingSessionSnapshot.StatusCode);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketOpportunityDbContext>();
        var persistedOpportunities = await dbContext.Opportunities
            .AsNoTracking()
            .Include(item => item.Evidence)
            .Where(item => opportunityIds.Contains(item.Id)
                && item.FeatureSetVersion == configuration.FeatureSetVersion
                && item.SetupDefinitionVersion == configuration.SetupDefinitionVersion
                && item.ConfigurationVersion == configuration.Version)
            .ToListAsync();
        Assert.Equal(opportunityIds.OrderBy(id => id), persistedOpportunities.Select(item => item.Id).OrderBy(id => id));
        foreach (var persisted in persistedOpportunities)
        {
            AssertPersistedOpportunity(httpOpportunities[persisted.Id], persisted);
        }

        var snapshotIds = httpSnapshots.Keys.ToArray();
        var persistedSnapshots = await dbContext.FeatureSnapshots
            .AsNoTracking()
            .Where(item => snapshotIds.Contains(item.Id)
                && item.FeatureSetVersion == configuration.FeatureSetVersion)
            .ToListAsync();
        Assert.Equal(snapshotIds.OrderBy(id => id), persistedSnapshots.Select(item => item.Id).OrderBy(id => id));
        foreach (var persisted in persistedSnapshots)
        {
            var snapshot = httpSnapshots[persisted.Id];
            Assert.Equal(snapshot.Symbol, persisted.Symbol);
            Assert.Equal(snapshot.SessionId, persisted.SessionId);
            Assert.Equal(snapshot.AsOfUtc, persisted.AsOfUtc);
            Assert.Equal(snapshot.FeatureSetVersion, persisted.FeatureSetVersion);
            Assert.Equal(snapshot, JsonSerializer.Deserialize<FeatureSnapshot>(persisted.SnapshotJson, JsonOptions));
        }

        var eventIdentities = expectedEvents.Select(MarketEventIdentity.Create).ToArray();
        var persistedEvents = await dbContext.MarketEvents
            .AsNoTracking()
            .Where(item => eventIdentities.Contains(item.EventIdentity))
            .ToListAsync();
        Assert.Equal(eventIdentities.OrderBy(identity => identity),
            persistedEvents.Select(item => item.EventIdentity).OrderBy(identity => identity));
        foreach (var expected in expectedEvents)
        {
            var identity = MarketEventIdentity.Create(expected);
            var persisted = Assert.Single(persistedEvents, item => item.EventIdentity == identity);
            Assert.Equal(MarketEventIdentity.ToGuid(identity), persisted.Id);
            Assert.Equal(expected.Symbol, persisted.Symbol);
            Assert.Equal(expected.SessionId, persisted.SessionId);
            Assert.Equal(expected.Provider, persisted.Provider);
            Assert.Equal(expected.SequenceId, persisted.SequenceId);
            Assert.Equal(expected.EventTimeUtc, persisted.EventTimeUtc);
            Assert.Equal(expected.ReceivedTimeUtc, persisted.ReceivedTimeUtc);
            Assert.Equal(expected is TradeEvent ? "Trade" : "Quote", persisted.EventType);
            Assert.Equal((expected as TradeEvent)?.Price, persisted.TradePrice);
            Assert.Equal((expected as TradeEvent)?.Size, persisted.TradeSize);
            Assert.Equal((expected as QuoteEvent)?.BidPrice, persisted.BidPrice);
            Assert.Equal((expected as QuoteEvent)?.AskPrice, persisted.AskPrice);
            Assert.Equal((expected as QuoteEvent)?.BidSize, persisted.BidSize);
            Assert.Equal((expected as QuoteEvent)?.AskSize, persisted.AskSize);
        }
    }

    private static void AssertJsonEqual<T>(T expected, T actual) =>
        Assert.True(JsonElement.DeepEquals(
            JsonSerializer.SerializeToElement(expected, JsonOptions),
            JsonSerializer.SerializeToElement(actual, JsonOptions)),
            $"Expected and actual {typeof(T).Name} payloads differ.");

    private static void AssertPersistedOpportunity(Opportunity expected, OpportunityEntity actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Symbol, actual.Symbol);
        Assert.Equal(expected.SessionId, actual.SessionId);
        Assert.Equal(expected.AsOfUtc, actual.AsOfUtc);
        Assert.Equal(expected.ValidUntilUtc, actual.ValidUntilUtc);
        Assert.Equal(expected.SetupType, actual.SetupType);
        Assert.Equal(expected.Bias.ToString(), actual.Bias);
        Assert.Equal(expected.Classification.ToString(), actual.Classification);
        Assert.Equal(expected.QualityScore, actual.QualityScore);
        Assert.Equal(expected.ReferencePrice, actual.ReferencePrice);
        Assert.Equal(expected.FeatureSetVersion, actual.FeatureSetVersion);
        Assert.Equal(expected.SetupDefinitionVersion, actual.SetupDefinitionVersion);
        Assert.Equal(expected.ConfigurationVersion, actual.ConfigurationVersion);
        Assert.Equal(expected.MarketData.Provider, actual.DataProvider);
        Assert.Equal(expected.MarketData.Feed, actual.DataFeed);
        Assert.Equal(expected.MarketData.Coverage.ToString(), actual.DataCoverage);
        Assert.Equal(expected.MarketData.IsRealTime, actual.IsRealTimeData);
        Assert.Equal(expected.MarketData.SupportsTrades, actual.SupportsTrades);
        Assert.Equal(expected.MarketData.SupportsQuotes, actual.SupportsQuotes);

        var expectedEvidence = expected.SupportingEvidence.Select(item => ("Supporting", item.Code, item.Description))
            .Concat(expected.ContradictoryEvidence.Select(item => ("Contradictory", item.Code, item.Description)))
            .Append(("Invalidation", "InvalidationCondition", expected.InvalidationCondition));
        Assert.Empty(expected.ActiveVetoes);
        Assert.Equal(expectedEvidence.OrderBy(item => item),
            actual.Evidence.Select(item => (item.Kind, item.Code, item.Description)).OrderBy(item => item));
        Assert.All(actual.Evidence, item => Assert.Equal(expected.Id, item.OpportunityId));
    }

    private static async Task<JsonDocument> GetJsonWithRetryAsync(
        HttpClient client,
        string requestUri,
        Func<JsonElement, bool> isReady)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Exception? lastFailure = null;
        while (!timeout.IsCancellationRequested)
        {
            try
            {
                using var response = await client.GetAsync(requestUri, timeout.Token);
                response.EnsureSuccessStatusCode();
                using var document = JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync(timeout.Token));
                if (isReady(document.RootElement))
                {
                    return JsonDocument.Parse(document.RootElement.GetRawText());
                }
            }
            catch (Exception exception) when (
                exception is HttpRequestException or JsonException or OperationCanceledException)
            {
                lastFailure = exception;
                if (timeout.IsCancellationRequested)
                {
                    break;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new TimeoutException(
            $"The fixture scanner did not produce a valid response from '{requestUri}' within 30 seconds.",
            lastFailure);
    }

    private static string GetTestConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("MARKETOPPORTUNITY_SQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Fixture web integration test was not run: set MARKETOPPORTUNITY_SQL_TEST_CONNECTION to a dedicated SQL Server test database.");
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

    private sealed class FixtureWebApplicationFactory(
        string connectionString,
        ScannerConfiguration configuration)
        : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            // Host configuration is available before Program captures provider/connection settings.
            builder.ConfigureHostConfiguration(configuration =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MarketOpportunity"] = connectionString,
                    ["MarketData:Provider"] = "Fixture"
                }));
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // A non-Development environment prevents loading local provider User Secrets.
            builder.UseEnvironment("Testing");
            builder.UseContentRoot(WebProjectPath);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MarketOpportunity"] = connectionString,
                    ["MarketData:Provider"] = "Fixture"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ScannerConfiguration>();
                services.AddSingleton(configuration);
                services.RemoveAll<IMarketDataSource>();
                services.AddScoped<IMarketDataSource>(_ => new FixtureMarketDataSource(
                    Path.Combine(WebProjectPath, "Fixtures", "momentum-continuation.json")));
                services.AddDbContext<MarketOpportunityDbContext>(options =>
                    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));
            });
        }
    }
}
