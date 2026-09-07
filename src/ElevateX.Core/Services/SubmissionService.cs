using System.Security.Cryptography;
using ElevateX.Core.Data;
using ElevateX.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ElevateX.Core.Services;

public class SubmissionService : ISubmissionService
{
    private readonly AppDbContext _db;
    private readonly ILogger<SubmissionService> _logger;
    private const long MaxStandardUploadBytes = 32 * 1024 * 1024; // 32 MB limit (FR-12)

    public SubmissionService(
        AppDbContext db,
        ILogger<SubmissionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<SubmissionResult> SubmitFileAsync(
        Stream fileStream,
        string fileName,
        long fileSize,
        SubmissionSource source,
        string reasonForSuspicion,
        ThreatPriority threatPriority = ThreatPriority.Medium,
        string? targetDepartment = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Calculate Hashes (SHA-256, MD5, SHA-1) locally
        using var sha256 = SHA256.Create();
        using var md5 = MD5.Create();
        using var sha1 = SHA1.Create();

        // Buffer stream to memory or temp file to compute multiple hashes and store
        var memoryStream = new MemoryStream();
        await fileStream.CopyToAsync(memoryStream, cancellationToken);
        var fileBytes = memoryStream.ToArray();

        var sha256Hash = Convert.ToHexString(sha256.ComputeHash(fileBytes)).ToLowerInvariant();
        var md5Hash = Convert.ToHexString(md5.ComputeHash(fileBytes)).ToLowerInvariant();
        var sha1Hash = Convert.ToHexString(sha1.ComputeHash(fileBytes)).ToLowerInvariant();

        _logger.LogInformation("Processing submission for file {FileName} with SHA256: {Sha256}", fileName, sha256Hash);

        // Ensure uploads directory exists to store raw binary for potential upload
        var uploadDir = Path.Combine(AppContext.BaseDirectory, "uploads");
        Directory.CreateDirectory(uploadDir);
        var savedFilePath = Path.Combine(uploadDir, sha256Hash);
        if (!File.Exists(savedFilePath))
        {
            await File.WriteAllBytesAsync(savedFilePath, fileBytes, cancellationToken);
        }

        // 2. Local Deduplication Check (FR-08)
        var existingAnalysis = await _db.FileAnalyses
            .FirstOrDefaultAsync(a => a.Sha256 == sha256Hash, cancellationToken);

        bool isDuplicate = existingAnalysis != null;
        bool requiresScanning = false;
        string? noticeMessage = null;

        if (fileSize > MaxStandardUploadBytes)
        {
            noticeMessage = "File exceeds VirusTotal 32 MB upload limit. System will perform remote hash lookup only.";
        }

        FileAnalysis targetAnalysis;

        if (existingAnalysis != null)
        {
            targetAnalysis = existingAnalysis;
            _logger.LogInformation("File with SHA256 {Sha256} already exists in database with status {Status}.", sha256Hash, existingAnalysis.Status);

            if (existingAnalysis.Status == AnalysisStatus.Completed)
            {
                // Completed before: link directly (0 API quota spent!)
                requiresScanning = false;
                noticeMessage = noticeMessage ?? "Identical file was previously analyzed. Linked to existing scan results with 0 API quota spent.";
            }
            else if (existingAnalysis.Status == AnalysisStatus.InProgress || existingAnalysis.Status == AnalysisStatus.Queued)
            {
                // Currently in flight: attach submission to existing in-flight job
                requiresScanning = false;
                noticeMessage = noticeMessage ?? "Identical file is currently queued or being analyzed. Attached to active scan job.";
            }
            else // Failed previously
            {
                // Reset failed analysis and re-enqueue
                existingAnalysis.Status = AnalysisStatus.Queued;
                existingAnalysis.CurrentStage = AnalysisStage.Queued;
                existingAnalysis.FailureReason = null;
                existingAnalysis.RetryCount = 0;
                requiresScanning = true;
            }
        }
        else
        {
            // Brand new file: create FileAnalysis record
            targetAnalysis = new FileAnalysis
            {
                Id = Guid.NewGuid(),
                Sha256 = sha256Hash,
                Md5 = md5Hash,
                Sha1 = sha1Hash,
                FileName = Path.GetFileName(fileName),
                FileSizeBytes = fileSize,
                Status = AnalysisStatus.Queued,
                CurrentStage = AnalysisStage.Queued,
                FirstScannedAtUtc = DateTime.UtcNow,
                LastScannedAtUtc = DateTime.UtcNow
            };

            _db.FileAnalyses.Add(targetAnalysis);
            requiresScanning = true;
        }

        // 3. Create Submission record linking to the FileAnalysis
        var submission = new Submission
        {
            Id = Guid.NewGuid(),
            FileAnalysisId = targetAnalysis.Id,
            Source = source,
            ReasonForSuspicion = reasonForSuspicion,
            ThreatPriority = threatPriority,
            TargetDepartment = targetDepartment,
            SubmittedAtUtc = DateTime.UtcNow
        };

        _db.Submissions.Add(submission);
        await _db.SaveChangesAsync(cancellationToken);

        // 4. No explicit enqueue: the FileAnalysis row is persisted as Queued and the
        //    database-polled dispatcher (Gate 4 Option B) will pick it up.
        if (requiresScanning)
        {
            _logger.LogInformation("FileAnalysis {AnalysisId} queued for background processing.", targetAnalysis.Id);
        }

        return new SubmissionResult
        {
            Submission = submission,
            FileAnalysis = targetAnalysis,
            IsDuplicate = isDuplicate,
            RequiresScanning = requiresScanning,
            NoticeMessage = noticeMessage
        };
    }

    public async Task<List<Submission>> GetRecentSubmissionsAsync(int take = 50, CancellationToken cancellationToken = default)
    {
        return await _db.Submissions
            .AsNoTracking()
            .Include(s => s.FileAnalysis)
            .OrderByDescending(s => s.SubmittedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Submission>> GetSubmissionsPageAsync(int skip, int take, CancellationToken cancellationToken = default)
    {
        return await _db.Submissions
            .AsNoTracking()
            .Include(s => s.FileAnalysis)
            .OrderByDescending(s => s.SubmittedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public Task<int> GetSubmissionCountAsync(CancellationToken cancellationToken = default)
        => _db.Submissions.CountAsync(cancellationToken);

    public async Task<Submission?> GetSubmissionByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.Submissions
            .Include(s => s.FileAnalysis)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }
}
