using ElevateX.Core.Entities;
using ElevateX.Core.Services;
using Microsoft.AspNetCore.SignalR;

namespace ElevateX.Portal.Hubs;

/// <summary>
/// Forwards every <see cref="ScanEvent"/> from <see cref="IScanNotifier"/> onto the dedicated
/// <see cref="ScanHub"/>, so a client that never opens a Blazor circuit — a second browser tab
/// on the dashboard, a future non-Blazor client — still sees scan-status changes live. Runs
/// alongside the existing in-process notifier (Submissions.razor / Dashboard.razor still
/// subscribe to that directly); this does not replace it, it adds a second, independent channel.
/// Registered as an <see cref="IHostedService"/> purely so DI constructs it eagerly at startup
/// and subscribes immediately, rather than lazily on first hub connection.
/// </summary>
public sealed class ScanHubBroadcaster : IHostedService
{
    private readonly IScanNotifier _notifier;
    private readonly IHubContext<ScanHub> _hub;
    private readonly ILogger<ScanHubBroadcaster> _logger;

    public ScanHubBroadcaster(IScanNotifier notifier, IHubContext<ScanHub> hub, ILogger<ScanHubBroadcaster> logger)
    {
        _notifier = notifier;
        _hub = hub;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _notifier.Changed += OnScanEvent;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _notifier.Changed -= OnScanEvent;
        return Task.CompletedTask;
    }

    private void OnScanEvent(ScanEvent scanEvent) => _ = BroadcastAsync(scanEvent);

    private async Task BroadcastAsync(ScanEvent scanEvent)
    {
        try
        {
            await _hub.Clients.All.SendAsync("scanChanged", new
            {
                fileAnalysisId = scanEvent.FileAnalysisId,
                status = scanEvent.Status.ToString(),
                isNewSubmission = scanEvent.IsNewSubmission,
                atUtc = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            // A broadcast hiccup must never take down the scan pipeline that raised the event.
            _logger.LogWarning(ex, "Failed to broadcast scan event to ScanHub clients.");
        }
    }
}
