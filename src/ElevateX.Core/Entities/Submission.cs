namespace ElevateX.Core.Entities;

public class Submission
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid FileAnalysisId { get; set; }
    public FileAnalysis? FileAnalysis { get; set; }

    public SubmissionSource Source { get; set; } = SubmissionSource.Other;
    public required string ReasonForSuspicion { get; set; }
    public ThreatPriority ThreatPriority { get; set; } = ThreatPriority.Medium;
    public string? TargetDepartment { get; set; }

    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
}
