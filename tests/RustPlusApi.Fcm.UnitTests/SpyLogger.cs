using Microsoft.Extensions.Logging;

namespace RustPlusApi.Fcm.UnitTests;

/// <summary>In-memory logger capturing entries so tests can assert level + message.</summary>
public sealed class SpyLogger : ILogger
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, formatter(state, exception)));

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}

/// <summary>Factory that hands out a single shared <see cref="SpyLogger"/> for assertions.</summary>
public sealed class SpyLoggerFactory : ILoggerFactory
{
    public SpyLogger Logger { get; } = new();
    public ILogger CreateLogger(string categoryName) => Logger;
    public void AddProvider(ILoggerProvider provider) { }
    public void Dispose() { }
}
