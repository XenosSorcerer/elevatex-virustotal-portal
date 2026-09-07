using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ElevateX.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ElevateX.Core.Services;

public class VirusTotalClient : IVirusTotalClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<VirusTotalClient> _logger;
    private readonly VirusTotalOptions _options;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public VirusTotalClient(
        HttpClient httpClient,
        IOptions<VirusTotalOptions> options,
        ILogger<VirusTotalClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;

        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri(_options.BaseUrl);
        }

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Remove("x-apikey");
            _httpClient.DefaultRequestHeaders.Add("x-apikey", _options.ApiKey);
        }
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<VirusTotalFileReport?> GetFileReportAsync(string hash, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Querying VirusTotal report for hash {Hash}", hash);

        using var response = await _httpClient.GetAsync($"files/{hash}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Hash {Hash} not found on VirusTotal (HTTP 404).", hash);
            return null;
        }

        await EnsureSuccessOrThrowAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var parsed = JsonSerializer.Deserialize<VtResponse<VtFileData>>(json, JsonOptions);

        if (parsed?.Data?.Attributes == null)
        {
            return null;
        }

        var attr = parsed.Data.Attributes;
        var stats = attr.LastAnalysisStats ?? new VtAnalysisStats();

        return new VirusTotalFileReport
        {
            Sha256 = attr.Sha256 ?? hash,
            Md5 = attr.Md5,
            Sha1 = attr.Sha1,
            MeaningfulName = attr.MeaningfulName,
            Size = attr.Size,
            MaliciousCount = stats.Malicious,
            SuspiciousCount = stats.Suspicious,
            UndetectedCount = stats.Undetected,
            HarmlessCount = stats.Harmless
        };
    }

    public async Task<string> UploadFileAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Uploading file {FileName} ({Length} bytes) to VirusTotal", fileName, fileStream.Length);

        using var content = new MultipartFormDataContent();
        using var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(streamContent, "file", fileName);

        using var response = await _httpClient.PostAsync("files", content, cancellationToken);
        await EnsureSuccessOrThrowAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var parsed = JsonSerializer.Deserialize<VtResponse<VtAnalysisData>>(json, JsonOptions);

        var analysisId = parsed?.Data?.Id;
        if (string.IsNullOrWhiteSpace(analysisId))
        {
            throw new InvalidOperationException("VirusTotal upload response did not contain an analysis ID.");
        }

        _logger.LogInformation("File {FileName} uploaded successfully. Analysis ID: {AnalysisId}", fileName, analysisId);
        return analysisId;
    }

    public async Task<VirusTotalAnalysisReport?> GetAnalysisStatusAsync(string analysisId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Polling VirusTotal analysis status for AnalysisId {AnalysisId}", analysisId);

        using var response = await _httpClient.GetAsync($"analyses/{analysisId}", cancellationToken);
        await EnsureSuccessOrThrowAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var parsed = JsonSerializer.Deserialize<VtResponse<VtAnalysisData>>(json, JsonOptions);

        if (parsed?.Data?.Attributes == null)
        {
            return null;
        }

        var attr = parsed.Data.Attributes;
        var stats = attr.Stats ?? new VtAnalysisStats();

        return new VirusTotalAnalysisReport
        {
            Id = parsed.Data.Id ?? analysisId,
            Status = attr.Status ?? "queued",
            Sha256 = attr.Sha256,
            MaliciousCount = stats.Malicious,
            SuspiciousCount = stats.Suspicious,
            UndetectedCount = stats.Undetected,
            HarmlessCount = stats.Harmless
        };
    }

    /// <summary>
    /// Replaces <c>HttpResponseMessage.EnsureSuccessStatusCode()</c> so the status code and
    /// any <c>Retry-After</c> hint survive into the retry policy as a typed exception.
    /// </summary>
    private static async Task EnsureSuccessOrThrowAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var retryAfter = response.Headers.RetryAfter?.Delta
            ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null);

        string body = string.Empty;
        try { body = await response.Content.ReadAsStringAsync(cancellationToken); }
        catch { /* body is best-effort context only */ }

        if (body.Length > 200) body = body[..200];

        throw new VirusTotalApiException(
            response.StatusCode,
            retryAfter,
            $"VirusTotal {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} " +
            $"returned {(int)response.StatusCode} ({response.ReasonPhrase}). {body}".Trim());
    }
}
