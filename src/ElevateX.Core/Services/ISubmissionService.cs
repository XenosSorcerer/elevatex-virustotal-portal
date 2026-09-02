using ElevateX.Core.Entities;

namespace ElevateX.Core.Services;

public class SubmissionResult
{
    public required Submission Submission { get; set; }
    public required FileAnalysis FileAnalysis { get; set; }
    public bool IsDuplicate { get; set; }
    public bool RequiresScanning { get; set; }
    public string? NoticeMessage { get; set; }
}

public interface ISubmissionService
{
    Task<SubmissionResult> SubmitFileAsync(
        Stream fileStream,
        string fileName,
        long fileSize,
        SubmissionSource source,
        string reasonForSuspicion,
        ThreatPriority threatPriority = ThreatPriority.Medium,
        string? targetDepartment = null,
        CancellationToken cancellationToken = default);

    Task<List<Submission>> GetRecentSubmissionsAsync(int take = 50, CancellationToken cancellationToken = default);
    Task<Submission?> GetSubmissionByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
