using System.Net;
using ElevateX.Core.Data;
using ElevateX.Core.Entities;
using ElevateX.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace ElevateX.Core.Services;

/// <summary>
/// Database-polled outbox worker (Gate 4 Option B). The database is the only queue:
/// submissions land as <see cref="AnalysisStatus.Queued"/> and this loop drains them
/// one at a time. Outbound pacing lives in <see cref="VirusTotalRateLimitHandler"/>,
/// not here, so every individual API call is rate-limited — not just every job.
/// </summary>
public class ScanDispatcherBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ScanDispatcherBackgroundService> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly int _maxRetries;
    private readonly int _dailyCap;

    // Set by the retry policy's onRetry callback; read by the failure handler. Safe as a
    // field because the dispatcher processes exactly one job at a time.
    private int _attemptsMade;

    private static readonly HttpStatusCode[] TransientStatusCodes =
    {
        HttpStatusCode.RequestTimeout,       // 408
        HttpStatusCode.TooManyRequests,      // 429
        HttpStatusCode.InternalServerError,  // 500
        HttpStatusCode.BadGateway,           // 502
        HttpStatusCode.ServiceUnavailable,   // 503
        HttpStatusCode.GatewayTimeout        // 504
    };

    public ScanDispatcherBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<ScanDispatcherBackgroundService> logger,
        IOptions<VirusTotalOptions> options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _maxRetries = Math.Max(0, options.Value.MaxTransientRetries);
        _dailyCap = options.Value.DailyRequestCap;

        // Bounded retry with exponential backoff on genuinely transient failures only
        // (429 / 408 / 5xx / timeout / socket). Non-transient responses (400/401/...)
        // are NOT retried — they fail fast into a visible Failed state (FR-06).
        _retryPolicy = Policy
            .Handle<VirusTotalApiException>(ex => IsTransient(ex.StatusCode))
            .Or<HttpRequestException>(ex => IsTransient(ex.StatusCode))
            .Or<TimeoutException>()
            .Or<TaskCanceledException>(ex => ex.InnerException is TimeoutException)
            .WaitAndRetryAsync(
                retryCount: _maxRetries,
                sleepDurationProvider: (attempt, exception, _) =>
                {
                    // Honour the server's Retry-After when it gave us one.
                    if (exception is VirusTotalApiException { RetryAfter: { } retryAfter } && retryAfter > TimeSpan.Zero)
                        return retryAfter;

                    return TimeSpan.FromSeconds(Math.Pow(2, attempt))
                         + TimeSpan.FromMilliseconds(Random.Shared.Next(100, 500));
                },
                onRetryAsync: (exception, delay, attempt, _) =>
                {
                    _attemptsMade = attempt;
                    _logger.LogWarning(exception,
                        "Transient scan failure. Retry {Attempt}/{Max} in {DelayMs}ms.",
                        attempt, _maxRetries, delay.TotalMilliseconds);
                    return Task.CompletedTask;
                });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Database-Polled Scan Dispatcher Background Service started.");

        try
        {
            await ReclaimOrphanedJobsAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reclaim orphaned in-progress scans on startup.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await ProcessNextPendingScanAsync(stoppingToken);

                var idleDelay = result switch
                {
                    DispatchResult.Processed => TimeSpan.Zero,
                    DispatchResult.QuotaExhausted => TimeSpan.FromMinutes(1),
                    _ => TimeSpan.FromSeconds(2)
                };

                if (idleDelay > TimeSpan.Zero)
                    await Task.Delay(idleDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in background scan dispatcher loop.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.LogInformation("Database-Polled Scan Dispatcher Background Service stopped.");
    }

    /// <summary>
    /// FR-10: after a restart, any scan left <see cref="AnalysisStatus.InProgress"/> was
    /// interrupted mid-flight. Rewind it to a safe re-entry point so the poll loop resumes it.
    /// </summary>
    private async Task ReclaimOrphanedJobsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var orphaned = await db.FileAnalyses
            .Where(a => a.Status == AnalysisStatus.InProgress)
            .ToListAsync(cancellationToken);

        if (orphaned.Count == 0)
            return;

        foreach (var analysis in orphaned)
        {
            if (!string.IsNullOrWhiteSpace(analysis.AnalysisId))
            {
                // Upload had already succeeded — resume polling that analysis.
                analysis.Status = AnalysisStatus.InProgress;
                analysis.CurrentStage = AnalysisStage.PollingAnalysis;
            }
            else
            {
                // Interrupted mid hash-lookup or mid-upload — restart from the top.
                // Hash lookup is idempotent; a re-upload may create a second VirusTotal
                // analysis for the file (bounded, acceptable — see DECISIONS.md).
                analysis.Status = AnalysisStatus.Queued;
                analysis.CurrentStage = AnalysisStage.Queued;
            }

            analysis.LastScannedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        _logger.LogWarning("Reclaimed {Count} orphaned in-progress scan(s) after restart.", orphaned.Count);
    }

    private async Task<DispatchResult> ProcessNextPendingScanAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pipeline = scope.ServiceProvider.GetRequiredService<IScanPipelineService>();
        var quotaGuard = scope.ServiceProvider.GetRequiredService<IQuotaGuard>();

        // FR-05 / brief §2: stop spending once the daily cap is reached; jobs stay Queued.
        if (!await quotaGuard.HasDailyQuotaAsync(cancellationToken))
        {
            _logger.LogWarning(
                "Daily VirusTotal request cap ({Cap}) reached. Dispatch paused until 00:00 UTC.", _dailyCap);
            return DispatchResult.QuotaExhausted;
        }

        // Next queued job, or one legitimately parked mid-poll during normal operation.
        var pendingAnalysis = await db.FileAnalyses
            .Where(a => a.Status == AnalysisStatus.Queued ||
                        (a.Status == AnalysisStatus.InProgress && a.CurrentStage == AnalysisStage.PollingAnalysis))
            .OrderBy(a => a.FirstScannedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (pendingAnalysis == null)
            return DispatchResult.Idle;

        _logger.LogInformation("Dispatching scan for FileAnalysis {Id} ({Sha256}). Stage: {Stage}",
            pendingAnalysis.Id, pendingAnalysis.Sha256, pendingAnalysis.CurrentStage);

        _attemptsMade = 0;

        try
        {
            await _retryPolicy.ExecuteAsync(ct => pipeline.ProcessScanAsync(pendingAnalysis.Id, ct), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Scan failed after bounded retries for FileAnalysis {Id} ({Sha256}). Transitioning to Failed.",
                pendingAnalysis.Id, pendingAnalysis.Sha256);

            // pendingAnalysis is tracked by this same context and reflects the pipeline's
            // partial mutations — write the terminal Failed state onto it directly (FR-06).
            pendingAnalysis.Status = AnalysisStatus.Failed;
            pendingAnalysis.CurrentStage = AnalysisStage.Failed;
            pendingAnalysis.FailureReason = ex.Message;
            pendingAnalysis.RetryCount += _attemptsMade;
            pendingAnalysis.LastScannedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync(cancellationToken);
        }

        return DispatchResult.Processed;
    }

    internal static bool IsTransient(HttpStatusCode? status) =>
        status is null || Array.IndexOf(TransientStatusCodes, status.Value) >= 0;

    private enum DispatchResult
    {
        Processed,
        Idle,
        QuotaExhausted
    }
}
