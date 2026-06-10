using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustPlusApi.Interfaces;

namespace RustPlusApi.Extensions.DependencyInjection;

/// <summary>
/// Default <see cref="IRustPlusFactory"/>: stamps each created client with the host's logging and
/// the configured socket tuning.
/// </summary>
/// <param name="loggerFactory">The host's logger factory; <see langword="null"/> disables client logging.</param>
/// <param name="options">The configured socket tuning options.</param>
internal sealed class RustPlusFactory(ILoggerFactory? loggerFactory, IOptions<RustPlusSocketOptions> options)
    : IRustPlusFactory
{
    /// <inheritdoc />
    public IRustPlus Create(RustPlusConnection connection)
    {
        if (connection is null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        return new RustPlus(connection, options.Value, loggerFactory);
    }
}
