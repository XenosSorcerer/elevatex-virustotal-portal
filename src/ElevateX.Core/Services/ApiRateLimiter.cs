using ElevateX.Core.Models;
using Microsoft.Extensions.Options;

namespace ElevateX.Core.Services;

public interface IApiRateLimiter
{
    /// <summary>
    /// Blocks until it is compliant to make one more outbound VirusTotal request,
    /// then records the grant. Serialised, so concurrent callers cannot slip past
    /// together.
    /// </summary>
    Task WaitForTurnAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Process-wide pacing gate for outbound VirusTotal calls. Enforces BOTH a minimum
/// spacing between grants (60s / RateLimitRequestsPerMinute) AND a rolling
/// "N per 60s" ceiling. Clock is injected so tests can drive virtual time.
/// </summary>
public sealed class ApiRateLimiter : IApiRateLimiter, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _time;
    private readonly TimeSpan _minInterval;
    private readonly int _maxPerWindow;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly Queue<DateTimeOffset> _recentGrants = new();
    private DateTimeOffset _lastGrant = DateTimeOffset.MinValue;

    public ApiRateLimiter(IOptions<VirusTotalOptions> options, TimeProvider time)
    {
        _time = time;
        var perMinute = Math.Max(1, options.Value.RateLimitRequestsPerMinute);
        _maxPerWindow = perMinute;
        _minInterval = TimeSpan.FromSeconds(60.0 / perMinute); // 4/min -> 15s
    }

    public async Task WaitForTurnAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            while (true)
            {
                var now = _time.GetUtcNow();

                // Drop grants that have aged out of the rolling window.
                while (_recentGrants.Count > 0 && now - _recentGrants.Peek() >= Window)
                    _recentGrants.Dequeue();

                var wait = TimeSpan.Zero;

                var sinceLast = now - _lastGrant;
                if (sinceLast < _minInterval)
                    wait = _minInterval - sinceLast;

                if (_recentGrants.Count >= _maxPerWindow)
                {
                    var windowWait = _recentGrants.Peek() + Window - now;
                    if (windowWait > wait) wait = windowWait;
                }

                if (wait <= TimeSpan.Zero)
                {
                    _lastGrant = now;
                    _recentGrants.Enqueue(now);
                    return;
                }

                await Task.Delay(wait, _time, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
