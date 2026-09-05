using Microsoft.Extensions.Logging;

namespace RustPlusApi.CredentialsWeb.UnitTests;

/// <summary>Captures every log record the app writes, at every level, so a test can assert on what
/// is absent from them.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<string> _records = [];

    /// <summary>Formatted messages, each with its exception appended.</summary>
    internal IReadOnlyList<string> Records
    {
        get
        {
            lock (_records)
            {
                return _records.ToArray();
            }
        }
    }

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_records, categoryName);

    /// <inheritdoc/>
    public void Dispose()
    {
        // Nothing to release.
    }

    private sealed class CapturingLogger(List<string> records, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var line = $"{category}: {formatter(state, exception)} {exception}";
            lock (records)
            {
                records.Add(line);
            }
        }
    }
}
