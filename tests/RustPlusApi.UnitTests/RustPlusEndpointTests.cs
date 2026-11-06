using System;
using Xunit;

namespace RustPlusApi.UnitTests;

public class RustPlusEndpointTests
{
    private static readonly Guid ServerId = new("089941d9-064e-4628-9c60-eaeb01a943e8");

    [Fact]
    public void GetUri_Direct_UsesServerAndPort()
    {
        var connection = new DirectConnection("12.34.56.78", 28082, 1, 2);

        Assert.Equal(new Uri("ws://12.34.56.78:28082"),
            RustPlusEndpoint.GetUri(connection, RustPlusEndpoint.CompanionBaseUri));
    }

    [Fact]
    public void GetUri_Companion_UsesCompanionSocket()
    {
        var connection = new CompanionConnection(ServerId, "token", 1, 2);

        Assert.Equal(new Uri("wss://companion-rust.facepunch.com/socket/089941d9-064e-4628-9c60-eaeb01a943e8"),
            RustPlusEndpoint.GetUri(connection, RustPlusEndpoint.CompanionBaseUri));
    }

    [Fact]
    public void GetUri_Companion_ResolvesAgainstTheGivenBase()
    {
        var connection = new CompanionConnection(ServerId, "token", 1, 2);

        Assert.Equal(new Uri("ws://127.0.0.1:5000/socket/089941d9-064e-4628-9c60-eaeb01a943e8"),
            RustPlusEndpoint.GetUri(connection, new Uri("ws://127.0.0.1:5000/")));
    }

    [Fact]
    public void GetSubProtocols_Companion_AnnouncesBase64UrlToken()
    {
        var connection = new CompanionConnection(ServerId, "a?b>", 1, 2);

        // "a?b>" is YT9iPg== in standard base64: '+', '/' and padding must not appear in a protocol token.
        Assert.Equal(["rustplus.auth.v1", "YT9iPg"], RustPlusEndpoint.GetSubProtocols(connection));
    }

    [Fact]
    public void GetSubProtocols_Direct_IsEmpty()
    {
        // The token is only for the companion socket; a game server's app port is offered nothing.
        Assert.Empty(RustPlusEndpoint.GetSubProtocols(new DirectConnection("h", 1, 1, 2)));
    }

    [Fact]
    public void UnsupportedConnectionType_IsRejected_WhenTheClientIsBuilt()
    {
        // Nothing outside the library can normally derive from RustPlusConnection, but the record's
        // protected copy constructor leaves a way; such a type must fail loudly, not pick a route.
        var connection = new UnknownConnection();

        var uri = Assert.Throws<ArgumentException>(() =>
            RustPlusEndpoint.GetUri(connection, RustPlusEndpoint.CompanionBaseUri));
        var subProtocols = Assert.Throws<ArgumentException>(() => RustPlusEndpoint.GetSubProtocols(connection));
        var client = Assert.Throws<ArgumentException>(() => new RustPlus(connection));

        Assert.All([uri, subProtocols, client], ex =>
        {
            Assert.Equal("connection", ex.ParamName);
            Assert.StartsWith("UnknownConnection is not a supported connection", ex.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void RustPlus_CompanionBaseUri_DefaultsToFacepunch()
    {
        using var client = new RustPlus(new DirectConnection("h", 1, 1, 2));

        Assert.Equal(new Uri("wss://companion-rust.facepunch.com/"), client.CompanionBaseUriForTests);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("f", "Zg")]
    [InlineData("fo", "Zm8")]
    [InlineData("foo", "Zm9v")]
    [InlineData("ÿþ", "w7_Dvg")]
    public void ToBase64Url_EncodesUtf8_WithoutPaddingOrUnsafeCharacters(string value, string expected) =>
        Assert.Equal(expected, RustPlusEndpoint.ToBase64Url(value));

    private sealed record UnknownConnection() : RustPlusConnection(1UL, 2);
}
