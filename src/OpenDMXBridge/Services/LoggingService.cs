using System.Collections.Concurrent;
using System.IO;
using System.Text;
using OpenDMXBridge.Services.Contracts;

namespace OpenDMXBridge.Services;

public sealed class LoggingService : ILoggingService
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private const int MaxEntries = 5000;

    public event EventHandler<LogEntry>? EntryAdded;

    public LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    public void Log(LogLevel level, string message, string? source = null)
    {
        if (level < MinimumLevel)
            return;

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

    public Task ExportToFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var sb = new StringBuilder(_entries.Count * 80);
            foreach (var entry in _entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.AppendLine(entry.ToString());
            }

            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }, cancellationToken);
    }
}
