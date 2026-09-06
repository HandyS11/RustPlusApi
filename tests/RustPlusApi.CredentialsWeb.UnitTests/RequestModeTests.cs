using Microsoft.AspNetCore.Http;
using RustPlusApi.CredentialsWeb.Endpoints;
using System.Net;
using Xunit;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class RequestModeTests
{
    // CA1859: TestServer doesn't provide a concrete context subclass, so we return the abstract type for compatibility.
#pragma warning disable CA1859
    private static HttpContext Context(string? remoteIp, string host)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteIp is null ? null : IPAddress.Parse(remoteIp);
        context.Request.Host = new HostString(host);
        return context;
    }
#pragma warning restore CA1859


    [Theory]
    [InlineData("127.0.0.1", "localhost:8080")]
    [InlineData("127.0.0.5", "127.0.0.1:8080")]
    [InlineData("::1", "[::1]:8080")]
    [InlineData("::ffff:127.0.0.1", "localhost")]
    [InlineData("127.0.0.1", "app.localhost:8080")]
    public void IsLocal_True_WhenBothTheConnectionAndTheHostAreLoopback(string remoteIp, string host) =>
        Assert.True(RequestMode.IsLocal(Context(remoteIp, host)));

    [Theory]
    // A reverse proxy on the same host: the connection looks loopback, the Host does not.
    [InlineData("127.0.0.1", "creds.example.org")]
    // A forged Host header from a remote caller.
    [InlineData("203.0.113.7", "localhost:8080")]
    [InlineData("203.0.113.7", "creds.example.org")]
    public void IsLocal_False_UnlessBothHalvesAreLoopback(string remoteIp, string host) =>
        Assert.False(RequestMode.IsLocal(Context(remoteIp, host)));

    [Fact]
    public void IsLocal_False_WhenThereIsNoConnectionAddress() =>
        Assert.False(RequestMode.IsLocal(Context(null, "localhost")));

    [Fact]
    public void IsLoopbackHost_False_ForABlankHost() =>
        Assert.False(RequestMode.IsLoopbackHost("   "));

    [Fact]
    public void IsLoopbackHost_False_ForAHostThatMerelyEndsInTheWordLocalhost() =>
        Assert.False(RequestMode.IsLoopbackHost("notlocalhost"));

    [Fact]
    public void IsLoopbackAddress_True_ForTheIPv4MappedFormKestrelReportsOnADualStackSocket() =>
        Assert.True(RequestMode.IsLoopbackAddress(IPAddress.Parse("::ffff:127.0.0.1")));
}
