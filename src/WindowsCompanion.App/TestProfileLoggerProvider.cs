#if DEBUG
using Microsoft.Extensions.Logging;

namespace WindowsCompanion_App;

internal sealed class TestProfileLoggerProvider : ILoggerProvider
{
    internal const string FileName = "app.log";

    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private bool _disposed;

    internal TestProfileLoggerProvider(string settingsDirectory)
    {
        Directory.CreateDirectory(settingsDirectory);
        _writer = new StreamWriter(
            new FileStream(
                Path.Combine(settingsDirectory, FileName),
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite))
        {
            AutoFlush = true
        };
    }

    public ILogger CreateLogger(string categoryName) => new TestProfileLogger(this, categoryName);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _writer.Dispose();
        }
    }

    private void Write(
        string category,
        LogLevel level,
        EventId eventId,
        string message,
        Exception? exception)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _writer.Write(DateTimeOffset.UtcNow.ToString("O"));
            _writer.Write(" [");
            _writer.Write(level);
            _writer.Write("] ");
            _writer.Write(category);
            if (eventId.Id != 0)
            {
                _writer.Write(" (");
                _writer.Write(eventId.Id);
                _writer.Write(')');
            }
            _writer.Write(": ");
            _writer.WriteLine(message.ReplaceLineEndings(" "));
            if (exception is not null)
                _writer.WriteLine(exception.ToString().ReplaceLineEndings(" "));
        }
    }

    private sealed class TestProfileLogger(
        TestProfileLoggerProvider provider,
        string category) : ILogger
    {
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
            provider.Write(category, logLevel, eventId, formatter(state, exception), exception);
        }
    }
}
#endif
