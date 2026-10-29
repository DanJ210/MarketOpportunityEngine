using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
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
    public async Task Offline_host_exports_final_diagnostics_and_persists_unchanged_canonical_events()
    {
        var connection = Environment.GetEnvironmentVariable("MARKETOPPORTUNITY_SQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException("SQL diagnostic verification requires a dedicated test database.");
        }
        var target = new SqlConnectionStringBuilder(connection);
        if (target.DataSource != "localhost,1433"
            || target.InitialCatalog != "MarketOpportunityEngine_LiveValidationTests")
        {
            throw new InvalidOperationException("Diagnostic verification requires the dedicated local live-validation database.");
        }
        await using (var guard = new SqlConnection(connection))
        {
            await guard.OpenAsync();
            await using var command = guard.CreateCommand();
            command.CommandText = "SELECT DB_NAME()";
            Assert.Equal(target.InitialCatalog, await command.ExecuteScalarAsync());
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
            }

            Assert.True(File.Exists(output));
            using var final = JsonDocument.Parse(await File.ReadAllTextAsync(output));
            Assert.Equal(4, final.RootElement.GetProperty("acceptedEvents").GetInt64());
            Assert.Equal(4, final.RootElement.GetProperty("persistenceAcknowledgedEvents").GetInt64());
            Assert.Equal(0, final.RootElement.GetProperty("channelDepth").GetInt32());
            Assert.Equal(1, final.RootElement.GetProperty("zeroAskQuotes").GetInt64());
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
        const string connection = "Server=localhost,1433;Database=Validation;User ID=sa;Password=" + password;
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
        builder.Services.AddSingleton(_ =>
        {
            var diagnostics = new LiveValidationDiagnostics(TimeProvider.System);
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
        string connection, string session, string directory, MarketEvent[] events)
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
                    new OfflineSource(events, provider.GetRequiredService<LiveValidationDiagnostics>()));
            });
        }
    }

    private sealed class OfflineSource(MarketEvent[] events, LiveValidationDiagnostics diagnostics)
        : ILiveMarketDataSource
    {
        public MarketDataCapabilities Capabilities { get; } =
            new("OfflineDiagnostic", "offline", MarketDataCoverage.Synthetic, false, true, true);

        public async IAsyncEnumerable<MarketEvent> ReadEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            diagnostics.Configure(events[0].SessionId, "offline", ["A1TEST"]);
            foreach (var marketEvent in events)
            {
                diagnostics.Produced(marketEvent, Guid.Empty, marketEvent.ReceivedTimeUtc,
                    new("V", "V", ["R"], "C"));
                yield return marketEvent;
            }
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
