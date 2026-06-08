using RustPlusApi;
using Xunit;

namespace RustPlusApi.Tests.Unit;

/// <summary>Covers the null-broadcast guard in <see cref="RustPlus.ParseNotification"/>.</summary>
public class RustPlusParseNotificationTests
{
    /// <summary>Exposes the protected <c>ParseNotification</c> for direct unit testing.</summary>
    private sealed class TestRustPlus() : RustPlus("127.0.0.1", 1, 1, 1)
    {
        public void Feed(RustPlusContracts.AppBroadcast? b) => ParseNotification(b);
    }

    [Fact]
    public void ParseNotification_NullBroadcast_DoesNothing()
    {
        using var sut = new TestRustPlus();

        var ex = Record.Exception(() => sut.Feed(null));

        Assert.Null(ex);
    }
}
