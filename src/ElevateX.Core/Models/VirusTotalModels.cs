using System.Text.Json.Serialization;

namespace ElevateX.Core.Models;

public class VirusTotalFileReport
{
    public required string Sha256 { get; set; }
    public string? Md5 { get; set; }
    public string? Sha1 { get; set; }
    public string? MeaningfulName { get; set; }
    public long Size { get; set; }
    public int MaliciousCount { get; set; }
    public int SuspiciousCount { get; set; }
    public int UndetectedCount { get; set; }
    public int HarmlessCount { get; set; }
    public int TotalEngines => MaliciousCount + SuspiciousCount + UndetectedCount + HarmlessCount;
    public string ScanSummary => $"{MaliciousCount}/{TotalEngines} security vendors flagged this file as malicious";
    public string ReportUrl => $"https://www.virustotal.com/gui/file/{Sha256}";
}

public class VirusTotalAnalysisReport
{
    public required string Id { get; set; }
    public required string Status { get; set; } // queued, in-progress, completed
    public int MaliciousCount { get; set; }
    public int SuspiciousCount { get; set; }
    public int UndetectedCount { get; set; }
    public int HarmlessCount { get; set; }
    public int TotalEngines => MaliciousCount + SuspiciousCount + UndetectedCount + HarmlessCount;
    public string? Sha256 { get; set; }
}

public class VirusTotalOptions
{
    public const string SectionName = "VirusTotal";
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://www.virustotal.com/api/v3/";
    public int RateLimitRequestsPerMinute { get; set; } = 4;
}

// Internal JSON deserialization DTOs for VirusTotal API v3
public class VtResponse<T>
{
    [JsonPropertyName("data")]
    public T? Data { get; set; }
}

public class VtFileData
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("attributes")]
    public VtFileAttributes? Attributes { get; set; }
}

public class VtFileAttributes
{
    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("md5")]
    public string? Md5 { get; set; }

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("meaningful_name")]
    public string? MeaningfulName { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("last_analysis_stats")]
    public VtAnalysisStats? LastAnalysisStats { get; set; }
}

public class VtAnalysisStats
{
    [JsonPropertyName("malicious")]
    public int Malicious { get; set; }

    [JsonPropertyName("suspicious")]
    public int Suspicious { get; set; }

    [JsonPropertyName("undetected")]
    public int Undetected { get; set; }

    [JsonPropertyName("harmless")]
    public int Harmless { get; set; }
}

public class VtAnalysisData
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("attributes")]
    public VtAnalysisAttributes? Attributes { get; set; }
}

public class VtAnalysisAttributes
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("stats")]
    public VtAnalysisStats? Stats { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }
}
