using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace RustPlusApi.Extensions.DependencyInjection.UnitTests;

public class RustPlusFactoryTests
{
    private static RustPlusConnection AnyConnection() => new("127.0.0.1", 28082, 1UL, 1);

    [Fact]
    public void AddRustPlusFactory_RegistersASingletonFactory()
    {
        var services = new ServiceCollection();

        services.AddRustPlusFactory();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IRustPlusFactory));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddRustPlusFactory_CalledTwice_KeepsASingleRegistration()
    {
        var services = new ServiceCollection();

        services.AddRustPlusFactory();
        services.AddRustPlusFactory();

        Assert.Single(services, d => d.ServiceType == typeof(IRustPlusFactory));
    }

    [Fact]
    public void AddRustPlusFactory_NullServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddRustPlusFactory());
    }

    [Fact]
    public async Task Create_ReturnsDistinctCallerOwnedClients()
    {
        var services = new ServiceCollection();
        services.AddRustPlusFactory();
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IRustPlusFactory>();

        var first = factory.Create(AnyConnection());
        var second = factory.Create(AnyConnection());

        Assert.NotSame(first, second);

        // Caller-owned: disposing one leaves the other usable.
        ((IDisposable)first).Dispose();
        Assert.False(second.IsConnected);
        ((IDisposable)second).Dispose();
    }

    [Fact]
    public async Task Create_WiresTheContainerLoggerFactory()
    {
        var recorder = new RecordingLoggerFactory();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(recorder);
        services.AddRustPlusFactory();
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IRustPlusFactory>();

        using var client = (IDisposable)factory.Create(AnyConnection());

        Assert.Contains("RustPlusApi.RustPlusSocket", recorder.Categories);
    }

    [Fact]
    public async Task Create_AppliesConfiguredOptions_AndWorksWithoutLogging()
    {
        var services = new ServiceCollection();
        services.AddRustPlusFactory(o => o.RequestTimeout = TimeSpan.FromSeconds(3));
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IRustPlusFactory>();

        using var client = (IDisposable)factory.Create(AnyConnection());

        Assert.NotNull(client);
    }

    [Fact]
    public async Task Create_WithNullConnection_Throws()
    {
        var services = new ServiceCollection();
        services.AddRustPlusFactory();
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IRustPlusFactory>();

        Assert.Throws<ArgumentNullException>(() => factory.Create(null!));
    }
}
