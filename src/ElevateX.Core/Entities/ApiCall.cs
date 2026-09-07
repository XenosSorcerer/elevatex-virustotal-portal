namespace ElevateX.Core.Entities;

public enum ApiEndpointKind
{
    HashLookup,
    Upload,
    AnalysisPoll,
    Unknown
}

public enum ApiCallOutcome
{
    Ok,
    NotFound,
    RateLimited,
    TransientError,
    Failed
}

/// <summary>
/// One row per outbound VirusTotal HTTP request (including retries). Drives the
/// daily-quota guard and the FR-11 quota-consumption widget. Survives restarts so
/// a bounce cannot reset the 500/day budget.
/// </summary>
public class ApiCall
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public ApiEndpointKind EndpointKind { get; set; }
    public ApiCallOutcome Outcome { get; set; }
    public int StatusCode { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public Guid? FileAnalysisId { get; set; }
}
