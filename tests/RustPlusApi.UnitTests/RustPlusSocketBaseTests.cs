using RustPlusApi.Data;
using RustPlusContracts;
using Xunit;

namespace RustPlusApi.UnitTests;

/// <summary>
/// The two <see cref="RustPlusSocket"/> members that no test reaches through <see cref="RustPlus"/>,
/// because <see cref="RustPlus"/> itself is what hides them: the finalizer arm of the dispose
/// pattern, and the default <c>ParseNotification</c> extension point that every shipped derived
/// class overrides. Both need a derived type that does neither, which is what
/// <see cref="BareSocket"/> is.
/// </summary>
public sealed class RustPlusSocketBaseTests
{
    private static RustPlusConnection AnyConnection() => new("localhost", 28082, 76561198000000000, 123456789);

    [Fact]
    public void Dispose_WithDisposingFalse_ReleasesNothingManaged()
    {
        using var socket = new BareSocket(AnyConnection());

        // The finalizer arm: it must return before touching any managed field, and must leave the
        // instance usable so the real Dispose() below still does the managed teardown exactly once.
        socket.InvokeDispose(disposing: false);

        Assert.False(socket.IsConnected);
    }

    [Fact]
    public void ParseNotification_DefaultImplementation_IgnoresTheBroadcast()
    {
        using var socket = new BareSocket(AnyConnection());

        // The base extension point is a deliberate no-op: a derived class that does not override it
        // must not fault the receive loop when a broadcast arrives, including a null one.
        socket.InvokeParseNotification(new AppBroadcast());
        socket.InvokeParseNotification(null);

        Assert.False(socket.IsConnected);
    }

    /// <summary>A <see cref="RustPlusSocket"/> that overrides nothing, exposing the two protected
    /// members under test. It never connects — both paths are offline by construction.</summary>
    /// <param name="connection">Endpoint and credentials; never dialled.</param>
    private sealed class BareSocket(RustPlusConnection connection) : RustPlusSocket(connection)
    {
        internal void InvokeDispose(bool disposing) => Dispose(disposing);

        internal void InvokeParseNotification(AppBroadcast? broadcast) => ParseNotification(broadcast);
    }
}
