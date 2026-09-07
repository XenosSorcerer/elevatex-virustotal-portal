namespace ElevateX.Core.Entities;

public class FileAnalysis
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Sha256 { get; set; }
    public string? Md5 { get; set; }
    public string? Sha1 { get; set; }

    public required string FileName { get; set; }
    public long FileSizeBytes { get; set; }

    public AnalysisStatus Status { get; set; } = AnalysisStatus.Queued;
    public AnalysisStage CurrentStage { get; set; } = AnalysisStage.Queued;

    public string? AnalysisId { get; set; }

    public int MaliciousCount { get; set; }
    public int SuspiciousCount { get; set; }
    public int UndetectedCount { get; set; }
    public int HarmlessCount { get; set; }
    public int TotalEngines { get; set; }

    public string? ScanSummary { get; set; }
    public string? VirusTotalReportUrl { get; set; }

    public DateTime FirstScannedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastScannedAtUtc { get; set; } = DateTime.UtcNow;

    public int RetryCount { get; set; }
    public int PollCount { get; set; }
    public string? FailureReason { get; set; }

    public ICollection<Submission> Submissions { get; set; } = new List<Submission>();
}
