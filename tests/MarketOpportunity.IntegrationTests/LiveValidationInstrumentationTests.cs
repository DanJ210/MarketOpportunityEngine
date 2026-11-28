using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using MarketOpportunity.Analytics;
using MarketOpportunity.Application;
using MarketOpportunity.Domain;
using MarketOpportunity.Infrastructure;
using MarketOpportunity.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MarketOpportunity.IntegrationTests;

public sealed class LiveValidationInstrumentationTests
{
    [Fact]
    public async Task Sustained_sql_backed_host_drains_repeated_persistence_batches()
    {
        var connection = IntegrationTestConfiguration.GetSqlTestConnectionString();
        var session = "a1-sustained-" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), session);
        var start = DateTimeOffset.UtcNow.AddSeconds(-20);
        var events = Enumerable.Range(0, 16)
            .Select(index => (MarketEvent)new TradeEvent(
                "SUST",
                session,
                start.AddSeconds(index),
                start.AddSeconds(index).AddMilliseconds(10),
                "OfflineSustained",
                $"sustained-{index}",
                100m + index * 0.01m,
                10))
            .ToArray();
        try
        {
            await using (var factory = new DiagnosticFactory(
                connection,
                session,
                directory,
                events,
                delayedBatches: true))
            {
                using var client = factory.CreateClient();
                var diagnostics = factory.Services.GetRequiredService<LiveValidationDiagnostics>();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
                while (diagnostics.Snapshot().PersistenceAcknowledgedEvents < events.Length)
                {
                    await Task.Delay(25, timeout.Token);
                }

                using var opportunities = await client.GetAsync(
                    "/api/v1/opportunities",
                    timeout.Token);
                Assert.Equal(HttpStatusCode.OK, opportunities.StatusCode);
                var snapshot = diagnostics.Snapshot();
                Assert.Equal(events.Length, snapshot.AcceptedEvents);
                Assert.Equal(events.Length, snapshot.DequeuedEvents);
                Assert.Equal(events.Length, snapshot.EventsAnalyzed);
                Assert.Equal(events.Length, snapshot.PersistenceAcknowledgedEvents);
                Assert.InRange(snapshot.PersistenceBatches, 4, 5);
                Assert.Equal(0, snapshot.PersistenceFailures);
                Assert.Equal(0, snapshot.RemainingEvents);
                Assert.Equal(0, snapshot.ChannelDepth);
                Assert.Equal(5000, snapshot.ChannelCapacity);
                Assert.False(snapshot.ChannelCapacityReached);
                Assert.Equal(0, snapshot.ChannelBackpressureWaits);
                Assert.True(snapshot.MarketSituationPersistence.SuccessfulWriteCount >= 4);
                Assert.True(
                    snapshot.MarketSituationPersistence.MaximumCompactAssessmentBytes > 0);
                Assert.True(
                    snapshot.MarketSituationPersistence.MaximumCompactAssessmentBytes <= 16 * 1024);
                Console.WriteLine(
                    $"Sustained SQL host: events={snapshot.AcceptedEvents}, " +
                    $"batches={snapshot.PersistenceBatches}, failures={snapshot.PersistenceFailures}, " +
                    $"maxPersistenceMs={snapshot.MarketSituationPersistence.MaximumElapsedMs:F3}, " +
                    $"candidateSetsCreated={snapshot.MarketSituationPersistence.CandidateSetsCreated}, " +
                    $"candidateSetsReused={snapshot.MarketSituationPersistence.CandidateSetsReused}, " +
                    $"maxCandidates={snapshot.MarketSituationPersistence.MaximumCandidateCount}, " +
                    $"compactMaxBytes={snapshot.MarketSituationPersistence.MaximumCompactAssessmentBytes}, " +
                    $"capacity={snapshot.ChannelCapacity}, maxDepth={snapshot.MaximumSampledChannelDepth}, " +
                    $"capacityReached={snapshot.ChannelCapacityReached}, " +
                    $"writerWaits={snapshot.ChannelBackpressureWaits}, " +
                    $"maxWaitMs={snapshot.LongestChannelWaitMs:F3}, " +
                    $"oldestMs={snapshot.MaximumOldestQueuedEventAgeMs:F3}, " +
                    $"finalDepth={snapshot.ChannelDepth}.");
            }

            await using var db = new MarketOpportunityDbContext(
                new DbContextOptionsBuilder<MarketOpportunityDbContext>()
                    .UseSqlServer(connection)
                    .Options);
            Assert.Equal(
                events.Length,
                await db.MarketEvents.LongCountAsync(
                    item => item.SessionId == session));
            Assert.True(
                await db.MarketSituationAssessments.AnyAsync(
                    item => item.SessionId == session
                        && item.PersistenceVersion ==
                        CompactMarketSituationAssessment.CurrentPersistenceContractVersion));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Scanner_background_failure_records_terminal_persistence_outcome_and_nonzero_exit()
    {
        var builder = Host.CreateApplicationBuilder();
        var exitCodes = new List<int>();
        var clock = TimeProvider.System;
        var terminal = new ApplicationTerminalOutcomeTracker(clock, exitCodes.Add);
        using var diagnostics = new LiveValidationDiagnostics(
            clock,
            terminalOutcome: terminal);
        var scanner = new ScannerState(timeProvider: clock);
        var store = new TerminalFailureStore();
        builder.Services.AddSingleton(terminal);
        builder.Services.AddSingleton(diagnostics);
        builder.Services.AddSingleton(scanner);
        builder.Services.AddScoped(_ => new ScannerPipeline(
            new TerminalFailureSource(),
            store,
            scanner,
            new ScannerConfiguration(),
            new FeatureCalculator(),
            new MomentumContinuationDetector(),
            clock,
            diagnostics: diagnostics));
        builder.Services.Configure<HostOptions>(options =>
        {
            options.BackgroundServiceExceptionBehavior =
                BackgroundServiceExceptionBehavior.StopHost;
            options.ShutdownTimeout = TimeSpan.FromSeconds(5);
        });
        builder.Services.AddHostedService<ScannerRunnerService>();
        using var host = builder.Build();
        var stopping = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = host.Services.GetRequiredService<IHostApplicationLifetime>()
            .ApplicationStopping.Register(() => stopping.TrySetResult());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await host.StartAsync(timeout.Token);
        await store.Started.Task.WaitAsync(timeout.Token);
        while (terminal.Read() is null && !timeout.IsCancellationRequested)
        {
            await Task.Delay(10, timeout.Token);
        }
        await stopping.Task.WaitAsync(timeout.Token);

        var outcome = terminal.Read();
        Assert.NotNull(outcome);
        Assert.Equal(
            ApplicationTerminalOutcomeCategory.PersistenceTerminalFailure,
            outcome.Category);
        Assert.Equal(1, outcome.ProcessExitCode);
        Assert.False(outcome.Expected);
        Assert.True(outcome.HostShutdownRequested);
        Assert.Equal([1], exitCodes);
        Assert.Equal(
            "ApplicationFailure",
            diagnostics.Snapshot().ShutdownTrigger);
    }

    [Fact]
    public async Task Offline_host_exports_final_diagnostics_and_persists_unchanged_canonical_events()
    {
        var connection = IntegrationTestConfiguration.GetSqlTestConnectionString();
        var target = new SqlConnectionStringBuilder(connection);
        await using (var guard = new SqlConnection(connection))
        {
            await guard.OpenAsync();
            await using var command = guard.CreateCommand();
            command.CommandText = "SELECT DB_NAME()";
            Assert.Equal(
                target.InitialCatalog,
                await command.ExecuteScalarAsync() as string,
                ignoreCase: true);
        }
        var session = "a1-offline-diagnostic-" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), session);
        var now = DateTimeOffset.UtcNow;
        MarketEvent[] events =
        [
            new TradeEvent("A1TEST", session, now.AddSeconds(-3), now, "OfflineDiagnostic", "1", 100m, 10),
            new TradeEvent("A1TEST", session, now.AddSeconds(-2), now, "OfflineDiagnostic", "2", 101m, 10),
            new TradeEvent("A1TEST", session, now.AddSeconds(-1), now, "OfflineDiagnostic", "3", 102m, 10),
            new QuoteEvent("A1TEST", session, now, now, "OfflineDiagnostic", null, 102m, 0m, 1, 0)
        ];
        var output = Path.Combine(directory, session + ".diagnostics.json");
        try
        {
            await using (var factory = new DiagnosticFactory(connection, session, directory, events))
            {
                using var client = factory.CreateClient();
                var diagnostics = factory.Services.GetRequiredService<LiveValidationDiagnostics>();
                var scanner = factory.Services.GetRequiredService<ScannerState>();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                while (diagnostics.Snapshot().PersistenceAcknowledgedEvents < events.Length
                    || scanner.Read().Status.EventsProcessed < events.Length)
                {
                    await Task.Delay(20, timeout.Token);
                }
                using var response = await client.GetAsync("/api/v1/system/status", timeout.Token);
                response.EnsureSuccessStatusCode();
                using var status = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
                Assert.Equal(4, status.RootElement.GetProperty("eventsProcessed").GetInt32());
                var page = await client.GetStringAsync("/", timeout.Token);
                Assert.Contains("A1TEST", page);
                Assert.Contains("OfflineDiagnostic", page);
                Assert.Equal(4, diagnostics.Snapshot().EventsAnalyzed);
                Assert.Equal(1, diagnostics.Snapshot().ZeroAskQuotes);
                Assert.True(
                    diagnostics.Snapshot()
                        .MarketSituationPersistence.SuccessfulWriteCount >= 1);
                Assert.True(
                    diagnostics.Snapshot()
                        .MarketSituationPersistence.MaximumCompactAssessmentBytes > 0);
            }

            Assert.True(File.Exists(output));
            using var final = JsonDocument.Parse(await File.ReadAllTextAsync(output));
            Assert.Equal(4, final.RootElement.GetProperty("acceptedEvents").GetInt64());
            Assert.Equal(4, final.RootElement.GetProperty("persistenceAcknowledgedEvents").GetInt64());
            Assert.Equal(0, final.RootElement.GetProperty("channelDepth").GetInt32());
            Assert.Equal(1, final.RootElement.GetProperty("zeroAskQuotes").GetInt64());
            Assert.True(
                final.RootElement.GetProperty("marketSituationPersistence")
                    .GetProperty("successfulWriteCount").GetInt64() >= 1);
            Assert.True(
                final.RootElement.GetProperty("marketSituationPersistence")
                    .GetProperty("maximumCompactAssessmentBytes").GetInt32() > 0);
            var group = final.RootElement.GetProperty("providerMetadata").EnumerateArray()
                .Single(item => item.GetProperty("eventType").GetString() == "Quote");
            Assert.Equal("R", group.GetProperty("metadata").GetProperty("conditions")[0].GetString());
            Assert.Equal("V", group.GetProperty("metadata").GetProperty("askExchange").GetString());
            Assert.Equal(session, final.RootElement.GetProperty("sessionId").GetString());
            Assert.Equal(0, final.RootElement.GetProperty("configuredDurationSeconds").GetInt32());
            var sessionLog = final.RootElement.GetProperty("applicationLogPath").GetString();
            Assert.Equal(Path.GetFullPath(Path.Combine(directory, session + ".app.log")), sessionLog);
            Assert.DoesNotContain(target.Password is { Length: > 0 } password ? password : "\u0000",
                await File.ReadAllTextAsync(sessionLog!));
            await using var db = new MarketOpportunityDbContext(
                new DbContextOptionsBuilder<MarketOpportunityDbContext>().UseSqlServer(connection).Options);
            var saved = await db.MarketEvents.Where(item => item.SessionId == session).ToListAsync();
            Assert.Equal(events.Length, saved.Count);
            foreach (var marketEvent in events)
            {
                var row = Assert.Single(saved, item => item.EventIdentity == MarketEventIdentity.Create(marketEvent));
                Assert.Equal(marketEvent.EventTimeUtc, row.EventTimeUtc);
                Assert.Equal(marketEvent.ReceivedTimeUtc, row.ReceivedTimeUtc);
            }
            Assert.Equal(0m, Assert.Single(saved, item => item.EventType == "Quote").AskPrice);
            Assert.True(await db.Opportunities.AnyAsync(item => item.SessionId == session
                && item.DataProvider == "OfflineDiagnostic" && item.DataCoverage == "Synthetic"));
        }
        finally
        {
            if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); }
        }
    }

    [Fact]
    public async Task Diagnostic_duration_requests_normal_host_shutdown()
    {
        var session = "a1-duration-" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), session);
        var logPath = LiveValidationSession.ApplicationLogPath(directory, session);
        try
        {
            using (var host = BuildEvidenceHost(session, directory, logPath, durationSeconds: 5))
            {
                var diagnostics = host.Services.GetRequiredService<LiveValidationDiagnostics>();
                var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
                var triggerAtStop = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var registration = lifetime.ApplicationStopping.Register(
                    () => triggerAtStop.TrySetResult(diagnostics.Snapshot().ShutdownTrigger));
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await host.StartAsync(timeout.Token);
                try
                {
                    Assert.Equal("DurationElapsed", await triggerAtStop.Task.WaitAsync(timeout.Token));
                }
                finally
                {
                    await host.StopAsync(timeout.Token);
                }
            }

            using var final = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(directory, session + ".diagnostics.json")));
            Assert.Equal("DurationElapsed", final.RootElement.GetProperty("shutdownTrigger").GetString());
            Assert.Equal(5, final.RootElement.GetProperty("configuredDurationSeconds").GetInt32());
            Assert.Equal(JsonValueKind.String, final.RootElement.GetProperty("shutdownRequestedUtc").ValueKind);
            Assert.Equal(session, final.RootElement.GetProperty("sessionId").GetString());
            Assert.Equal(
                "ValidationDurationReached",
                final.RootElement.GetProperty("terminalOutcome")
                    .GetProperty("category").GetString());
            Assert.True(final.RootElement.GetProperty("validationSuccessful").GetBoolean());
            Assert.Equal(
                0,
                final.RootElement.GetProperty("terminalOutcome")
                    .GetProperty("processExitCode").GetInt32());
            var exportedLog = final.RootElement.GetProperty("applicationLogPath").GetString();
            Assert.Equal(logPath, exportedLog);
            Assert.StartsWith(session + ".", Path.GetFileName(exportedLog));
            var log = await File.ReadAllTextAsync(logPath);
            Assert.Contains("requesting normal host shutdown", log);
            Assert.Contains("trigger=DurationElapsed", log);
        }
        finally
        {
            if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); }
        }
    }

    [Fact]
    public async Task Console_interrupt_records_operator_cancellation_before_host_stop()
    {
        var session = "a1-operator-" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), session);
        var logPath = LiveValidationSession.ApplicationLogPath(directory, session);
        try
        {
            using (var host = BuildEvidenceHost(session, directory, logPath, durationSeconds: 600))
            {
                var diagnostics = host.Services.GetRequiredService<LiveValidationDiagnostics>();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await host.StartAsync(timeout.Token);
                LiveValidationReportService.OnSignal(diagnostics, PosixSignal.SIGTERM);
                Assert.Equal("Unknown", diagnostics.Snapshot().ShutdownTrigger);
                // Mirrors the console lifetime: the interrupt handler records, then the host stops.
                LiveValidationReportService.OnSignal(diagnostics, PosixSignal.SIGINT);
                host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
                await host.StopAsync(timeout.Token);
            }

            using var final = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(directory, session + ".diagnostics.json")));
            Assert.Equal("OperatorCancellation", final.RootElement.GetProperty("shutdownTrigger").GetString());
            Assert.Equal(
                "CancelledByRequestedShutdown",
                final.RootElement.GetProperty("terminalOutcome")
                    .GetProperty("category").GetString());
            Assert.False(
                final.RootElement.GetProperty("validationSuccessful").GetBoolean());
            Assert.Equal(session, final.RootElement.GetProperty("sessionId").GetString());
            Assert.Contains("trigger=OperatorCancellation", await File.ReadAllTextAsync(logPath));
        }
        finally
        {
            if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); }
        }
    }

    [Fact]
    public void Session_log_redacts_connection_strings_provider_keys_and_credential_assignments()
    {
        const string password = "SuperSecretPass123!";
        const string connection = "Server=127.0.0.1,14333;Database=Validation;User ID=sa;Password=" + password;
        const string apiKey = "AKFAKEALPACAKEY0001";
        const string secretKey = "fakeAlpacaSecretValue9876";
        const string massiveKey = "fakeMassiveApiKey5555";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MarketOpportunity"] = connection,
            ["MarketData:Alpaca:ApiKey"] = apiKey,
            ["MarketData:Alpaca:SecretKey"] = secretKey,
            ["MarketData:Massive:ApiKey"] = massiveKey,
            ["MarketData:Alpaca:Feed"] = "iex"
        }).Build();
        var session = "a1-redaction-" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), session);
        var path = LiveValidationSession.ApplicationLogPath(directory, session);
        try
        {
            using (var provider = new SessionFileLoggerProvider(path, LogRedactor.FromConfiguration(configuration), TimeProvider.System))
            {
                var logger = provider.CreateLogger("Validation");
                logger.LogInformation("Connecting with {Connection}", connection);
                logger.LogWarning("Auth {Key} {Secret} {Massive} feed=iex", apiKey, secretKey, massiveKey);
                logger.LogError(new InvalidOperationException("Login failed; Pwd=" + password),
                    "Unrelated password=abc12345 token: tok-998877 remains hidden");
            }

            var text = File.ReadAllText(path);
            foreach (var secret in new[] { password, connection, apiKey, secretKey, massiveKey, "abc12345", "tok-998877" })
            {
                Assert.DoesNotContain(secret, text);
            }
            Assert.Contains(LogRedactor.Mask, text);
            Assert.Contains("feed=iex", text);
            Assert.Contains("InvalidOperationException", text);
        }
        finally
        {
            if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); }
        }
    }

    private static IHost BuildEvidenceHost(string session, string directory, string logPath, int durationSeconds)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MarketData:Alpaca:SessionId"] = session,
            ["MarketData:Diagnostics:Directory"] = directory,
            ["MarketData:Diagnostics:DurationSeconds"] = durationSeconds.ToString()
        });
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ILoggerProvider>(provider => new SessionFileLoggerProvider(
            logPath, LogRedactor.FromConfiguration(builder.Configuration), TimeProvider.System));
        builder.Services.AddSingleton(serviceProvider =>
            new ApplicationTerminalOutcomeTracker(
                serviceProvider.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton(serviceProvider =>
        {
            var diagnostics = new LiveValidationDiagnostics(
                TimeProvider.System,
                terminalOutcome:
                    serviceProvider.GetRequiredService<ApplicationTerminalOutcomeTracker>());
            diagnostics.Configure(session, "test", ["AAA"]);
            diagnostics.ConfigureRun(durationSeconds, logPath);
            return diagnostics;
        });
        builder.Services.AddHostedService<LiveValidationReportService>();
        return builder.Build();
    }

    private static string WebRoot([CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "src", "MarketOpportunity.Web"));

    private sealed class DiagnosticFactory(
        string connection,
        string session,
        string directory,
        MarketEvent[] events,
        bool delayedBatches = false)
        : WebApplicationFactory<Program>
    {
        private Dictionary<string, string?> Settings => new()
        {
            ["ConnectionStrings:MarketOpportunity"] = connection,
            ["MarketData:Provider"] = "Alpaca",
            ["MarketData:Alpaca:SessionId"] = session,
            ["MarketData:Diagnostics:Enabled"] = "true",
            ["MarketData:Diagnostics:DurationSeconds"] = "0",
            ["MarketData:Diagnostics:Directory"] = directory
        };

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(Settings));
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseContentRoot(WebRoot());
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(Settings));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMarketDataSource>();
                services.AddScoped<IMarketDataSource>(provider =>
                    new OfflineSource(
                        events,
                        provider.GetRequiredService<LiveValidationDiagnostics>(),
                        delayedBatches));
                services.RemoveAll<IPriorSessionContextProvider>();
                services.AddSingleton<IPriorSessionContextProvider>(
                    new OfflinePriorSessionContextProvider());
            });
        }
    }

    private sealed class OfflinePriorSessionContextProvider : IPriorSessionContextProvider
    {
        public Task<PriorSessionContextView> GetContextAsync(
            string symbol,
            string currentSessionId,
            DateTimeOffset currentEventTimeUtc,
            decimal? currentReferencePrice,
            CancellationToken cancellationToken)
        {
            var context = new PriorSessionContextBuilder(new FeatureCalculator())
                .BuildUnavailable(
                    symbol,
                    currentSessionId,
                    "Offline hosted validation has no approved prior-session history.");
            return Task.FromResult(PriorSessionContextBuilder.CreateView(
                context,
                currentReferencePrice));
        }
    }

    private sealed class TerminalFailureSource : ILiveMarketDataSource
    {
        public async IAsyncEnumerable<MarketEvent> ReadEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            yield return new TradeEvent(
                "FAIL",
                "terminal-failure",
                now,
                now,
                "UnitTest",
                "failure-event",
                100m,
                10);
            await Task.CompletedTask;
        }
    }

    private sealed class TerminalFailureStore : IScannerStore
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task PersistRunAsync(
            ScannerConfiguration configuration,
            IReadOnlyCollection<MarketEvent> marketEvents,
            FeatureSnapshot featureSnapshot,
            Opportunity opportunity,
            TradePlan tradePlan,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            throw new IOException("Controlled terminal persistence failure.");
        }
    }

    private sealed class OfflineSource(
        MarketEvent[] events,
        LiveValidationDiagnostics diagnostics,
        bool delayedBatches = false)
        : ILiveMarketDataSource
    {
        public MarketDataCapabilities Capabilities { get; } =
            new("OfflineDiagnostic", "offline", MarketDataCoverage.Synthetic, false, true, true);

        public async IAsyncEnumerable<MarketEvent> ReadEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            diagnostics.Configure(events[0].SessionId, "offline", ["A1TEST"]);
            for (var index = 0; index < events.Length; index++)
            {
                var marketEvent = events[index];
                diagnostics.Produced(marketEvent, Guid.Empty, marketEvent.ReceivedTimeUtc,
                    new("V", "V", ["R"], "C"));
                yield return marketEvent;
                if (delayedBatches
                    && (index + 1) % 4 == 0
                    && index + 1 < events.Length)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(5_250), cancellationToken);
                }
            }
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
