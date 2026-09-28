using System.Text;

namespace WindowsCustomizer.Core.Logging;

/// <summary>Severity of a log entry. Ordered so filters can compare.</summary>
public enum LogSeverity
{
    Debug = 0,
    Info = 1,
    Success = 2,
    Warning = 3,
    Error = 4
}

/// <summary>A single structured log record.</summary>
public sealed record LogEntry(
    DateTimeOffset Timestamp,
    LogSeverity Severity,
    string Operation,
    string Message,
    string? Detail = null)
{
    public string FormatLine()
    {
        var sb = new StringBuilder(128);
        sb.Append(Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        sb.Append(" [").Append(Severity.ToString().ToUpperInvariant().PadRight(7)).Append("] ");
        sb.Append(Operation).Append(" :: ").Append(Message);
        return sb.ToString();
    }
}

/// <summary>
/// Central logging abstraction. All engines and services log through this interface so that
/// output can be routed to file, UI, or test sinks without coupling.
/// </summary>
public interface IAppLogger : IDisposable
{
    /// <summary>Raised for every entry (may fire on worker threads; UI must marshal).</summary>
    event EventHandler<LogEntry>? EntryLogged;

    IReadOnlyList<LogEntry> Entries { get; }

    /// <summary>Path of the backing log file, if any.</summary>
    string? LogFilePath { get; }

    void Log(LogSeverity severity, string operation, string message, string? detail = null);
}

public static class AppLoggerExtensions
{
    public static void Debug(this IAppLogger logger, string operation, string message, string? detail = null)
        => logger.Log(LogSeverity.Debug, operation, message, detail);

    public static void Info(this IAppLogger logger, string operation, string message, string? detail = null)
        => logger.Log(LogSeverity.Info, operation, message, detail);

    public static void Success(this IAppLogger logger, string operation, string message, string? detail = null)
        => logger.Log(LogSeverity.Success, operation, message, detail);

    public static void Warn(this IAppLogger logger, string operation, string message, string? detail = null)
        => logger.Log(LogSeverity.Warning, operation, message, detail);

    public static void Error(this IAppLogger logger, string operation, string message, Exception? exception = null)
        => logger.Log(LogSeverity.Error, operation, message, exception?.ToString());
}

/// <summary>
/// Thread-safe logger writing to an optional rolling file plus an in-memory buffer for the UI.
/// </summary>
public sealed class FileLogger : IAppLogger
{
    private readonly object _sync = new();
    private readonly List<LogEntry> _entries = new();
    private readonly StreamWriter? _writer;

    public FileLogger(string? logDirectory = null, string? fileName = null)
    {
        if (!string.IsNullOrWhiteSpace(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
            var path = Path.Combine(logDirectory, fileName ?? $"winimage-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            _writer = new StreamWriter(
                new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };
            LogFilePath = path;
        }
    }

    public event EventHandler<LogEntry>? EntryLogged;

    public string? LogFilePath { get; }

    public IReadOnlyList<LogEntry> Entries
    {
        get { lock (_sync) { return _entries.ToArray(); } }
    }

    public void Log(LogSeverity severity, string operation, string message, string? detail = null)
    {
        var entry = new LogEntry(DateTimeOffset.Now, severity, operation, message, detail);
        lock (_sync)
        {
            _entries.Add(entry);
            if (_entries.Count > 5000)
            {
                _entries.RemoveRange(0, _entries.Count - 5000);
            }

            if (_writer is not null)
            {
                _writer.WriteLine(entry.FormatLine());
                if (!string.IsNullOrEmpty(detail))
                {
                    _writer.WriteLine("    " + detail);
                }
            }
        }

        EntryLogged?.Invoke(this, entry);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _writer?.Dispose();
        }
    }
}
