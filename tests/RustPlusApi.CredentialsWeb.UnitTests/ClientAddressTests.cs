using Microsoft.AspNetCore.Http;
using RustPlusApi.CredentialsWeb.Endpoints;
using System.Net;
using Xunit;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class ClientAddressTests
{
    private static string KeyOf(string remoteIp)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        return ClientAddress.Of(context);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("203.0.113.7")]
    public void Of_ReturnsAnIpv4ConnectionAddressUnchanged(string remoteIp) =>
        Assert.Equal(remoteIp, KeyOf(remoteIp));

    [Fact]
    public void Of_MapsAnIpv4MappedAddressToItsIpv4Form() =>
        Assert.Equal("203.0.113.7", KeyOf("::ffff:203.0.113.7"));

    /// <summary>One IPv6 subscriber or VPS normally holds a whole /64. Keying per-address caps on
    /// the full /128 would hand that one principal 2^64 buckets, enough to fill the global session
    /// cap on its own.</summary>
    /// <param name="remoteIp">An address inside 2001:db8:abcd:12::/64.</param>
    [Theory]
    [InlineData("2001:db8:abcd:12::1")]
    [InlineData("2001:db8:abcd:12::c8")]
    [InlineData("2001:db8:abcd:12:ffff:ffff:ffff:ffff")]
    public void Of_AggregatesAnIpv6AddressToItsSlash64(string remoteIp) =>
        Assert.Equal("2001:db8:abcd:12::/64", KeyOf(remoteIp));

    [Fact]
    public void Of_KeepsDifferentIpv6Slash64sApart() =>
        Assert.NotEqual(KeyOf("2001:db8:abcd:12::1"), KeyOf("2001:db8:abcd:13::1"));

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
