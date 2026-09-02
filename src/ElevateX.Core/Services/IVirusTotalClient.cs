using ElevateX.Core.Models;

namespace ElevateX.Core.Services;

public interface IVirusTotalClient
{
    /// <summary>
    /// Looks up a file report by SHA-256 (or MD5/SHA-1) without uploading binary (1 API call).
    /// Returns null if file is not found (HTTP 404).
    /// </summary>
    Task<VirusTotalFileReport?> GetFileReportAsync(string hash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a file binary to VirusTotal for analysis (1 API call).
    /// Returns the analysis ID.
    /// </summary>
    Task<string> UploadFileAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks status of an analysis job by analysis ID (1 API call).
    /// </summary>
    Task<VirusTotalAnalysisReport?> GetAnalysisStatusAsync(string analysisId, CancellationToken cancellationToken = default);
}
