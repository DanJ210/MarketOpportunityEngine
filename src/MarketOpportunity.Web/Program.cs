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
    .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation().AddRuntimeInstrumentation());
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
builder.Services.AddSingleton(TimeProvider.System);
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
        serviceProvider.GetRequiredService<TimeProvider>()));
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
