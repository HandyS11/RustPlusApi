using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustPlusApi.Interfaces;
using Xunit;

namespace RustPlusApi.Extensions.DependencyInjection.UnitTests;

public class RustPlusServiceCollectionExtensionsTests
{
    private static DirectConnection AnyConnection() => new("127.0.0.1", 28082, 76561198000000000UL, 123456789);

    [Fact]
    public void AddRustPlus_WithConnection_RegistersIRustPlusAsSingleton()
    {
        var services = new ServiceCollection();

        services.AddRustPlus(AnyConnection());

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IRustPlus));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddRustPlus_CalledTwice_KeepsASingleRegistration()
    {
        var services = new ServiceCollection();

        services.AddRustPlus(AnyConnection());
        services.AddRustPlus(AnyConnection());

        Assert.Single(services, d => d.ServiceType == typeof(IRustPlus));
    }

    [Fact]
    public async Task AddRustPlus_ResolvesTheSameUnconnectedSingleton()
    {
        var services = new ServiceCollection();
        services.AddRustPlus(AnyConnection());
        await using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IRustPlus>();
        var second = provider.GetRequiredService<IRustPlus>();

        Assert.Same(first, second);
        Assert.False(first.IsConnected);
    }

    [Fact]
    public async Task AddRustPlus_UsesTheContainerLoggerFactory()
    {
        var recorder = new RecordingLoggerFactory();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(recorder);
        services.AddRustPlus(AnyConnection());
        await using var provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<IRustPlus>();

        Assert.Contains("RustPlusApi.RustPlusSocket", recorder.Categories);
    }

    [Fact]
    public async Task AddRustPlus_WorksWithoutLoggingRegistered()
    {
        var services = new ServiceCollection();
        services.AddRustPlus(AnyConnection());
        await using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IRustPlus>());
    }

    [Fact]
    public async Task AddRustPlus_AppliesConfigureOptions()
    {
        var services = new ServiceCollection();
        services.AddRustPlus(AnyConnection(), o => o.RequestTimeout = TimeSpan.FromSeconds(5));
        await using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<RustPlusSocketOptions>>();

        Assert.Equal(TimeSpan.FromSeconds(5), options.Value.RequestTimeout);
    }

    [Fact]
    public async Task AddRustPlus_WithConfiguration_BindsADirectConnection()
    {
        var section = Section(new Dictionary<string, string?>
        {
            ["Rust:Server"] = "1.2.3.4",
            ["Rust:Port"] = "28082",
            ["Rust:PlayerId"] = "76561198000000000",
            ["Rust:PlayerToken"] = "123456789",
        });

        // No ServerId key: the section describes a direct connection (documents the binding contract)…
        Assert.Equal(
            new DirectConnection("1.2.3.4", 28082, 76561198000000000UL, 123456789),
            RustPlusServiceCollectionExtensions.BindConnection(section));

        // …and the registration resolves a client from it.
        var services = new ServiceCollection();
        services.AddRustPlus(section);
        await using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IRustPlus>());
    }

    [Fact]
    public async Task AddRustPlus_WithConfiguration_BindsACompanionConnection_WhenServerIdIsSet()
    {
        // Server and Port are not needed for the companion socket; leftovers from a direct setup are ignored.
        var section = Section(new Dictionary<string, string?>
        {
            ["Rust:Server"] = "1.2.3.4",
            ["Rust:Port"] = "28082",
            ["Rust:PlayerId"] = "76561198000000000",
            ["Rust:PlayerToken"] = "123456789",
            ["Rust:ServerId"] = "089941d9-064e-4628-9c60-eaeb01a943e8",
            ["Rust:AuthToken"] = "companion-token",
        });

        Assert.Equal(
            new CompanionConnection(new Guid("089941d9-064e-4628-9c60-eaeb01a943e8"), "companion-token",
                76561198000000000UL, 123456789),
            RustPlusServiceCollectionExtensions.BindConnection(section));

        var services = new ServiceCollection();
        services.AddRustPlus(section);
        await using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IRustPlus>());
    }

    [Fact]
    public void BindConnection_ServerIdWithoutAuthToken_NamesTheMissingKey()
    {
        // A ServerId alone must not quietly become a direct connection: the binder refuses and says why.
        var section = Section(new Dictionary<string, string?>
        {
            ["Rust:PlayerId"] = "76561198000000000",
            ["Rust:PlayerToken"] = "123456789",
            ["Rust:ServerId"] = "089941d9-064e-4628-9c60-eaeb01a943e8",
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            RustPlusServiceCollectionExtensions.BindConnection(section));
        Assert.Contains("authToken", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BindConnection_BlankAuthToken_IsRejectedByTheConstructor()
    {
        var section = Section(new Dictionary<string, string?>
        {
            ["Rust:PlayerId"] = "76561198000000000",
            ["Rust:PlayerToken"] = "123456789",
            ["Rust:ServerId"] = "089941d9-064e-4628-9c60-eaeb01a943e8",
            ["Rust:AuthToken"] = "   ",
        });

        var ex = Assert.Throws<ArgumentException>(() => RustPlusServiceCollectionExtensions.BindConnection(section));
        Assert.Equal("authToken", ex.ParamName);
    }

    [Fact]
    public async Task AddRustPlus_WithEmptyConfigurationSection_ThrowsOnResolve()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddRustPlus(config.GetSection("Missing"));
        await using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IRustPlus>());
        Assert.Contains("could not be bound", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddRustPlus_WithPartialConfigurationSection_ThrowsOnResolve()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Rust:Server"] = "1.2.3.4"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddRustPlus(config.GetSection("Rust"));
        await using var provider = services.BuildServiceProvider();

        // The binder itself reports the missing constructor parameter (e.g. 'Port').
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IRustPlus>());
    }

    [Fact]
    public async Task AddRustPlus_WithConnectionFactory_ResolvesItFromTheProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RustPlusConnection>(new DirectConnection("9.9.9.9", 1, 1UL, 1));
        services.AddRustPlus(sp => sp.GetRequiredService<RustPlusConnection>());
        await using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IRustPlus>());
    }

    [Fact]
    public async Task ProviderDisposal_DisposesTheSingletonClient()
    {
        var services = new ServiceCollection();
        services.AddRustPlus(AnyConnection());
        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IRustPlus>();

        await provider.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ConnectAsync());
    }

    [Fact]
    public void AddRustPlus_NullArguments_Throw()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddRustPlus(AnyConnection()));
        Assert.Throws<ArgumentNullException>(() => services.AddRustPlus((RustPlusConnection)null!));
        Assert.Throws<ArgumentNullException>(() => services.AddRustPlus((IConfiguration)null!));
        Assert.Throws<ArgumentNullException>(() =>
            services.AddRustPlus((Func<IServiceProvider, RustPlusConnection>)null!));
    }


    private static IConfigurationSection Section(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection("Rust");
}
