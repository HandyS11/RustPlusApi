using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustPlusApi;
using RustPlusApi.Extensions.DependencyInjection;
using RustPlusApi.Interfaces;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers <see cref="IRustPlus"/> clients and the <see cref="IRustPlusFactory"/> into a service collection.</summary>
public static class RustPlusServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="IRustPlusFactory"/> creating caller-owned <see cref="IRustPlus"/>
    /// clients for connections known only at runtime. The host's <see cref="ILoggerFactory"/> (when
    /// registered) and the configured <see cref="RustPlusSocketOptions"/> are wired into every client.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configureOptions">Optional tuning applied to <see cref="RustPlusSocketOptions"/>.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddRustPlusFactory(
        this IServiceCollection services,
        Action<RustPlusSocketOptions>? configureOptions = null)
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

        services.TryAddSingleton<IRustPlusFactory>(static sp => new RustPlusFactory(
            sp.GetService<ILoggerFactory>(),
            sp.GetRequiredService<IOptions<RustPlusSocketOptions>>()));
        return services;
    }

    /// <summary>
    /// Registers a single configured <see cref="IRustPlus"/> client as a container-disposed singleton.
    /// The client is not connected; call <c>ConnectAsync</c> when ready.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="connection">The server endpoint and player credentials the client connects as.</param>
    /// <param name="configureOptions">Optional tuning applied to <see cref="RustPlusSocketOptions"/>.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="connection"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddRustPlus(
        this IServiceCollection services,
        RustPlusConnection connection,
        Action<RustPlusSocketOptions>? configureOptions = null)
    {
        if (connection is null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        return services.AddRustPlus(_ => connection, configureOptions);
    }

    /// <summary>
    /// Registers a single configured <see cref="IRustPlus"/> client as a container-disposed singleton,
    /// binding its <see cref="RustPlusConnection"/> from <paramref name="connectionSection"/>
    /// (keys: <c>Server</c>, <c>Port</c>, <c>PlayerId</c>, <c>PlayerToken</c>, optional <c>UseFacepunchProxy</c>).
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="connectionSection">The configuration section the connection is bound from.</param>
    /// <param name="configureOptions">Optional tuning applied to <see cref="RustPlusSocketOptions"/>.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="connectionSection"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddRustPlus(
        this IServiceCollection services,
        IConfiguration connectionSection,
        Action<RustPlusSocketOptions>? configureOptions = null)
    {
        if (connectionSection is null)
        {
            throw new ArgumentNullException(nameof(connectionSection));
        }

        return services.AddRustPlus(
            _ => connectionSection.Get<RustPlusConnection>()
                 ?? throw new InvalidOperationException(
                     "The configuration section could not be bound to a RustPlusConnection — is the section empty?"),
            configureOptions);
    }

    /// <summary>
    /// Registers a single configured <see cref="IRustPlus"/> client as a container-disposed singleton,
    /// resolving its <see cref="RustPlusConnection"/> from the provider when first requested.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="connectionFactory">Produces the connection from the built provider.</param>
    /// <param name="configureOptions">Optional tuning applied to <see cref="RustPlusSocketOptions"/>.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="connectionFactory"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddRustPlus(
        this IServiceCollection services,
        Func<IServiceProvider, RustPlusConnection> connectionFactory,
        Action<RustPlusSocketOptions>? configureOptions = null)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (connectionFactory is null)
        {
            throw new ArgumentNullException(nameof(connectionFactory));
        }

        services.AddOptions();
        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }

        services.TryAddSingleton<IRustPlus>(sp => new RustPlus(
            connectionFactory(sp),
            sp.GetRequiredService<IOptions<RustPlusSocketOptions>>().Value,
            sp.GetService<ILoggerFactory>()));
        return services;
    }
}
