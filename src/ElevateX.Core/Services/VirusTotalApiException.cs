using System.Net;

namespace ElevateX.Core.Services;

/// <summary>
/// Raised by <see cref="VirusTotalClient"/> on a non-success HTTP response. Carries the
/// status code and any <c>Retry-After</c> hint so the dispatcher's retry policy can
/// distinguish transient failures and honour the server's requested wait.
/// </summary>
public sealed class VirusTotalApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public TimeSpan? RetryAfter { get; }

    public VirusTotalApiException(HttpStatusCode statusCode, TimeSpan? retryAfter, string message)
        : base(message)
    {
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }
}
