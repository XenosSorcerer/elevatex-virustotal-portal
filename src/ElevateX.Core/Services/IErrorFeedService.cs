using Microsoft.Extensions.Logging;

namespace ElevateX.Core.Services;

public sealed record ErrorFeedEntry(DateTimeOffset TimestampUtc, LogLevel Level, string Category, string Message);

/// <summary>
/// Bounded, in-memory feed of recent Warning+/Error+ log entries, surfaced as a live notification
/// bell in the UI (FR-12). Deliberately independent of any specific logging backend — see
/// ElevateX.Portal.Logging.ErrorFeedLoggerProvider, a plain Microsoft.Extensions.Logging provider.
/// </summary>
public interface IErrorFeedService
{
    IReadOnlyList<ErrorFeedEntry> Recent { get; }
    int UnreadCount { get; }
    event Action<ErrorFeedEntry>? EntryAdded;
    void Record(ErrorFeedEntry entry);
    void MarkAllRead();
}

public sealed class ErrorFeedService : IErrorFeedService
{
    private const int Capacity = 50;
    private readonly object _gate = new();
    private readonly LinkedList<ErrorFeedEntry> _entries = new();
    private int _unread;

    public event Action<ErrorFeedEntry>? EntryAdded;

    public IReadOnlyList<ErrorFeedEntry> Recent
    {
        get { lock (_gate) { return _entries.ToArray(); } }
    }

    public int UnreadCount
    {
        get { lock (_gate) { return _unread; } }
    }

    public void Record(ErrorFeedEntry entry)
    {
        lock (_gate)
        {
            _entries.AddFirst(entry);
            if (_entries.Count > Capacity) _entries.RemoveLast();
            _unread++;
        }

        EntryAdded?.Invoke(entry);
    }

    public void MarkAllRead()
    {
        lock (_gate) { _unread = 0; }
    }
}
