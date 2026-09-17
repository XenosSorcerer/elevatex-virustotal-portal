using Microsoft.AspNetCore.SignalR;

namespace ElevateX.Portal.Hubs;

/// <summary>
/// Dedicated SignalR hub for the FR-12 "real-time updates" stand-out — a genuinely separate
/// transport from Blazor Server's own render circuit (see <see cref="ScanHubBroadcaster"/> and
/// DECISIONS.md). Server-to-client broadcast only: no client-invokable methods are exposed.
/// </summary>
public sealed class ScanHub : Hub
{
    public const string Route = "/hubs/scan";
}
