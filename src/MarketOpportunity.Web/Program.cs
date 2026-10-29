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
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = livePipelineOptions.GracefulShutdownTimeout + TimeSpan.FromSeconds(10));
builder.Services.AddSingleton<Action<string, string>>(serviceProvider =>
{
    var logger = serviceProvider.GetRequiredService<ILogger<ScannerPipeline>>();
    return (provider, exceptionType) => logger.LogWarning(
        "Market-data source {Source} disconnected ({ExceptionType}); retrying after the configured delay.",
        provider,
        exceptionType);
});
builder.Services.AddSingleton(TimeProvider.System);
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
        var diagnostics = new LiveValidationDiagnostics(serviceProvider.GetRequiredService<TimeProvider>(),
            diagnostic => logger.LogInformation("Live validation transition/anomaly {Diagnostic}", diagnostic));
        diagnostics.ConfigureRun(duration, applicationLogPath);
        return diagnostics;
    });
    // Registered before the scanner so final export follows scanner shutdown.
    builder.Services.AddHostedService<LiveValidationReportService>();
}
builder.Services.AddSingleton<IOpportunityPublicationGate, OpportunityPublicationGate>();
builder.Services.AddSingleton<ScannerState>();
builder.Services.AddSingleton<FeatureCalculator>();
builder.Services.AddSingleton<MomentumContinuationDetector>();
builder.Services.AddScoped<ScannerPipeline>();
var marketDataProvider = builder.Configuration["MarketData:Provider"] ?? "Fixture";
builder.Services.AddScoped<IMarketDataSource>(serviceProvider =>
    MarketDataSourceFactory.Create(
        marketDataProvider,
        builder.Configuration,
        Path.Combine(builder.Environment.ContentRootPath, "Fixtures", "momentum-continuation.json"),
        serviceProvider.GetRequiredService<TimeProvider>(),
        serviceProvider.GetService<LiveValidationDiagnostics>()));
builder.Services.AddScoped<IScannerStore, EfScannerStore>();

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
            result.Publication))
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
        : Results.Ok(new OpportunityPublicationReadModel(result.Opportunity, result.Publication));
});
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
app.MapGet("/api/v1/configuration/effective", (ScannerConfiguration configuration) =>
    Results.Ok(configuration));

await app.RunAsync();

public partial class Program { }
