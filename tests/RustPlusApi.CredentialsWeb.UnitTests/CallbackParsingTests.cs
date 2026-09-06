using RustPlusApi.CredentialsWeb.Endpoints;
using Xunit;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class CallbackParsingTests
{
    private const string Token = "0123456789abcdef0123456789abcdef";

    private static string Address(string prefix) =>
        $"{prefix}/callback/{Token}?steamId=76561198249527954&token=steam-token";

    [Fact]
    public void TryParsePastedCallback_ReadsTheTokenAndTheSteamIdentity()
    {
        var parsed = CallbackParsing.TryParsePastedCallback(
            Address("http://localhost:54321"), out var returnToken, out var login);

        Assert.True(parsed);
        Assert.Equal(Token, returnToken);
        Assert.Equal(76561198249527954UL, login!.SteamId);
        Assert.Equal("steam-token", login.Token);
    }

    [Fact]
    public void TryParsePastedCallback_AcceptsAnAddressCopiedWithoutItsScheme()
    {
        // Safari drops "http://" when the address bar is copied.
        var parsed = CallbackParsing.TryParsePastedCallback(
            Address("localhost:54321"), out var returnToken, out _);

        Assert.True(parsed);
        Assert.Equal(Token, returnToken);
    }

    [Fact]
    public void TryParsePastedCallback_IgnoresSurroundingWhitespace()
    {
        var parsed = CallbackParsing.TryParsePastedCallback(
            $"  {Address("http://localhost:54321")}\n", out _, out _);

        Assert.True(parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url at all")]
    [InlineData("ftp://localhost:54321/callback/0123456789abcdef0123456789abcdef?steamId=1&token=t")]
    // The Facepunch login page pasted by mistake.
    [InlineData("https://companion-rust.facepunch.com/login?returnUrl=http%3A%2F%2Flocalhost")]
    // No path segment at all.
    [InlineData("http://localhost:54321")]
    // A path segment that is not a return token.
    [InlineData("http://localhost:54321/callback/nope?steamId=76561198249527954&token=steam-token")]
    // 32 characters, but not hex.
    [InlineData("http://localhost:54321/callback/zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz?steamId=1&token=t")]
    // Everything right except the Steam identity.
    [InlineData("http://localhost:54321/callback/0123456789abcdef0123456789abcdef")]
    [InlineData("http://localhost:54321/callback/0123456789abcdef0123456789abcdef?steamId=76561198249527954")]
    [InlineData("http://localhost:54321/callback/0123456789abcdef0123456789abcdef?token=steam-token")]
    [InlineData("http://localhost:54321/callback/0123456789abcdef0123456789abcdef?steamId=nope&token=t")]
    public void TryParsePastedCallback_RejectsAnythingElse(string? pasted)
    {
        var parsed = CallbackParsing.TryParsePastedCallback(pasted, out var returnToken, out var login);

        Assert.False(parsed);
        Assert.Null(returnToken);
        Assert.Null(login);
    }
}
