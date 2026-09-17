using ElevateX.Core.Entities;
using ElevateX.Core.Services;
using ElevateX.Portal.Hubs;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ElevateX.Tests.Unit;

public class ScanHubBroadcasterTests
{
    private static (ScanHubBroadcaster Broadcaster, ScanNotifier Notifier, IClientProxy ClientProxy) Build()
    {
        var notifier = new ScanNotifier();

        var clientProxy = Substitute.For<IClientProxy>();
        var hubClients = Substitute.For<IHubClients>();
        hubClients.All.Returns(clientProxy);
        var hubContext = Substitute.For<IHubContext<ScanHub>>();
        hubContext.Clients.Returns(hubClients);

        var broadcaster = new ScanHubBroadcaster(notifier, hubContext, NullLogger<ScanHubBroadcaster>.Instance);
        return (broadcaster, notifier, clientProxy);
    }

    [Fact]
    public async Task Starting_forwards_a_published_scan_event_to_all_hub_clients()
    {
        var (broadcaster, notifier, clientProxy) = Build();
        await broadcaster.StartAsync(CancellationToken.None);

        notifier.Publish(new ScanEvent(Guid.NewGuid(), AnalysisStatus.Completed, IsNewSubmission: false));

        // Broadcasting is fire-and-forget from the notifier's synchronous event; give the
        // spawned Task a moment to run before asserting.
        await Task.Delay(100);

        await clientProxy.Received(1).SendCoreAsync(
            "scanChanged", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stopping_unsubscribes_so_later_events_are_not_forwarded()
    {
        var (broadcaster, notifier, clientProxy) = Build();
        await broadcaster.StartAsync(CancellationToken.None);
        await broadcaster.StopAsync(CancellationToken.None);

        notifier.Publish(new ScanEvent(Guid.NewGuid(), AnalysisStatus.Failed, IsNewSubmission: false));
        await Task.Delay(100);

        await clientProxy.DidNotReceive().SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_broadcast_failure_is_swallowed_and_never_reaches_the_publisher()
    {
        var (broadcaster, notifier, clientProxy) = Build();
        clientProxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("hub transport unavailable"));

        await broadcaster.StartAsync(CancellationToken.None);

        var publish = () => notifier.Publish(new ScanEvent(Guid.NewGuid(), AnalysisStatus.InProgress, IsNewSubmission: true));

        publish.Should().NotThrow();
    }
}
