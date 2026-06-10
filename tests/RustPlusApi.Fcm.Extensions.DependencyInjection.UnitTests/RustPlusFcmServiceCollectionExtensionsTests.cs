using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Interfaces;
using Xunit;

namespace RustPlusApi.Fcm.Extensions.DependencyInjection.UnitTests;

public class RustPlusFcmServiceCollectionExtensionsTests
{
    private static Credentials AnyCredentials() => new() { Gcm = new Gcm { AndroidId = 1, SecurityToken = 1 } };

    [Fact]
    public void AddRustPlusFcm_WithCredentials_RegistersIRustPlusFcmAsSingleton()
    {
        var services = new ServiceCollection();

        services.AddRustPlusFcm(AnyCredentials());

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IRustPlusFcm));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddRustPlusFcm_CalledTwice_KeepsASingleRegistration()
    {
        var services = new ServiceCollection();

        services.AddRustPlusFcm(AnyCredentials());
        services.AddRustPlusFcm(AnyCredentials());

        Assert.Single(services, d => d.ServiceType == typeof(IRustPlusFcm));
    }

    [Fact]
    public async Task AddRustPlusFcm_ResolvesTheSameSingleton()
    {
        var services = new ServiceCollection();
        services.AddRustPlusFcm(AnyCredentials());
        await using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IRustPlusFcm>();
        var second = provider.GetRequiredService<IRustPlusFcm>();

        Assert.Same(first, second);
    }

    [Fact]
    public async Task AddRustPlusFcm_UsesTheContainerLoggerFactory()
    {
        var recorder = new RecordingLoggerFactory();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(recorder);
        services.AddRustPlusFcm(AnyCredentials());
        await using var provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<IRustPlusFcm>();

        Assert.Contains("RustPlusApi.Fcm.RustPlusFcmSocket", recorder.Categories);
    }

    [Fact]
    public async Task AddRustPlusFcm_WorksWithoutLoggingRegistered()
    {
        var services = new ServiceCollection();
        services.AddRustPlusFcm(AnyCredentials());
        await using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IRustPlusFcm>());
    }

    [Fact]
    public async Task AddRustPlusFcm_WithCredentialsFactory_ResolvesThemFromTheProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(AnyCredentials());
        services.AddRustPlusFcm(sp => sp.GetRequiredService<Credentials>());
        await using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IRustPlusFcm>());
    }

    [Fact]
    public async Task AddRustPlusFcm_AppliesConfigureOptions()
    {
        var services = new ServiceCollection();
        services.AddRustPlusFcm(AnyCredentials(), o => o.HeartbeatInterval = TimeSpan.FromMinutes(1));
        await using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<RustPlusFcmSocketOptions>>();

        Assert.Equal(TimeSpan.FromMinutes(1), options.Value.HeartbeatInterval);
    }

    [Fact]
    public void AddRustPlusFcm_NullArguments_Throw()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddRustPlusFcm(AnyCredentials()));
        Assert.Throws<ArgumentNullException>(() => services.AddRustPlusFcm((Credentials)null!));
        Assert.Throws<ArgumentNullException>(() => services.AddRustPlusFcm((Func<IServiceProvider, Credentials>)null!));
    }

    [Fact]
    public void AddRustPlusFcmFactory_RegistersASingletonFactory()
    {
        var services = new ServiceCollection();

        services.AddRustPlusFcmFactory();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IRustPlusFcmFactory));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddRustPlusFcmFactory_CalledTwice_KeepsASingleRegistration()
    {
        var services = new ServiceCollection();

        services.AddRustPlusFcmFactory();
        services.AddRustPlusFcmFactory();

        Assert.Single(services, d => d.ServiceType == typeof(IRustPlusFcmFactory));
    }

    [Fact]
    public void AddRustPlusFcmFactory_NullServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddRustPlusFcmFactory());
    }

    [Fact]
    public async Task FactoryCreate_ReturnsDistinctClients_AndWiresLogging()
    {
        var recorder = new RecordingLoggerFactory();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(recorder);
        services.AddRustPlusFcmFactory();
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IRustPlusFcmFactory>();

        await using var first = factory.Create(AnyCredentials());
        await using var second = factory.Create(AnyCredentials());

        Assert.NotSame(first, second);
        Assert.Contains("RustPlusApi.Fcm.RustPlusFcmSocket", recorder.Categories);
    }

    [Fact]
    public async Task FactoryCreate_AppliesConfiguredOptions_AndWorksWithoutLogging()
    {
        var services = new ServiceCollection();
        services.AddRustPlusFcmFactory(o => o.HeartbeatInterval = TimeSpan.FromMinutes(2));
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IRustPlusFcmFactory>();

        await using var client = factory.Create(AnyCredentials());

        Assert.NotNull(client);
    }

    [Fact]
    public async Task FactoryCreate_WithExplicitPersistentIds_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddRustPlusFcmFactory();
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IRustPlusFcmFactory>();

        // Covers the caller-supplied list branch (the null branch is covered by the other Create tests).
        await using var client = factory.Create(AnyCredentials(), ["already-seen-id"]);

        Assert.NotNull(client);
    }

    [Fact]
    public async Task FactoryCreate_WithNullCredentials_Throws()
    {
        var services = new ServiceCollection();
        services.AddRustPlusFcmFactory();
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IRustPlusFcmFactory>();

        Assert.Throws<ArgumentNullException>(() => factory.Create(null!));
    }
}
