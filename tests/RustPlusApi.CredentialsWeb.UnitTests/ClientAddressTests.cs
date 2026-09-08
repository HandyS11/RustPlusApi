using Microsoft.AspNetCore.Http;
using RustPlusApi.CredentialsWeb.Endpoints;
using System.Net;
using Xunit;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class ClientAddressTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("203.0.113.7")]
    [InlineData("::1")]
    public void Of_ReturnsTheConnectionAddress(string remoteIp)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);

        Assert.Equal(IPAddress.Parse(remoteIp).ToString(), ClientAddress.Of(context));
    }

    /// <summary>A request can arrive with no connection address — that is what <c>TestServer</c>
    /// reports, and Kestrel does the same over a Unix socket. Per-IP accounting then has to keep
    /// happening against a single shared bucket rather than disappearing, so the fallback constant
    /// is asserted rather than left to the null-conditional's untested arm.</summary>
    [Fact]
    public void Of_FallsBackToASharedBucket_WhenThereIsNoConnectionAddress()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = null;

        Assert.Equal("unknown", ClientAddress.Of(context));
    }
}
