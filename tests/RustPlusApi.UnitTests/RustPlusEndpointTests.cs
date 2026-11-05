using System;
using Xunit;

namespace RustPlusApi.UnitTests;

public class RustPlusEndpointTests
{
    private static readonly Guid ServerId = new("089941d9-064e-4628-9c60-eaeb01a943e8");

    [Fact]
    public void GetUri_Direct_UsesServerAndPort()
    {
        var connection = new RustPlusConnection("12.34.56.78", 28082, 1, 2);

        Assert.Equal(new Uri("ws://12.34.56.78:28082"),
            RustPlusEndpoint.GetUri(connection, RustPlusEndpoint.CompanionBaseUri));
    }

    [Fact]
    public void GetUri_Proxy_UsesLegacyGamePath()
    {
        var connection = new RustPlusConnection("12.34.56.78", 28082, 1, 2, UseFacepunchProxy: true);

        Assert.Equal(new Uri("wss://companion-rust.facepunch.com/game/12.34.56.78/28082"),
            RustPlusEndpoint.GetUri(connection, RustPlusEndpoint.CompanionBaseUri));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetUri_ServerId_UsesCompanionSocket_RegardlessOfProxyFlag(bool useFacepunchProxy)
    {
        var connection = new RustPlusConnection("12.34.56.78", 28082, 1, 2, useFacepunchProxy)
        {
            ServerId = ServerId
        };

        Assert.Equal(new Uri("wss://companion-rust.facepunch.com/socket/089941d9-064e-4628-9c60-eaeb01a943e8"),
            RustPlusEndpoint.GetUri(connection, RustPlusEndpoint.CompanionBaseUri));
    }

    [Fact]
    public void GetSubProtocols_ServerIdAndToken_AnnouncesBase64UrlToken()
    {
        var connection = new RustPlusConnection("h", 1, 1, 2)
        {
            ServerId = ServerId, AuthToken = "a?b>"
        };

        // "a?b>" is YT9iPg== in standard base64: '+', '/' and padding must not appear in a protocol token.
        Assert.Equal(["rustplus.auth.v1", "YT9iPg"], RustPlusEndpoint.GetSubProtocols(connection));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetSubProtocols_ServerIdWithoutToken_Throws(string? authToken)
    {
        // The companion socket refuses an unauthenticated handshake, so a missing token must fail
        // loudly here rather than surface later as a bare 403 from the WebSocket connect.
        var connection = new RustPlusConnection("h", 1, 1, 2)
        {
            ServerId = ServerId, AuthToken = authToken
        };

        var ex = Assert.Throws<ArgumentException>(() => RustPlusEndpoint.GetSubProtocols(connection));
        Assert.Equal("connection", ex.ParamName);
        Assert.Contains(nameof(RustPlusConnection.AuthToken), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSubProtocols_EmptyServerId_Throws()
    {
        // ServerPairing.ServerId is Guid.Empty when a pairing push carried no id; passing it through
        // would dial /socket/00000000-… and fail with a 404 that names nothing.
        var connection = new RustPlusConnection("h", 1, 1, 2)
        {
            ServerId = Guid.Empty, AuthToken = "token"
        };

        var ex = Assert.Throws<ArgumentException>(() => RustPlusEndpoint.GetSubProtocols(connection));
        Assert.Equal("connection", ex.ParamName);
        Assert.Contains(nameof(RustPlusConnection.ServerId), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RustPlus_Constructor_RejectsServerIdWithoutAuthToken()
    {
        // Validated when the client is built, not when it first connects.
        var connection = new RustPlusConnection("h", 1, 1, 2)
        {
            ServerId = ServerId
        };

        var ex = Assert.Throws<ArgumentException>(() => new RustPlus(connection));
        Assert.Equal("connection", ex.ParamName);
    }

    [Fact]
    public void RustPlus_CompanionBaseUri_DefaultsToFacepunch()
    {
        using var client = new RustPlus(new RustPlusConnection("h", 1, 1, 2));

        Assert.Equal(new Uri("wss://companion-rust.facepunch.com/"), client.CompanionBaseUriForTests);
    }

    [Fact]
    public void GetUri_CompanionRoutes_ResolveAgainstTheGivenBase()
    {
        var companionBase = new Uri("ws://127.0.0.1:5000/");
        var socket = new RustPlusConnection("12.34.56.78", 28082, 1, 2)
        {
            ServerId = ServerId
        };
        var proxy = new RustPlusConnection("12.34.56.78", 28082, 1, 2, UseFacepunchProxy: true);

        Assert.Equal(new Uri("ws://127.0.0.1:5000/socket/089941d9-064e-4628-9c60-eaeb01a943e8"),
            RustPlusEndpoint.GetUri(socket, companionBase));
        Assert.Equal(new Uri("ws://127.0.0.1:5000/game/12.34.56.78/28082"),
            RustPlusEndpoint.GetUri(proxy, companionBase));
    }

    [Fact]
    public void GetSubProtocols_TokenWithoutServerId_IsEmpty()
    {
        // The token is only for the companion socket; never offer it to a game server's app port.
        var connection = new RustPlusConnection("h", 1, 1, 2)
        {
            AuthToken = "token"
        };

        Assert.Empty(RustPlusEndpoint.GetSubProtocols(connection));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("f", "Zg")]
    [InlineData("fo", "Zm8")]
    [InlineData("foo", "Zm9v")]
    [InlineData("ÿþ", "w7_Dvg")]
    public void ToBase64Url_EncodesUtf8_WithoutPaddingOrUnsafeCharacters(string value, string expected) =>
        Assert.Equal(expected, RustPlusEndpoint.ToBase64Url(value));
}
