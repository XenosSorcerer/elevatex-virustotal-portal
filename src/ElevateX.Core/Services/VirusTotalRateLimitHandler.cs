using ElevateX.Core.Entities;

namespace ElevateX.Core.Services;

/// <summary>
/// Cross-cutting HTTP layer for the VirusTotal client: paces every outbound request
/// through the shared <see cref="IApiRateLimiter"/> (so a single multi-step scan can no
/// longer fire three calls back-to-back), and records each call for quota accounting.
/// </summary>
public sealed class VirusTotalRateLimitHandler : DelegatingHandler
{
    private readonly IApiRateLimiter _rateLimiter;
    private readonly IApiCallRecorder _recorder;

    public VirusTotalRateLimitHandler(IApiRateLimiter rateLimiter, IApiCallRecorder recorder)
    {
        _rateLimiter = rateLimiter;
        _recorder = recorder;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await _rateLimiter.WaitForTurnAsync(cancellationToken);

        var kind = Classify(request);
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            await SafeRecordAsync(kind, OutcomeFor((int)response.StatusCode), (int)response.StatusCode);
            return response;
        }
        catch
        {
            await SafeRecordAsync(kind, ApiCallOutcome.TransientError, 0);
            throw;
        }
    }

    private async Task SafeRecordAsync(ApiEndpointKind kind, ApiCallOutcome outcome, int status)
    {
        // Quota accounting must never break a scan.
        try { await _recorder.RecordAsync(kind, outcome, status, CancellationToken.None); }
        catch { /* best effort */ }
    }

    private static ApiEndpointKind Classify(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        if (path.Contains("/analyses/", StringComparison.OrdinalIgnoreCase))
            return ApiEndpointKind.AnalysisPoll;
        if (request.Method == HttpMethod.Post && path.EndsWith("/files", StringComparison.OrdinalIgnoreCase))
            return ApiEndpointKind.Upload;
        if (path.Contains("/files/", StringComparison.OrdinalIgnoreCase))
            return ApiEndpointKind.HashLookup;

        return ApiEndpointKind.Unknown;
    }

    private static ApiCallOutcome OutcomeFor(int status) => status switch
    {
        404 => ApiCallOutcome.NotFound,
        429 => ApiCallOutcome.RateLimited,
        >= 200 and < 300 => ApiCallOutcome.Ok,
        >= 500 => ApiCallOutcome.TransientError,
        _ => ApiCallOutcome.Failed
    };
}
