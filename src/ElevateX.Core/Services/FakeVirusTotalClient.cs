using System.Collections.Concurrent;
using System.Net;
using ElevateX.Core.Models;

namespace ElevateX.Core.Services;

public class FakeVirusTotalClient : IVirusTotalClient
{
    private readonly ConcurrentDictionary<string, VirusTotalFileReport> _knownFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, VirusTotalAnalysisReport> _inFlightAnalyses = new();

    public int GetFileReportCallCount { get; private set; }
    public int UploadFileCallCount { get; private set; }
    public int GetAnalysisStatusCallCount { get; private set; }

    public bool SimulateRateLimit429 { get; set; }
    public bool SimulateTransient500 { get; set; }

    public void SeedFile(string sha256, int malicious, int suspicious, int undetected, int harmless)
    {
        _knownFiles[sha256] = new VirusTotalFileReport
        {
            Sha256 = sha256,
            MaliciousCount = malicious,
            SuspiciousCount = suspicious,
            UndetectedCount = undetected,
            HarmlessCount = harmless
        };
    }

    public Task<VirusTotalFileReport?> GetFileReportAsync(string hash, CancellationToken cancellationToken = default)
    {
        GetFileReportCallCount++;

        if (SimulateRateLimit429)
            throw new HttpRequestException("Rate limit exceeded (HTTP 429)", null, HttpStatusCode.TooManyRequests);

        if (SimulateTransient500)
            throw new HttpRequestException("Internal server error (HTTP 500)", null, HttpStatusCode.InternalServerError);

        if (_knownFiles.TryGetValue(hash, out var report))
        {
            return Task.FromResult<VirusTotalFileReport?>(report);
        }

        return Task.FromResult<VirusTotalFileReport?>(null);
    }

    public Task<string> UploadFileAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default)
    {
        UploadFileCallCount++;

        if (SimulateRateLimit429)
            throw new HttpRequestException("Rate limit exceeded (HTTP 429)", null, HttpStatusCode.TooManyRequests);

        if (SimulateTransient500)
            throw new HttpRequestException("Internal server error (HTTP 500)", null, HttpStatusCode.InternalServerError);

        var analysisId = $"analysis_{Guid.NewGuid():N}";
        _inFlightAnalyses[analysisId] = new VirusTotalAnalysisReport
        {
            Id = analysisId,
            Status = "completed",
            MaliciousCount = 5,
            SuspiciousCount = 1,
            UndetectedCount = 60,
            HarmlessCount = 6
        };

        return Task.FromResult(analysisId);
    }

    public Task<VirusTotalAnalysisReport?> GetAnalysisStatusAsync(string analysisId, CancellationToken cancellationToken = default)
    {
        GetAnalysisStatusCallCount++;

        if (SimulateRateLimit429)
            throw new HttpRequestException("Rate limit exceeded (HTTP 429)", null, HttpStatusCode.TooManyRequests);

        if (SimulateTransient500)
            throw new HttpRequestException("Internal server error (HTTP 500)", null, HttpStatusCode.InternalServerError);

        if (_inFlightAnalyses.TryGetValue(analysisId, out var analysis))
        {
            return Task.FromResult<VirusTotalAnalysisReport?>(analysis);
        }

        return Task.FromResult<VirusTotalAnalysisReport?>(new VirusTotalAnalysisReport
        {
            Id = analysisId,
            Status = "completed",
            MaliciousCount = 0,
            SuspiciousCount = 0,
            UndetectedCount = 70,
            HarmlessCount = 2
        });
    }
}
