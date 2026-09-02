namespace ElevateX.Core.Entities;

public enum AnalysisStatus
{
    Queued,
    InProgress,
    Completed,
    Failed
}

public enum AnalysisStage
{
    Queued,
    CheckingHash,
    Uploading,
    PollingAnalysis,
    Done,
    Failed
}

public enum SubmissionSource
{
    EmailAttachment,
    UsbDrive,
    WebDownload,
    NetworkShare,
    InternalServer,
    Other
}

public enum ThreatPriority
{
    Low,
    Medium,
    High,
    Critical
}
