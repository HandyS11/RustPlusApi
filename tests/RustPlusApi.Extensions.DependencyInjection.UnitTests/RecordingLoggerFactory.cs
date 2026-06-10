using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RustPlusApi.Extensions.DependencyInjection.UnitTests;

/// <summary>Records the category names requested from the factory; the loggers themselves are no-ops.</summary>
internal sealed class RecordingLoggerFactory : ILoggerFactory
{
    public List<string> Categories { get; } = [];

    public ILogger CreateLogger(string categoryName)
    {
        Categories.Add(categoryName);
        return NullLogger.Instance;
    }

    public void AddProvider(ILoggerProvider provider)
    {
        // No-op: this factory only records requested categories.
    }

    public void Dispose()
    {
        // Nothing to release.
    }
}
