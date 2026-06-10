using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Interfaces;

namespace RustPlusApi.Fcm.Extensions.DependencyInjection;

/// <summary>
/// Default <see cref="IRustPlusFcmFactory"/>: stamps each created listener with the host's logging
/// and the configured tuning.
/// </summary>
/// <param name="loggerFactory">The host's logger factory; <see langword="null"/> disables listener logging.</param>
/// <param name="options">The configured tuning options.</param>
internal sealed class RustPlusFcmFactory(ILoggerFactory? loggerFactory, IOptions<RustPlusFcmSocketOptions> options) : IRustPlusFcmFactory
{
    /// <inheritdoc />
    public IRustPlusFcm Create(Credentials credentials, ICollection<string>? persistentIds = null)
    {
        if (credentials is null)
        {
            throw new ArgumentNullException(nameof(credentials));
        }

        return new RustPlusFcm(credentials, persistentIds ?? [], options.Value, loggerFactory);
    }
}
