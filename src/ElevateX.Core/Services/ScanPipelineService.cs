using ElevateX.Core.Data;
using ElevateX.Core.Entities;
using ElevateX.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ElevateX.Core.Services;

public class ScanPipelineService : IScanPipelineService
{
    private readonly AppDbContext _db;
    private readonly IVirusTotalClient _vtClient;
    private readonly ILogger<ScanPipelineService> _logger;
    private readonly int _maxPollAttempts;
    private const long MaxStandardUploadBytes = 32 * 1024 * 1024; // 32 MB limit (FR-12)

    public ScanPipelineService(
        AppDbContext db,
        IVirusTotalClient vtClient,
        ILogger<ScanPipelineService> logger,
        IOptions<VirusTotalOptions> options)
    {
        _db = db;
        _vtClient = vtClient;
        _logger = logger;
        _maxPollAttempts = Math.Max(1, options.Value.MaxPollAttempts);
    }

    public async Task ProcessScanAsync(Guid fileAnalysisId, CancellationToken cancellationToken = default)
    {
        var analysis = await _db.FileAnalyses
            .FirstOrDefaultAsync(a => a.Id == fileAnalysisId, cancellationToken);

        if (analysis == null)
        {
            _logger.LogWarning("FileAnalysis {Id} not found in database.", fileAnalysisId);
            return;
        }

        if (analysis.Status == AnalysisStatus.Completed)
        {
            _logger.LogInformation("FileAnalysis {Id} ({Sha256}) is already completed.", fileAnalysisId, analysis.Sha256);
            return;
        }

        analysis.Status = AnalysisStatus.InProgress;
        await _db.SaveChangesAsync(cancellationToken);

        // Stage 1: Remote Hash Lookup (GET /files/{hash}) - 1 request
        if (analysis.CurrentStage == AnalysisStage.Queued || analysis.CurrentStage == AnalysisStage.CheckingHash)
        {
            analysis.CurrentStage = AnalysisStage.CheckingHash;
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Step 1: Checking VirusTotal hash cache for {Sha256}", analysis.Sha256);
            var report = await _vtClient.GetFileReportAsync(analysis.Sha256, cancellationToken);

            if (report != null)
            {
                // Found existing report on VirusTotal! Resolved in exactly 1 API request.
                _logger.LogInformation("Hash {Sha256} found on VirusTotal. Threat score: {Malicious}/{Total}", 
                    analysis.Sha256, report.MaliciousCount, report.TotalEngines);

                analysis.Md5 = report.Md5 ?? analysis.Md5;
                analysis.Sha1 = report.Sha1 ?? analysis.Sha1;
                analysis.MaliciousCount = report.MaliciousCount;
                analysis.SuspiciousCount = report.SuspiciousCount;
                analysis.UndetectedCount = report.UndetectedCount;
                analysis.HarmlessCount = report.HarmlessCount;
                analysis.TotalEngines = report.TotalEngines;
                analysis.ScanSummary = report.ScanSummary;
                analysis.VirusTotalReportUrl = report.ReportUrl;
                analysis.Status = AnalysisStatus.Completed;
                analysis.CurrentStage = AnalysisStage.Done;
                analysis.LastScannedAtUtc = DateTime.UtcNow;

                await _db.SaveChangesAsync(cancellationToken);
                return;
            }

            _logger.LogInformation("Hash {Sha256} not known to VirusTotal. Transitioning to upload stage.", analysis.Sha256);
            analysis.CurrentStage = AnalysisStage.Uploading;
            await _db.SaveChangesAsync(cancellationToken);
        }

        // Stage 2: File Upload (POST /files) - 1 request
        if (analysis.CurrentStage == AnalysisStage.Uploading)
        {
            if (analysis.FileSizeBytes > MaxStandardUploadBytes)
            {
                _logger.LogInformation("File {Sha256} exceeds 32 MB upload limit. Finalizing with lookup summary.", analysis.Sha256);
                analysis.Status = AnalysisStatus.Completed;
                analysis.CurrentStage = AnalysisStage.Done;
                analysis.ScanSummary = "File not found in VirusTotal database (Upload omitted: exceeds 32 MB limit)";
                analysis.VirusTotalReportUrl = $"https://www.virustotal.com/gui/file/{analysis.Sha256}";
                analysis.LastScannedAtUtc = DateTime.UtcNow;

                await _db.SaveChangesAsync(cancellationToken);
                return;
            }

            var filePath = Path.Combine(AppContext.BaseDirectory, "uploads", analysis.Sha256);
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Binary file for analysis {analysis.Sha256} not found at {filePath}.");
            }

            using var fileStream = File.OpenRead(filePath);
            var analysisId = await _vtClient.UploadFileAsync(fileStream, analysis.FileName, cancellationToken);

            analysis.AnalysisId = analysisId;
            analysis.CurrentStage = AnalysisStage.PollingAnalysis;
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("File {Sha256} uploaded. AnalysisId: {AnalysisId}. Transitioned to polling.", analysis.Sha256, analysisId);
        }

        // Stage 3: Polling Analysis Status (GET /analyses/{id}) - 1 request per poll
        if (analysis.CurrentStage == AnalysisStage.PollingAnalysis && !string.IsNullOrWhiteSpace(analysis.AnalysisId))
        {
            var analysisReport = await _vtClient.GetAnalysisStatusAsync(analysis.AnalysisId, cancellationToken);

            if (analysisReport != null && analysisReport.Status.Equals("completed", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Analysis {AnalysisId} for {Sha256} completed. Threat score: {Malicious}/{Total}", 
                    analysis.AnalysisId, analysis.Sha256, analysisReport.MaliciousCount, analysisReport.TotalEngines);

                analysis.MaliciousCount = analysisReport.MaliciousCount;
                analysis.SuspiciousCount = analysisReport.SuspiciousCount;
                analysis.UndetectedCount = analysisReport.UndetectedCount;
                analysis.HarmlessCount = analysisReport.HarmlessCount;
                analysis.TotalEngines = analysisReport.TotalEngines;
                analysis.ScanSummary = $"{analysis.MaliciousCount}/{analysis.TotalEngines} security vendors flagged this file as malicious";
                analysis.VirusTotalReportUrl = $"https://www.virustotal.com/gui/file/{analysis.Sha256}";
                analysis.Status = AnalysisStatus.Completed;
                analysis.CurrentStage = AnalysisStage.Done;
                analysis.LastScannedAtUtc = DateTime.UtcNow;

                await _db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                analysis.PollCount++;
                analysis.LastScannedAtUtc = DateTime.UtcNow;

                if (analysis.PollCount > _maxPollAttempts)
                {
                    analysis.Status = AnalysisStatus.Failed;
                    analysis.CurrentStage = AnalysisStage.Failed;
                    analysis.FailureReason =
                        $"VirusTotal analysis did not complete after {_maxPollAttempts} polls.";
                    _logger.LogWarning(
                        "Analysis {AnalysisId} for {Sha256} abandoned after {Polls} polls.",
                        analysis.AnalysisId, analysis.Sha256, analysis.PollCount);
                }
                else
                {
                    _logger.LogInformation(
                        "Analysis {AnalysisId} still in progress on VirusTotal ({Status}). Poll {Poll}/{Max}.",
                        analysis.AnalysisId, analysisReport?.Status ?? "queued", analysis.PollCount, _maxPollAttempts);
                    // Left in PollingAnalysis stage for subsequent poll pass
                }

                await _db.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
