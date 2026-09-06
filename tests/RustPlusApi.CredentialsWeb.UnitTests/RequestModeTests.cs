using Microsoft.AspNetCore.Http;
using RustPlusApi.CredentialsWeb.Endpoints;
using System.Net;
using Xunit;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class RequestModeTests
{
    private static DefaultHttpContext Context(string? remoteIp, string host)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteIp is null ? null : IPAddress.Parse(remoteIp);
        context.Request.Host = new HostString(host);
        return context;
    }


    [Theory]
    [InlineData("127.0.0.1", "localhost:8080")]
    [InlineData("127.0.0.5", "127.0.0.1:8080")]
    [InlineData("::1", "[::1]:8080")]
    [InlineData("::ffff:127.0.0.1", "localhost")]
    [InlineData("127.0.0.1", "app.localhost:8080")]
    public void IsLocal_True_WhenBothTheConnectionAndTheHostAreLoopback(string remoteIp, string host) =>
        Assert.True(RequestMode.IsLocal(Context(remoteIp, host)));

    [Theory]
    // The documented `docker run -p 127.0.0.1:8080:8080`: the port is published on the host's
    // loopback, but the container is reached through the bridge, so the app sees the gateway and
    // never loopback. Treating this as remote put the app's own headline command into the paste
    // flow and then answered its pairing request with "run the app yourself".
    [InlineData("172.17.0.1", "localhost:8080")]
    // Docker Desktop's gateway, and rootless Podman's slirp4netns address.
    [InlineData("192.168.65.1", "localhost:8080")]
    [InlineData("10.0.2.100", "localhost")]
    // An IPv6-only container network reaches the app from unique-local space.
    [InlineData("fd00::1", "[::1]:8080")]
    public void IsLocal_True_ForANonRoutableConnectionNamingALoopbackHost(string remoteIp, string host) =>
        Assert.True(RequestMode.IsLocal(Context(remoteIp, host)));

    [Theory]
    // A reverse proxy on the same host: the connection looks loopback, the Host does not.
    [InlineData("127.0.0.1", "creds.example.org")]
    // A forged Host header from a remote caller.
    [InlineData("203.0.113.7", "localhost:8080")]
    [InlineData("203.0.113.7", "creds.example.org")]
    // Carrier-grade NAT space is not private space: it is routed by someone else's network, so a
    // caller there is not on this machine or its LAN in any sense the local behaviour should cover.
    [InlineData("100.64.0.1", "localhost:8080")]
    // A container-network connection is still remote when the Host is not loopback — the widened
    // connection half never stands alone.
    [InlineData("172.17.0.1", "creds.example.org")]
    public void IsLocal_False_UnlessBothHalvesHold(string remoteIp, string host) =>
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

    [Fact]
    public void IsLoopbackAddress_StaysStrict_ForAPrivateAddress() =>
        Assert.False(RequestMode.IsLoopbackAddress(IPAddress.Parse("172.17.0.1")));

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.10")]
    [InlineData("169.254.10.1")]
    [InlineData("fe80::1")]
    [InlineData("fd12:3456::1")]
    // Kestrel reports the mapped form on a dual-stack socket, private addresses included.
    [InlineData("::ffff:172.17.0.1")]
    public void IsLocalConnection_True_ForNonRoutableSpace(string address) =>
        Assert.True(RequestMode.IsLocalConnection(IPAddress.Parse(address)));

    [Theory]
    [InlineData("203.0.113.7")]
    // The two neighbours of 172.16/12, which is the one private range that is not a whole octet.
    [InlineData("172.15.0.1")]
    [InlineData("172.32.0.1")]
    [InlineData("100.64.0.1")]
    [InlineData("2001:db8::1")]
    public void IsLocalConnection_False_ForARoutableAddress(string address) =>
        Assert.False(RequestMode.IsLocalConnection(IPAddress.Parse(address)));

    [Fact]
    public void IsLocalConnection_False_WhenThereIsNoConnectionAddress() =>
        Assert.False(RequestMode.IsLocalConnection(null));
}
