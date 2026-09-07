using System.Net;
using ElevateX.Core.Models;
using ElevateX.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ElevateX.Tests.Unit;

public class ResilienceTests
{
    private static ApiRateLimiter Limiter(FakeTimeProvider time, int perMinute = 4) =>
        new(Options.Create(new VirusTotalOptions { RateLimitRequestsPerMinute = perMinute }), time);

    [Fact]
    public async Task First_call_is_granted_immediately()
    {
        var task = Limiter(new FakeTimeProvider()).WaitForTurnAsync();

        task.IsCompletedSuccessfully.Should().BeTrue();
        await task;
    }

    [Fact]
    public async Task Subsequent_calls_are_spaced_by_the_minimum_interval()
    {
        var time = new FakeTimeProvider();
        var limiter = Limiter(time, perMinute: 4); // -> 15s spacing, <= 4/min

        await limiter.WaitForTurnAsync();

        for (var i = 0; i < 4; i++)
        {
            var next = limiter.WaitForTurnAsync();
            next.IsCompleted.Should().BeFalse("call {0} must wait for its turn", i + 2);

            time.Advance(TimeSpan.FromSeconds(14));
            next.IsCompleted.Should().BeFalse("14s is short of the 15s interval");

            time.Advance(TimeSpan.FromSeconds(1));
            await next; // 15s reached -> granted
        }
    }

    [Fact]
    public async Task Waiting_for_a_turn_honours_cancellation()
    {
        var limiter = Limiter(new FakeTimeProvider());
        await limiter.WaitForTurnAsync(); // consume the free slot

        using var cts = new CancellationTokenSource();
        var pending = limiter.WaitForTurnAsync(cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.GatewayTimeout, true)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    public void Retry_classification_only_retries_genuine_transient_codes(HttpStatusCode code, bool expected)
        => ScanDispatcherBackgroundService.IsTransient(code).Should().Be(expected);

    [Fact]
    public void Retry_classification_treats_a_missing_status_as_transient()
        => ScanDispatcherBackgroundService.IsTransient(null).Should().BeTrue();
}
