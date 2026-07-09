using System.Collections.Concurrent;
using System.Text.Json;
using OpenDMXBridge.Models;
using OpenDMXBridge.Services.Contracts;

namespace OpenDMXBridge.Services;

public sealed class LoggingService : ILoggingService
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private const int MaxEntries = 2000;

    public event EventHandler<LogEntry>? EntryAdded;

    public void Log(LogLevel level, string message, string? source = null)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, message, source);
        _entries.Enqueue(entry);

        while (_entries.Count > MaxEntries && _entries.TryDequeue(out _))
        {
        }

        EntryAdded?.Invoke(this, entry);
    }

    public IReadOnlyList<LogEntry> GetRecentEntries(int maxCount = 500)
    {
        var list = _entries.ToArray();
        if (list.Length <= maxCount)
            return list;

        return list[^maxCount..];
    }
}
