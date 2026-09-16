using ElevateX.Core.Services;

namespace ElevateX.Portal.Logging;

/// <summary>
/// A plain Microsoft.Extensions.Logging provider — not a Serilog sink — so the in-app error feed
/// keeps working regardless of which logging backend is active. Registered alongside Serilog with
/// `writeToProviders: true` so both receive every log call unchanged (see Program.cs).
/// Captures errors/criticals from anywhere, and warnings only from this app's own components —
/// framework-internal warnings (routing, EF, etc.) are noise an analyst doesn't need surfaced.
/// </summary>
public sealed class ErrorFeedLoggerProvider : ILoggerProvider
{
    private readonly IErrorFeedService _feed;

    public ErrorFeedLoggerProvider(IErrorFeedService feed) => _feed = feed;

    public ILogger CreateLogger(string categoryName) => new ErrorFeedLogger(categoryName, _feed);

    public void Dispose()
    {
    }

    private sealed class ErrorFeedLogger : ILogger
    {
        private readonly string _category;
        private readonly IErrorFeedService _feed;

        public ErrorFeedLogger(string category, IErrorFeedService feed)
        {
            _category = category;
            _feed = feed;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel >= LogLevel.Error ||
            (logLevel == LogLevel.Warning && _category.StartsWith("ElevateX", StringComparison.Ordinal));

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            if (exception is not null)
                message = $"{message} ({exception.GetType().Name}: {exception.Message})";

            _feed.Record(new ErrorFeedEntry(DateTimeOffset.UtcNow, logLevel, ShortCategory(_category), message));
        }

        private static string ShortCategory(string category)
        {
            var lastDot = category.LastIndexOf('.');
            return lastDot >= 0 ? category[(lastDot + 1)..] : category;
        }
    }
}
