namespace OpenDMXBridge.Services.Contracts;

public enum LogLevel
{
    Trace = 0,
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

    public string LevelLabel => Level switch
    {
        LogLevel.Warning => "WARN",
        _ => Level.ToString().ToUpperInvariant()
    };

    public override string ToString() =>
        $"[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{LevelLabel,-5}] {Message}";
}

public interface ILoggingService
{
    event EventHandler<LogEntry>? EntryAdded;

    LogLevel MinimumLevel { get; set; }

    void Log(LogLevel level, string message, string? source = null);
    void Trace(string message, string? source = null) => Log(LogLevel.Trace, message, source);
    void Debug(string message, string? source = null) => Log(LogLevel.Debug, message, source);
    void Info(string message, string? source = null) => Log(LogLevel.Info, message, source);
    void Warning(string message, string? source = null) => Log(LogLevel.Warning, message, source);
    void Error(string message, string? source = null) => Log(LogLevel.Error, message, source);

    IReadOnlyList<LogEntry> GetRecentEntries(int maxCount = 500);
    Task ExportToFileAsync(string filePath, CancellationToken cancellationToken = default);
}
