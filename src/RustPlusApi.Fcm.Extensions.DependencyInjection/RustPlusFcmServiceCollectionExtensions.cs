using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustPlusApi.Fcm;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Extensions.DependencyInjection;
using RustPlusApi.Fcm.Interfaces;

#pragma warning disable IDE0130 // Namespace does not match folder structure — MS convention for DI extension methods
namespace Microsoft.Extensions.DependencyInjection;
#pragma warning restore IDE0130

/// <summary>Registers <see cref="IRustPlusFcm"/> listeners and the <see cref="IRustPlusFcmFactory"/> into a service collection.</summary>
public static class RustPlusFcmServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="IRustPlusFcmFactory"/> creating caller-owned
    /// <see cref="IRustPlusFcm"/> listeners for credentials acquired at runtime. The host's
    /// <see cref="ILoggerFactory"/> (when registered) and the configured
    /// <see cref="RustPlusFcmSocketOptions"/> are wired into every listener.
    /// </summary>
    /// <remarks>No-op if an <see cref="IRustPlusFcmFactory"/> is already registered (first registration wins);
    /// <paramref name="configureOptions"/> delegates always compose regardless.</remarks>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configureOptions">Optional tuning applied to <see cref="RustPlusFcmSocketOptions"/>.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddRustPlusFcmFactory(
        this IServiceCollection services,
        Action<RustPlusFcmSocketOptions>? configureOptions = null)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.AddOptions();
        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }

        services.TryAddSingleton<IRustPlusFcmFactory>(static sp => new RustPlusFcmFactory(
            sp.GetService<ILoggerFactory>(),
            sp.GetRequiredService<IOptions<RustPlusFcmSocketOptions>>()));
        return services;
    }

    /// <summary>
    /// Registers a single configured <see cref="IRustPlusFcm"/> listener as a container-disposed
    /// singleton with its own fresh persistent-ID list. The listener is not connected; call
    /// <c>ConnectAsync</c> when ready.
    /// </summary>
    /// <remarks>No-op if an <see cref="IRustPlusFcm"/> is already registered (first registration wins);
    /// <paramref name="configureOptions"/> delegates always compose regardless.
    /// FCM listeners are single-connection: the registered singleton cannot reconnect after its
    /// connection drops. For reconnect scenarios, use <see cref="AddRustPlusFcmFactory"/> and create
    /// a new listener per connection attempt.</remarks>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="credentials">The FCM credentials to authenticate with.</param>
    /// <param name="configureOptions">Optional tuning applied to <see cref="RustPlusFcmSocketOptions"/>.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="credentials"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddRustPlusFcm(
        this IServiceCollection services,
        Credentials credentials,
        Action<RustPlusFcmSocketOptions>? configureOptions = null)
    {
        if (credentials is null)
        {
            throw new ArgumentNullException(nameof(credentials));
        }

        return services.AddRustPlusFcm(_ => credentials, configureOptions);
    }

    /// <summary>
    /// Registers a single configured <see cref="IRustPlusFcm"/> listener as a container-disposed
    /// singleton, resolving its <see cref="Credentials"/> from the provider when first requested.
    /// </summary>
    /// <remarks>No-op if an <see cref="IRustPlusFcm"/> is already registered (first registration wins);
    /// <paramref name="configureOptions"/> delegates always compose regardless.
    /// FCM listeners are single-connection: the registered singleton cannot reconnect after its
    /// connection drops. For reconnect scenarios, use <see cref="AddRustPlusFcmFactory"/> and create
    /// a new listener per connection attempt.</remarks>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="credentialsFactory">Produces the credentials from the built provider.</param>
    /// <param name="configureOptions">Optional tuning applied to <see cref="RustPlusFcmSocketOptions"/>.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="credentialsFactory"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddRustPlusFcm(
        this IServiceCollection services,
        Func<IServiceProvider, Credentials> credentialsFactory,
        Action<RustPlusFcmSocketOptions>? configureOptions = null)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (credentialsFactory is null)
        {
            throw new ArgumentNullException(nameof(credentialsFactory));
        }

        services.AddOptions();
        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }

        services.TryAddSingleton<IRustPlusFcm>(sp => new RustPlusFcm(
            credentialsFactory(sp),
            [],
            sp.GetRequiredService<IOptions<RustPlusFcmSocketOptions>>().Value,
            sp.GetService<ILoggerFactory>()));
        return services;
    }
}
