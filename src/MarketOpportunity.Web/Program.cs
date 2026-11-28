using System.Text.Json;
using System.Text.Json.Serialization;
using MarketOpportunity.Analytics;
using MarketOpportunity.Application;
using MarketOpportunity.Domain;
using MarketOpportunity.Infrastructure;
using MarketOpportunity.Web;
using MarketOpportunity.Web.Components;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHealthChecks();
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation())
    .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation().AddRuntimeInstrumentation()
        .AddMeter(LiveValidationDiagnostics.MeterName));
if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    builder.Services.AddOpenTelemetry()
        .WithTracing(tracing => tracing.AddOtlpExporter())
        .WithMetrics(metrics => metrics.AddOtlpExporter());
}

var scannerConfiguration = builder.Configuration.GetSection("Scanner").Get<ScannerConfiguration>()
    ?? new ScannerConfiguration();
scannerConfiguration.Validate();
builder.Services.AddSingleton(scannerConfiguration);
var recoveryOptions = builder.Configuration.GetSection("MarketData:Recovery")
    .Get<LiveMarketDataRecoveryOptions>() ?? new LiveMarketDataRecoveryOptions();
recoveryOptions.Validate();
builder.Services.AddSingleton(recoveryOptions);
var livePipelineOptions = builder.Configuration.GetSection("Operations")
    .Get<LivePipelineOptions>() ?? new LivePipelineOptions();
livePipelineOptions.Validate();
builder.Services.AddSingleton(livePipelineOptions);
var priorSessionWarmupOptions = builder.Configuration
    .GetSection("Operations:PriorSessionWarmup")
    .Get<PriorSessionWarmupOptions>() ?? new PriorSessionWarmupOptions();
priorSessionWarmupOptions.Validate();
builder.Services.AddSingleton(priorSessionWarmupOptions);
var marketSituationPersistenceOptions =
    MarketSituationPersistenceOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(marketSituationPersistenceOptions);
var marketSituationEvidenceOptions = builder.Configuration
    .GetSection("Operations:MarketSituationEvidence")
    .Get<MarketSituationEvidenceReadOptions>() ?? new MarketSituationEvidenceReadOptions();
