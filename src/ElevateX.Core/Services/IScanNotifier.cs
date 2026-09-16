using ElevateX.Core.Entities;

namespace ElevateX.Core.Services;

/// <summary>
/// Fired whenever a FileAnalysis is created/changes status, or a new Submission is recorded.
/// Purely a "something changed, go re-query" pulse — consumers should treat Status as informational
/// only and re-read fresh state, since a few paths (e.g. a Submission-Added event for a resubmission
/// of an already-Completed file) don't recompute the analysis's real current status before firing.
/// </summary>
public sealed record ScanEvent(Guid FileAnalysisId, AnalysisStatus Status, bool IsNewSubmission);

public interface IScanNotifier
{
    event Action<ScanEvent>? Changed;
    void Publish(ScanEvent scanEvent);
}

/// <summary>
/// In-process pub/sub riding Blazor Server's existing SignalR circuit (FR-12 "real-time updates") —
/// published from a single choke point (<see cref="Data.AppDbContext.SaveChangesAsync"/>) rather than
/// every scan-pipeline/dispatcher/submission call site. Subscribers are responsible for marshalling
/// back to their own circuit (InvokeAsync) — publishing must never block on UI work.
/// </summary>
public sealed class ScanNotifier : IScanNotifier
{
    public event Action<ScanEvent>? Changed;

    public void Publish(ScanEvent scanEvent) => Changed?.Invoke(scanEvent);
}
