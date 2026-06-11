using System;
using Microsoft.Extensions.Logging;
using RustPlusApi.Fcm.Data;
using Xunit;

namespace RustPlusApi.Fcm.UnitTests;

public class FcmLoggingTests
{
    private static Credentials AnyCredentials() =>
        new()
        {
            Gcm = new Gcm
            {
                AndroidId = 1, SecurityToken = 1
            }
        };

    [Fact]
    public void Constructor_WithNoOptions_DoesNotThrow()
    {
        using var socket = new TestSocket(AnyCredentials(), null);
        Assert.NotNull(socket);
    }

    [Fact]
    public void Constructor_WithOptionsButNoFactory_DoesNotThrow()
    {
        using var socket = new TestSocket(AnyCredentials(), new RustPlusFcmSocketOptions());
        Assert.NotNull(socket);
    }

    [Fact]
    public void Constructor_WithLoggerFactory_DoesNotThrow()
    {
        var factory = new SpyLoggerFactory();
        using var socket = new TestSocket(AnyCredentials(), loggerFactory: factory);
        Assert.NotNull(socket);
    }

    [Fact]
    public void ParseNotification_UnknownChannel_LogsWarning()
    {
        var factory = new SpyLoggerFactory();
        using var fcm = new TestableRustPlusFcm(factory);

        fcm.InvokeParseNotification(new FcmMessage
        {
            Data = new MessageData
            {
                ChannelId = "not-a-known-channel"
            }
        });

        Assert.Single(factory.Logger.Entries, e =>
            e.Level == LogLevel.Warning && e.Message.Contains("Unknown channel", StringComparison.Ordinal));
    }

    /// <summary>Concrete subclass: <see cref="RustPlusFcmSocket"/> is abstract.</summary>
    /// <param name="credentials">The FCM credentials.</param>
    /// <param name="options">Optional socket options.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    private sealed class TestSocket(
        Credentials credentials,
        RustPlusFcmSocketOptions? options = null,
        ILoggerFactory? loggerFactory = null)
        : RustPlusFcmSocket(credentials, options: options, loggerFactory: loggerFactory);

    /// <summary>Exposes the protected ParseNotification so the unknown-channel path can be driven.</summary>
    /// <param name="loggerFactory">The logger factory under test.</param>
    private sealed class TestableRustPlusFcm(ILoggerFactory loggerFactory)
        : RustPlusFcm(new Credentials
        {
            Gcm = new Gcm
            {
                AndroidId = 1, SecurityToken = 1
            }
        }, loggerFactory: loggerFactory)
    {
        public void InvokeParseNotification(FcmMessage message) => ParseNotification(message);
    }
}
