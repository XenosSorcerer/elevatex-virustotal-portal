using ElevateX.Core.Data;
using ElevateX.Core.Entities;
using ElevateX.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ElevateX.Core.Services;

public interface IAnalyticsService
{
    Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken ct = default);
}

public sealed record DashboardSnapshot(
    KpiSummary Kpis,
    IReadOnlyList<DailyCount> Volume,
    IReadOnlyList<StatusCount> StatusBreakdown,
    IReadOnlyList<SourceCount> TopSources,
    DetectionSummary Detections,
    QuotaUsage Quota);

public sealed record KpiSummary(
    int TotalSubmissions, int DistinctFiles, int Completed, int Failed, int InFlight, double DedupRatePct);

public sealed record DailyCount(DateOnly Day, int Count);
public sealed record StatusCount(string Status, int Count);
public sealed record SourceCount(string Source, int Total, int Flagged);
public sealed record DetectionSummary(int Completed, int Flagged, int Clean, int Sev1to3, int Sev4to10, int Sev10Plus);
public sealed record QuotaUsage(int UsedToday, int DailyCap, int UsedLastMinute, int PerMinuteCap);

/// <summary>
/// FR-11: read-only aggregation for the dashboard. Prototype scale — pulls light
/// projections and aggregates in memory; at 10x this should move to SQL GROUP BY
/// plus a materialised daily rollup.
/// </summary>
public sealed class AnalyticsService : IAnalyticsService
{
    public const int VolumeWindowDays = 14;

    private readonly AppDbContext _db;
    private readonly TimeProvider _time;
    private readonly VirusTotalOptions _options;

    public AnalyticsService(AppDbContext db, TimeProvider time, IOptions<VirusTotalOptions> options)
    {
        _db = db;
        _time = time;
        _options = options.Value;
    }

    public async Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var nowUtc = _time.GetUtcNow().UtcDateTime;
        var windowStart = nowUtc.Date.AddDays(-(VolumeWindowDays - 1));

        var subs = await _db.Submissions
            .Select(s => new
            {
                s.Source,
                s.SubmittedAtUtc,
                Status = s.FileAnalysis!.Status,
                Malicious = s.FileAnalysis.MaliciousCount
            })
            .ToListAsync(ct);

        var files = await _db.FileAnalyses
            .Select(f => new { f.Status, f.MaliciousCount })
            .ToListAsync(ct);

        var usedToday = await _db.ApiCalls.CountAsync(c => c.OccurredAtUtc >= nowUtc.Date, ct);
        var usedLastMinute = await _db.ApiCalls.CountAsync(c => c.OccurredAtUtc >= nowUtc.AddMinutes(-1), ct);

        var total = subs.Count;
        var distinct = files.Count;
        var completed = files.Count(f => f.Status == AnalysisStatus.Completed);
        var failed = files.Count(f => f.Status == AnalysisStatus.Failed);
        var inFlight = files.Count(f => f.Status is AnalysisStatus.Queued or AnalysisStatus.InProgress);
        var dedupPct = total == 0 ? 0 : Math.Round(100.0 * (total - distinct) / total, 1);

        var byDay = subs
            .Where(s => s.SubmittedAtUtc >= windowStart)
            .GroupBy(s => DateOnly.FromDateTime(s.SubmittedAtUtc.Date))
            .ToDictionary(g => g.Key, g => g.Count());

        var volume = Enumerable.Range(0, VolumeWindowDays)
            .Select(i => DateOnly.FromDateTime(windowStart.AddDays(i)))
            .Select(d => new DailyCount(d, byDay.GetValueOrDefault(d)))
            .ToList();

        var statusBreakdown = new[]
            {
                AnalysisStatus.Queued, AnalysisStatus.InProgress,
                AnalysisStatus.Completed, AnalysisStatus.Failed
            }
            .Select(st => new StatusCount(st.ToString(), files.Count(f => f.Status == st)))
            .ToList();

        var topSources = subs
            .GroupBy(s => s.Source)
            .Select(g => new SourceCount(
                g.Key.ToString(),
                g.Count(),
                g.Count(s => s.Status == AnalysisStatus.Completed && s.Malicious > 0)))
            .OrderByDescending(s => s.Total)
            .Take(5)
            .ToList();

        var completedMalicious = files
            .Where(f => f.Status == AnalysisStatus.Completed)
            .Select(f => f.MaliciousCount)
            .ToList();

        var detections = new DetectionSummary(
            Completed: completedMalicious.Count,
            Flagged: completedMalicious.Count(m => m > 0),
            Clean: completedMalicious.Count(m => m == 0),
            Sev1to3: completedMalicious.Count(m => m is >= 1 and <= 3),
            Sev4to10: completedMalicious.Count(m => m is >= 4 and <= 10),
            Sev10Plus: completedMalicious.Count(m => m > 10));

        return new DashboardSnapshot(
            new KpiSummary(total, distinct, completed, failed, inFlight, dedupPct),
            volume,
            statusBreakdown,
            topSources,
            detections,
            new QuotaUsage(usedToday, _options.DailyRequestCap, usedLastMinute, _options.RateLimitRequestsPerMinute));
    }
}
