using ElevateX.Core.Data;
using ElevateX.Core.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace ElevateX.Core.Services;

public interface IApiCallRecorder
{
    Task RecordAsync(ApiEndpointKind kind, ApiCallOutcome outcome, int statusCode, CancellationToken ct);
}

/// <summary>
/// Persists one <see cref="ApiCall"/> row per outbound VirusTotal request. Kept as a
/// singleton with its own DI scope per write so it can be consumed by the HTTP message
/// handler without capturing a scoped <see cref="AppDbContext"/>.
/// </summary>
public sealed class ApiCallRecorder : IApiCallRecorder
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _time;

    public ApiCallRecorder(IServiceScopeFactory scopeFactory, TimeProvider time)
    {
        _scopeFactory = scopeFactory;
        _time = time;
    }

    public async Task RecordAsync(ApiEndpointKind kind, ApiCallOutcome outcome, int statusCode, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.ApiCalls.Add(new ApiCall
        {
            EndpointKind = kind,
            Outcome = outcome,
            StatusCode = statusCode,
            OccurredAtUtc = _time.GetUtcNow().UtcDateTime
        });

        await db.SaveChangesAsync(ct);
    }
}
