using ElevateX.Core.Data;
using ElevateX.Core.Models;
using ElevateX.Core.Services;
using ElevateX.Portal.Components;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

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

// In-process real-time push, riding Blazor Server's own SignalR circuit (FR-12)
builder.Services.AddSingleton<IScanNotifier, ScanNotifier>();

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
