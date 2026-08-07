using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace HaCompanion_App.Services;

/// <summary>
/// A minimal rolling file logger, so a user can hand us a log when something
/// misbehaves. Writes to %LOCALAPPDATA%\HaCompanion\logs\companion-yyyyMMdd.log.
/// </summary>
/// <remarks>
/// Deliberately tiny and dependency-free. Secrets are never passed to the logging
/// calls in the first place; this class additionally refuses to write anything
/// that looks like a bearer token as a defence in depth.
/// </remarks>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private static readonly object Gate = new();
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private readonly LogLevel _minimum;

    public FileLoggerProvider(LogLevel minimum = LogLevel.Information)
    {
        _minimum = minimum;
        Directory.CreateDirectory(LogDirectory);
        Prune();
    }

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HaCompanion", "logs");

    public static string CurrentLogFile =>
        Path.Combine(LogDirectory, $"companion-{DateTime.Now:yyyyMMdd}.log");

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _minimum));

    public void Dispose() => _loggers.Clear();

    /// <summary>Keeps a week of logs so the folder cannot grow without bound.</summary>
    private static void Prune()
    {
        try
        {
            foreach (var file in Directory.GetFiles(LogDirectory, "companion-*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-7))
                    File.Delete(file);
            }
        }
        catch
        {
            // Logging must never break startup.
        }
    }

    internal static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                File.AppendAllText(CurrentLogFile, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Never throw from logging.
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _category;
        private readonly LogLevel _minimum;

        public FileLogger(string category, LogLevel minimum)
        {
            // Only the class name is useful; the namespace just makes lines long.
            _category = category[(category.LastIndexOf('.') + 1)..];
            _minimum = minimum;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= _minimum && logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            if (LooksLikeSecret(message)) message = "[redacted]";

            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {Short(logLevel)} {_category}: {message}";
            if (exception is not null)
                line += Environment.NewLine + exception;

            Write(line);
        }

        private static bool LooksLikeSecret(string message) =>
            message.Contains("Bearer ", StringComparison.OrdinalIgnoreCase)
            || message.Contains("access_token", StringComparison.OrdinalIgnoreCase)
            || message.Contains("refresh_token", StringComparison.OrdinalIgnoreCase);

        private static string Short(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???"
        };
    }
}
