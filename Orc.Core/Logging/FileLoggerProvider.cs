using System.Text;
using Microsoft.Extensions.Logging;

namespace Orc.Core.Logging;

/// <summary>
/// Minimal append-only file logger. The TUI suppresses console logging so the splash
/// screen stays clean, which means host/service warnings and task failures otherwise
/// vanish. This persists them to {logsDir}/orc-{date}.log for after-the-fact diagnosis.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logsDir;
    private readonly object _gate = new();

    public FileLoggerProvider(string logsDir)
    {
        _logsDir = logsDir;
        Directory.CreateDirectory(_logsDir);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose() { }

    private void Write(string line)
    {
        // Day-rolled file; serialize writes so concurrent stages don't interleave lines.
        var path = Path.Combine(_logsDir, $"orc-{DateTime.UtcNow:yyyyMMdd}.log");
        lock (_gate)
        {
            try { File.AppendAllText(path, line, Encoding.UTF8); }
            catch { /* logging must never throw into the app */ }
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var sb = new StringBuilder();
            sb.Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff"))
              .Append(" [").Append(Short(logLevel)).Append("] ")
              .Append(_category)
              .Append(" - ")
              .Append(formatter(state, exception));
            sb.AppendLine();
            if (exception is not null) sb.AppendLine(exception.ToString());

            _provider.Write(sb.ToString());
        }

        private static string Short(LogLevel l) => l switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => l.ToString(),
        };
    }
}