marketSituationEvidenceOptions.Validate();
builder.Services.AddSingleton(marketSituationEvidenceOptions);
var evidenceJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    Converters = { new JsonStringEnumConverter() }
};
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = livePipelineOptions.GracefulShutdownTimeout + TimeSpan.FromSeconds(10);
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
});
builder.Services.AddSingleton<Action<string, string>>(serviceProvider =>
{
    var logger = serviceProvider.GetRequiredService<ILogger<ScannerPipeline>>();
    return (provider, exceptionType) => logger.LogWarning(
        "Market-data source {Source} disconnected ({ExceptionType}); retrying after the configured delay.",
        provider,
        exceptionType);
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(serviceProvider =>
    new ApplicationTerminalOutcomeTracker(
        serviceProvider.GetRequiredService<TimeProvider>()));
builder.Services.AddHttpClient<AlpacaMarketSessionCalendar>(client =>
    client.BaseAddress = new Uri("https://api.alpaca.markets/"));
builder.Services.AddHttpClient<AlpacaHistoricalMarketDataSource>(client =>
    client.BaseAddress = new Uri("https://data.alpaca.markets/"));
var diagnosticEnabled = builder.Configuration.GetValue<bool>("MarketData:Diagnostics:Enabled");
if (diagnosticEnabled)
{
    if (!string.Equals(builder.Configuration["MarketData:Provider"], "Alpaca", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Live validation diagnostics require the Alpaca source.");
    }
    var duration = builder.Configuration.GetValue<int>("MarketData:Diagnostics:DurationSeconds");
    if (duration is < 0 or > 3600)
    {
        throw new InvalidOperationException("Diagnostic duration must be between 0 and 3600 seconds.");
    }
    string? applicationLogPath = null;
    var diagnosticDirectory = builder.Configuration["MarketData:Diagnostics:Directory"];
    if (!string.IsNullOrWhiteSpace(diagnosticDirectory))
    {
        applicationLogPath = LiveValidationSession.ApplicationLogPath(
            diagnosticDirectory, builder.Configuration["MarketData:Alpaca:SessionId"]);
        var sessionLogPath = applicationLogPath;
        var redactor = LogRedactor.FromConfiguration(builder.Configuration);
        builder.Services.AddSingleton<ILoggerProvider>(serviceProvider => new SessionFileLoggerProvider(
            sessionLogPath, redactor, serviceProvider.GetRequiredService<TimeProvider>()));
        builder.Logging.AddFilter<SessionFileLoggerProvider>("Microsoft.EntityFrameworkCore", LogLevel.Warning);
    }
    builder.Services.AddSingleton(serviceProvider =>
    {
        var logger = serviceProvider.GetRequiredService<ILogger<LiveValidationDiagnostics>>();
        var diagnostics = new LiveValidationDiagnostics(
            serviceProvider.GetRequiredService<TimeProvider>(),
            diagnostic => logger.LogInformation(
                "Live validation transition/anomaly {Diagnostic}",
                diagnostic),
            serviceProvider.GetRequiredService<ApplicationTerminalOutcomeTracker>());
        diagnostics.ConfigureRun(duration, applicationLogPath);
        return diagnostics;
    });
    // Registered before the scanner so final export follows scanner shutdown.
    builder.Services.AddHostedService<LiveValidationReportService>();
}
builder.Services.AddSingleton<IOpportunityPublicationGate, OpportunityPublicationGate>();
builder.Services.AddSingleton<ScannerState>();
builder.Services.AddSingleton<PreTradeReadinessTracker>();
builder.Services.AddSingleton<FeatureCalculator>();
builder.Services.AddSingleton<PriorSessionContextBuilder>();
builder.Services.AddSingleton<PriorSessionHistoricalReconstructor>();
builder.Services.AddSingleton<MomentumContinuationDetector>();
builder.Services.AddSingleton<MarketReactionEvaluator>();
builder.Services.AddScoped<IMarketReactionStore, EfMarketReactionStore>();
builder.Services.AddScoped<MarketReactionTracker>();
var marketDataProvider = builder.Configuration["MarketData:Provider"] ?? "Fixture";
var fixturePath = Path.Combine(builder.Environment.ContentRootPath, "Fixtures", "momentum-continuation.json");
if (string.Equals(marketDataProvider, "Fixture", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IMarketSessionCalendar, FixtureMarketSessionCalendar>();
    builder.Services.AddScoped<IHistoricalMarketDataSource>(_ =>
        new FixtureHistoricalMarketDataSource(fixturePath));
}
else if (string.Equals(marketDataProvider, "Alpaca", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton(_ =>
        AlpacaHistoricalMarketDataOptions.FromConfiguration(builder.Configuration));
    builder.Services.AddScoped<IMarketSessionCalendar>(serviceProvider =>
        serviceProvider.GetRequiredService<AlpacaMarketSessionCalendar>());
    builder.Services.AddScoped<IHistoricalMarketDataSource>(serviceProvider =>
        serviceProvider.GetRequiredService<AlpacaHistoricalMarketDataSource>());
}
builder.Services.AddScoped<IPriorSessionContextProvider>(serviceProvider =>
    new PriorSessionContextProvider(
        serviceProvider.GetService<IMarketSessionCalendar>(),
        serviceProvider.GetService<IHistoricalMarketDataSource>(),
        serviceProvider.GetRequiredService<PriorSessionContextBuilder>(),
        serviceProvider.GetRequiredService<ScannerConfiguration>(),
        serviceProvider.GetRequiredService<PriorSessionHistoricalReconstructor>(),
        serviceProvider.GetRequiredService<PriorSessionWarmupOptions>(),
        serviceProvider.GetRequiredService<TimeProvider>(),
        serviceProvider.GetRequiredService<ILogger<PriorSessionContextProvider>>()));
builder.Services.AddScoped<ScannerPipeline>();
builder.Services.AddScoped<IMarketDataSource>(serviceProvider =>
    MarketDataSourceFactory.Create(
        marketDataProvider,
        builder.Configuration,
        fixturePath,
        serviceProvider.GetRequiredService<TimeProvider>(),
        serviceProvider.GetService<LiveValidationDiagnostics>()));
builder.Services.AddScoped<IScannerStore, EfScannerStore>();
builder.Services.AddScoped<IPreTradeReadinessStore, EfPreTradeReadinessStore>();
builder.Services.AddScoped<EfMarketSituationStore>();
builder.Services.AddScoped<IMarketSituationStore>(serviceProvider =>
    serviceProvider.GetRequiredService<EfMarketSituationStore>());
builder.Services.AddScoped<IMarketSituationEvidenceStore>(serviceProvider =>
    serviceProvider.GetRequiredService<EfMarketSituationStore>());

var connectionString = builder.Configuration.GetConnectionString("MarketOpportunity")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:MarketOpportunity must be configured for SQL Server persistence.");
builder.Services.AddDbContext<MarketOpportunityDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));
builder.Services.AddHostedService<ScannerRunnerService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<MarketOpportunityDbContext>();
    if (!await dbContext.Database.EnsureCreatedAsync())
    {
        await dbContext.Database.MigrateAsync();
    }
}

app.MapHealthChecks("/health");
app.MapGet("/api/v1/system/status", (ScannerState scanner) => Results.Ok(scanner.Read().Status));
if (app.Environment.IsDevelopment() && diagnosticEnabled)
{
    app.MapGet("/api/v1/system/diagnostics", (LiveValidationDiagnostics diagnostics) =>
        TypedResults.Ok(diagnostics.Snapshot()))
        .WithName("GetLiveValidationDiagnostics")
        .WithSummary("Read bounded, opt-in live validation diagnostics.")
        .WithDescription("Development-only sidecar metrics and sanitized provider metadata; does not change scanner results.");
}
app.MapGet("/api/v1/opportunities", (ScannerState scanner) =>
    Results.Ok(scanner.Read().Results
        .Select(result => new OpportunityPublicationReadModel(
            result.Opportunity,
            result.Publication,
            result.TradePlan,
            PreTradeReadinessReadModel.From(result),
            result.MarketSituation,
            result.MarketReactions,
            result.PriorSessionWarmup))
        .OrderByDescending(result => result.Opportunity.AsOfUtc)
        .ThenBy(result => result.Opportunity.Symbol, StringComparer.Ordinal)
        .ThenBy(result => result.Opportunity.SessionId, StringComparer.Ordinal)
        .ToArray()));
app.MapGet("/api/v1/opportunities/{id:guid}", (Guid id, ScannerState scanner) =>
{
    var result = scanner.Read().Results
        .SingleOrDefault(item => item.Opportunity.Id == id);
    return result is null
        ? Results.NotFound()
        : Results.Ok(new OpportunityPublicationReadModel(
            result.Opportunity,
            result.Publication,
            result.TradePlan,
            PreTradeReadinessReadModel.From(result),
            result.MarketSituation,
            result.MarketReactions,
            result.PriorSessionWarmup));
});
app.MapGet("/api/v1/reactions", (string? symbol, string? sessionId, ScannerState scanner) =>
    Results.Ok(scanner.Read().Results
        .Where(result => symbol is null
            || result.Features.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
        .Where(result => sessionId is null
            || result.Features.SessionId.Equals(sessionId, StringComparison.Ordinal))
        .SelectMany(result => result.MarketReactions ?? [])
        .OrderByDescending(reaction => reaction.Assessment.AsOfUtc)
        .ThenBy(reaction => reaction.Assessment.Symbol, StringComparer.Ordinal)
        .ThenBy(reaction => reaction.Assessment.SessionId, StringComparer.Ordinal)
        .ToArray()));
app.MapGet(
    "/api/v1/market-situations/{assessmentId:guid}/candidates",
    async (
        Guid assessmentId,
        int? afterOrdinal,
        int? pageSize,
        IMarketSituationEvidenceStore store,
        MarketSituationEvidenceReadOptions options,
        CancellationToken cancellationToken) =>
    {
        var cursor = afterOrdinal ?? -1;
        if (cursor < -1)
        {
            return Results.Problem(
                title: "Invalid candidate cursor",
                detail: "afterOrdinal must be -1 or a non-negative canonical member ordinal.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var size = pageSize ?? options.DefaultPageSize;
        if (size < 1 || size > options.MaximumPageSize)
        {
            return Results.Problem(
                title: "Invalid candidate page size",
                detail:
                    $"pageSize must be between 1 and {options.MaximumPageSize}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        MarketSituationCandidatePageReadModel? page;
        try
        {
            page = await store.ReadCandidatePageAsync(
                assessmentId,
                cursor,
                size,
                cancellationToken);
        }
        catch (MarketSituationEvidenceReadException exception)
            when (exception.Failure ==
                MarketSituationEvidenceReadFailure.LegacyCandidateEvidenceUnavailable)
        {
            return Results.Problem(
                title: "Legacy candidate evidence unavailable",
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "LegacyCandidateEvidenceUnavailable"
                });
        }
        catch (MarketSituationEvidenceReadException exception)
            when (exception.Failure ==
                MarketSituationEvidenceReadFailure.CandidateSetIncomplete)
        {
            return Results.Problem(
                title: "Candidate evidence incomplete",
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "CandidateSetIncomplete"
                });
        }

        if (page is null)
        {
            return Results.NotFound();
        }

        var serializedBytes = JsonSerializer.SerializeToUtf8Bytes(
            page,
            evidenceJsonOptions).Length;
        return serializedBytes > options.MaximumSerializedPageBytes
            ? Results.Problem(
                title: "Candidate evidence page too large",
                detail:
                    $"The serialized page is {serializedBytes} bytes; the operational limit is " +
                    $"{options.MaximumSerializedPageBytes} bytes.",
                statusCode: StatusCodes.Status413PayloadTooLarge,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "CandidateEvidencePageTooLarge"
                })
            : Results.Ok(page);
    })
    .WithName("GetMarketSituationCandidateEvidence")
    .WithSummary("Read bounded paged candidate evidence for a MarketSituation assessment.")
    .WithDescription(
        "Read-only diagnostic evidence in deterministic canonical ordinal order. " +
        "Repeated paging can return the full immutable candidate set.");
app.MapGet("/api/v1/symbols/{symbol}/snapshot", (string symbol, string? sessionId, ScannerState scanner) =>
{
    var snapshots = scanner.Read().Results
        .Where(result => result.Features.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
        .Where(result => sessionId is null
            || result.Features.SessionId.Equals(sessionId, StringComparison.Ordinal))
        .OrderByDescending(result => result.Features.AsOfUtc);
    var result = snapshots.FirstOrDefault();
    return result is null ? Results.NotFound() : Results.Ok(result.Features);
});
app.MapGet("/api/v1/symbols/{symbol}/prior-context", (string symbol, string? sessionId, ScannerState scanner) =>
{
    var scannerState = scanner.Read();
    var results = scannerState.Results
        .Where(result => result.Features.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
        .Where(result => sessionId is null
            || result.Features.SessionId.Equals(sessionId, StringComparison.Ordinal))
        .OrderByDescending(result => result.Features.AsOfUtc);
    var result = results.FirstOrDefault();
    if (result is not null && result.PriorSessionWarmup is { } resultWarmup)
    {
        return Results.Ok(PriorSessionContextReadModel.From(
            result.PriorSessionContext, resultWarmup));
    }

    var warmup = (scannerState.PriorSessionWarmups ?? [])
        .Where(item => item.Scope.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
        .Where(item => sessionId is null
            || item.Scope.CurrentSessionId.Equals(sessionId, StringComparison.Ordinal))
        .LastOrDefault();
    return warmup is null
        ? Results.NotFound()
        : Results.Ok(PriorSessionContextReadModel.From(null, warmup));
});
app.MapGet("/api/v1/configuration/effective", (ScannerConfiguration configuration) =>
    Results.Ok(configuration));

await app.RunAsync();

public partial class Program { }
