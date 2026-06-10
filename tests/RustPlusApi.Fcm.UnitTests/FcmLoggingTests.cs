using RustPlusApi.Fcm.Data;
using Xunit;

namespace RustPlusApi.Fcm.UnitTests;

public class FcmLoggingTests
{
    private static Credentials AnyCredentials() =>
        new() { Gcm = new Gcm { AndroidId = 1, SecurityToken = 1 } };

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
        using var socket = new TestSocket(AnyCredentials(),
            new RustPlusFcmSocketOptions { LoggerFactory = factory });
        Assert.NotNull(socket);
    }

    /// <summary>Concrete subclass: <see cref="RustPlusFcmSocket"/> is abstract.</summary>
    /// <param name="credentials">The FCM credentials.</param>
    /// <param name="options">Optional socket options.</param>
    private sealed class TestSocket(Credentials credentials, RustPlusFcmSocketOptions? options)
        : RustPlusFcmSocket(credentials, options: options);
}
