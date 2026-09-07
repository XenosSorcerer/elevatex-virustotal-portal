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

// Configure VirusTotal Options and HTTP Client
builder.Services.Configure<VirusTotalOptions>(
    builder.Configuration.GetSection(VirusTotalOptions.SectionName));

builder.Services.AddHttpClient<IVirusTotalClient, VirusTotalClient>();

// Core Services
builder.Services.AddSingleton<IScanQueue, InMemoryScanQueue>();
builder.Services.AddScoped<ISubmissionService, SubmissionService>();
builder.Services.AddScoped<IScanPipelineService, ScanPipelineService>();

// Background Worker Service (FR-04, FR-05, FR-06, FR-10 - Option B Database-Polled Outbox)
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

app.Run();
