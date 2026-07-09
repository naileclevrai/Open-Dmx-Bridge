namespace OpenDMXBridge.Services.Contracts;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public sealed class LogEntry(DateTimeOffset timestamp, LogLevel level, string message, string? source = null)
{
    public DateTimeOffset Timestamp { get; } = timestamp;
    public LogLevel Level { get; } = level;
    public string Message { get; } = message;
    public string? Source { get; } = source;

    public override string ToString() =>
        $"[{Timestamp:HH:mm:ss.fff}] [{Level,-7}] {Message}";
}

public interface ILoggingService
{
    event EventHandler<LogEntry>? EntryAdded;

    void Log(LogLevel level, string message, string? source = null);
    void Debug(string message, string? source = null) => Log(LogLevel.Debug, message, source);
    void Info(string message, string? source = null) => Log(LogLevel.Info, message, source);
    void Warning(string message, string? source = null) => Log(LogLevel.Warning, message, source);
    void Error(string message, string? source = null) => Log(LogLevel.Error, message, source);

    IReadOnlyList<LogEntry> GetRecentEntries(int maxCount = 500);
}
