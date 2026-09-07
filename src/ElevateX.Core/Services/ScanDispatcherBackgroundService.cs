using ElevateX.Core.Data;
using ElevateX.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace ElevateX.Core.Services;

public class ScanDispatcherBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ScanDispatcherBackgroundService> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;

    // Strict 15-second delay to guarantee never exceeding 4 requests per minute (FR-05)
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromSeconds(15.0);
    private DateTime _lastApiCallTimeUtc = DateTime.MinValue;

    public ScanDispatcherBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<ScanDispatcherBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        // Bounded retry policy with exponential backoff on transient HTTP failures (FR-06)
        _retryPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) + TimeSpan.FromMilliseconds(Random.Shared.Next(100, 500)),
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning(exception, "Transient scan failure. Retry {RetryCount}/3 in {DelayMs}ms.", retryCount, timeSpan.TotalMilliseconds);
                });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Database-Polled Scan Dispatcher Background Service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessNextPendingScanAsync(stoppingToken);

                if (!processed)
                {
                    // No pending jobs: sleep briefly before polling DB again
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
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

    private async Task<bool> ProcessNextPendingScanAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pipeline = scope.ServiceProvider.GetRequiredService<IScanPipelineService>();

        // Query database for next queued or unfinished in-progress job (FR-10 Restart Resilience)
        var pendingAnalysis = await db.FileAnalyses
            .Where(a => a.Status == AnalysisStatus.Queued || 
                       (a.Status == AnalysisStatus.InProgress && a.CurrentStage == AnalysisStage.PollingAnalysis))
            .OrderBy(a => a.FirstScannedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (pendingAnalysis == null)
        {
            return false;
        }

        // Rate-Limit Compliance (FR-05): Enforce >= 15 seconds since last outbound call
        var elapsed = DateTime.UtcNow - _lastApiCallTimeUtc;
        if (elapsed < MinRequestInterval)
        {
            var waitTime = MinRequestInterval - elapsed;
            _logger.LogDebug("Rate limiter pacing: waiting {WaitMs}ms before executing next API request.", waitTime.TotalMilliseconds);
            await Task.Delay(waitTime, cancellationToken);
        }

        _lastApiCallTimeUtc = DateTime.UtcNow;
        _logger.LogInformation("Dispatching scan for FileAnalysis {Id} ({Sha256}). Stage: {Stage}", 
            pendingAnalysis.Id, pendingAnalysis.Sha256, pendingAnalysis.CurrentStage);

        try
        {
            // Execute scan pipeline with bounded Polly retries (FR-06)
            await _retryPolicy.ExecuteAsync(async (ct) =>
            {
                await pipeline.ProcessScanAsync(pendingAnalysis.Id, ct);
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scan failed after bounded retries for FileAnalysis {Id} ({Sha256}). Transitioning to Failed.", 
                pendingAnalysis.Id, pendingAnalysis.Sha256);

            // Fetch fresh tracking instance to safely persist failure state (FR-06)
            var failedEntry = await db.FileAnalyses.FirstOrDefaultAsync(a => a.Id == pendingAnalysis.Id, cancellationToken);
            if (failedEntry != null)
            {
                failedEntry.Status = AnalysisStatus.Failed;
                failedEntry.CurrentStage = AnalysisStage.Failed;
                failedEntry.FailureReason = ex.Message;
                failedEntry.RetryCount += 3;
                failedEntry.LastScannedAtUtc = DateTime.UtcNow;

                await db.SaveChangesAsync(cancellationToken);
            }
        }

        return true;
    }
}
