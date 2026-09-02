using System.Diagnostics.CodeAnalysis;
using RustPlusApi.Fcm.Registration;
using RustPlusApi.Fcm.Registration.Steps;
using Xunit;

namespace RustPlusApi.Fcm.Registration.UnitTests;

/// <summary>Offline coverage of the Steam login redirect flow: URL construction, callback
/// parsing, and the loopback listener loop driven without a browser.</summary>
public class SteamLoginServiceTests
{
    [Fact]
    public void BuildLoginUrl_EncodesReturnUrlAsQueryParameter()
    {
        var url = SteamLoginService.BuildLoginUrl("http://localhost:3000/callback/abc123");

        Assert.Equal(
            RegistrationConstants.SteamLoginUrl
            + "?returnUrl=http%3A%2F%2Flocalhost%3A3000%2Fcallback%2Fabc123",
            url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [SuppressMessage("Design", "CA1054:URI parameters should not be strings",
        Justification = "Test parameters use string for testability")]
    public void BuildLoginUrl_BlankReturnUrl_Throws(string? returnUrl) =>
        Assert.Throws<ArgumentException>(() => SteamLoginService.BuildLoginUrl(returnUrl!));

    [Fact]
    public void ParseCallback_ReturnsSteamIdAndToken()
    {
        var uri = new Uri("http://localhost:3000/callback/abc123?steamId=76561198249527954&token=eyJhbGciOi");

        var result = SteamLoginService.ParseCallback(uri);

        Assert.Equal(76561198249527954UL, result.SteamId);
        Assert.Equal("eyJhbGciOi", result.Token);
    }

    [Fact]
    public void ParseCallback_UrlDecodesValuesAndIgnoresExtraParameters()
    {
        var uri = new Uri("http://localhost:3000/callback?extra=1&token=a%2Bb%3Dc&steamId=7&other=x");

        var result = SteamLoginService.ParseCallback(uri);

        Assert.Equal(7UL, result.SteamId);
        Assert.Equal("a+b=c", result.Token);
    }

    [Theory]
    [InlineData("http://localhost:3000/callback?steamId=7")] // no token
    [InlineData("http://localhost:3000/callback?steamId=7&token=")] // empty token
    [InlineData("http://localhost:3000/callback?steamId=7&token=%20%20")] // whitespace token
    [InlineData("http://localhost:3000/callback?token=abc")] // no steamId
    [InlineData("http://localhost:3000/callback?steamId=nope&token=abc")] // non-numeric steamId
    [InlineData("http://localhost:3000/callback?steamId=-1&token=abc")] // negative steamId
    [InlineData("http://localhost:3000/callback")] // no query at all
    [SuppressMessage("Design", "CA1054:URI parameters should not be strings",
        Justification = "Test parameters use string for testability")]
    public void ParseCallback_InvalidCallback_Throws(string uri) =>
        Assert.Throws<InvalidOperationException>(() => SteamLoginService.ParseCallback(new Uri(uri)));
}
