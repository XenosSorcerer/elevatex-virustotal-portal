using ElevateX.Core.Data;
using ElevateX.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ElevateX.Core.Services;

public interface IQuotaGuard
{
    Task<int> UsedTodayAsync(CancellationToken ct = default);
    Task<bool> HasDailyQuotaAsync(CancellationToken ct = default);
}

/// <summary>
/// Enforces the VirusTotal daily request cap by counting persisted <see cref="Entities.ApiCall"/>
/// rows since 00:00 UTC. Persistence is deliberate: a restart must not reset the budget.
/// </summary>
public sealed class QuotaGuard : IQuotaGuard
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _time;
    private readonly VirusTotalOptions _options;

    public QuotaGuard(AppDbContext db, TimeProvider time, IOptions<VirusTotalOptions> options)
    {
        _db = db;
        _time = time;
        _options = options.Value;
    }

    public async Task<int> UsedTodayAsync(CancellationToken ct = default)
    {
        var startOfUtcDay = _time.GetUtcNow().UtcDateTime.Date;
        return await _db.ApiCalls.CountAsync(c => c.OccurredAtUtc >= startOfUtcDay, ct);
    }

    public async Task<bool> HasDailyQuotaAsync(CancellationToken ct = default)
        => await UsedTodayAsync(ct) < _options.DailyRequestCap;
}
