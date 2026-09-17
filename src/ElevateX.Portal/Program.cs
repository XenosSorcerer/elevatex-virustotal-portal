using ElevateX.Core.Data;
using ElevateX.Core.Models;
using ElevateX.Core.Services;
using ElevateX.Portal.Components;
using ElevateX.Portal.Hubs;
using ElevateX.Portal.Logging;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// Structured logging (Serilog) + in-app error notification feed (FR-12). The error feed is a plain
// ILoggerProvider, not a Serilog sink, so it keeps working regardless of the logging backend;
// writeToProviders:true is required for it (and the console) to actually receive log calls once
// Serilog owns the ILoggerFactory.
var errorFeed = new ErrorFeedService();
builder.Services.AddSingleton<IErrorFeedService>(errorFeed);

// Drop the default console/debug providers so Serilog's own sinks below are the only console
// output; the error feed remains as the sole Microsoft.Extensions.Logging provider, still reached
// via writeToProviders below.
builder.Logging.ClearProviders();
builder.Logging.AddProvider(new ErrorFeedLoggerProvider(errorFeed));

builder.Host.UseSerilog((_, configuration) => configuration
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/elevatex-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14),
    writeToProviders: true);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure SQLite DbContext with connection string from appsettings.json (FR-02)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=elevatex_portal.db";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));

// VirusTotal options
builder.Services.Configure<VirusTotalOptions>(
    builder.Configuration.GetSection(VirusTotalOptions.SectionName));

// Shared clock + outbound-call pacing and quota accounting (FR-05, FR-11)
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IApiRateLimiter, ApiRateLimiter>();
builder.Services.AddSingleton<IApiCallRecorder, ApiCallRecorder>();
builder.Services.AddTransient<VirusTotalRateLimitHandler>();

// In-process real-time push, riding Blazor Server's own SignalR circuit (FR-09)
builder.Services.AddSingleton<IScanNotifier, ScanNotifier>();

// Dedicated SignalR hub (FR-12 stand-out) — a second, independent broadcast channel on top of
// the Blazor-circuit push above, so a client that never opens a Blazor circuit still gets live
// scan-status events. See ScanHubBroadcaster and DECISIONS.md.
builder.Services.AddSignalR();
builder.Services.AddHostedService<ScanHubBroadcaster>();

// Typed VirusTotal client; every outbound call is paced + logged by the handler
builder.Services.AddHttpClient<IVirusTotalClient, VirusTotalClient>()
    .AddHttpMessageHandler<VirusTotalRateLimitHandler>();

// Core services
builder.Services.AddScoped<ISubmissionService, SubmissionService>();
builder.Services.AddScoped<IScanPipelineService, ScanPipelineService>();
builder.Services.AddScoped<IQuotaGuard, QuotaGuard>();
builder.Services.AddScoped<IExportService, ExportService>();
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();

// Background scan dispatcher — database-polled outbox (Gate 4 Option B; FR-04, FR-05, FR-06, FR-10)
builder.Services.AddHostedService<ScanDispatcherBackgroundService>();

var app = builder.Build();

// Auto-provision SQLite database and enable WAL mode on first run (FR-02)
await DatabaseInitializer.InitializeDatabaseAsync(app.Services);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// FR-12: dedicated SignalR hub for scan-status broadcasts (see ScanHubBroadcaster)
app.MapHub<ScanHub>(ScanHub.Route);

// FR-12: .xlsx export of submission data
app.MapGet("/export/submissions.xlsx", async (IExportService export, CancellationToken ct) =>
{
    var bytes = await export.BuildSubmissionsXlsxAsync(ct);
    return Results.File(
        bytes,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        $"submissions-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xlsx");
});

app.Run();

// Exposed for WebApplicationFactory in the integration test project (Phase 7)
public partial class Program { }
