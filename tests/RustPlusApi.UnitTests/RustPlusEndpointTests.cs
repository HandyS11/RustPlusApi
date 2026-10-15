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

        Assert.Equal(new Uri("ws://12.34.56.78:28082"), RustPlusEndpoint.GetUri(connection));
    }

    [Fact]
    public void GetUri_Proxy_UsesLegacyGamePath()
    {
        var connection = new RustPlusConnection("12.34.56.78", 28082, 1, 2, UseFacepunchProxy: true);

        Assert.Equal(new Uri("wss://companion-rust.facepunch.com/game/12.34.56.78/28082"),
            RustPlusEndpoint.GetUri(connection));
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
            RustPlusEndpoint.GetUri(connection));
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
    public void GetSubProtocols_ServerIdWithoutToken_IsEmpty(string? authToken)
    {
        var connection = new RustPlusConnection("h", 1, 1, 2)
        {
            ServerId = ServerId, AuthToken = authToken
        };

        Assert.Empty(RustPlusEndpoint.GetSubProtocols(connection));
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
