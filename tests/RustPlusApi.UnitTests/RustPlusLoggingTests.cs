using System;
using Microsoft.Extensions.Logging;
using RustPlusContracts;
using Xunit;

namespace RustPlusApi.UnitTests;

public class RustPlusLoggingTests
{
    private static RustPlusConnection AnyConnection() => new("127.0.0.1", 1, 1UL, 1);

    [Fact]
    public void Constructor_WithNoOptions_DoesNotThrow()
    {
        using var client = new RustPlus(AnyConnection());
        Assert.False(client.IsConnected);
    }

    [Fact]
    public void Constructor_WithOptionsButNoFactory_DoesNotThrow()
    {
        using var client = new RustPlus(AnyConnection(), new RustPlusSocketOptions());
        Assert.False(client.IsConnected);
    }

    [Fact]
    public void Constructor_WithLoggerFactory_DoesNotThrow()
    {
        var factory = new SpyLoggerFactory();
        using var client = new RustPlus(AnyConnection(), new RustPlusSocketOptions { LoggerFactory = factory });
        Assert.False(client.IsConnected);
    }

    [Fact]
    public void UnknownBroadcast_LogsWarning()
    {
        var factory = new SpyLoggerFactory();
        using var client = new TestableRustPlus(new RustPlusSocketOptions { LoggerFactory = factory });

        client.InvokeParseNotification(new AppBroadcast());

        Assert.Single(factory.Logger.Entries, e =>
            e.Level == LogLevel.Warning && e.Message.Contains("Unknown broadcast", StringComparison.Ordinal));
    }

    /// <summary>Exposes the protected ParseNotification so the unknown-broadcast path can be driven.</summary>
    /// <param name="options">Socket options to forward to the base constructor.</param>
    private sealed class TestableRustPlus(RustPlusSocketOptions options)
        : RustPlus(new RustPlusConnection("127.0.0.1", 1, 1UL, 1), options)
    {
        public void InvokeParseNotification(AppBroadcast broadcast) => ParseNotification(broadcast);
    }
}
