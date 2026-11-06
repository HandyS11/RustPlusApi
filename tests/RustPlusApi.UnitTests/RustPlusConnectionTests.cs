using System;
using Xunit;

namespace RustPlusApi.UnitTests;

public class RustPlusConnectionTests
{
    private static readonly Guid ServerId = new("089941d9-064e-4628-9c60-eaeb01a943e8");

    [Fact]
    public void Direct_ToString_RedactsPlayerToken()
    {
        var connection = new DirectConnection("12.34.56.78", 28082, 76561198000000000UL, 1234567890);

        var text = connection.ToString();

        // The credential must never appear in the string form; the rest stays visible for debuggability.
        Assert.DoesNotContain("1234567890", text, StringComparison.Ordinal);
        Assert.Equal(
            "DirectConnection { PlayerId = 76561198000000000, PlayerToken = ***, Server = 12.34.56.78, Port = 28082 }",
            text);
    }

    [Fact]
    public void Companion_ToString_RedactsBothTokens()
    {
        var connection = new CompanionConnection(ServerId, "secret-companion-token", 76561198000000000UL, 1234567890);

        var text = connection.ToString();

        Assert.DoesNotContain("1234567890", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-companion-token", text, StringComparison.Ordinal);
        Assert.Equal(
            "CompanionConnection { PlayerId = 76561198000000000, PlayerToken = ***, "
            + "ServerId = 089941d9-064e-4628-9c60-eaeb01a943e8, AuthToken = *** }",
            text);
    }

    [Fact]
    public void Direct_Equality_UsesEveryMember_DespiteRedactedToString()
    {
        var a = new DirectConnection("host", 1, 2UL, 3);

        Assert.Equal(a, new DirectConnection("host", 1, 2UL, 3));
        Assert.Equal(a.GetHashCode(), new DirectConnection("host", 1, 2UL, 3).GetHashCode());
        Assert.NotEqual(a, new DirectConnection("other", 1, 2UL, 3));
        Assert.NotEqual(a, new DirectConnection("host", 9, 2UL, 3));
        Assert.NotEqual(a, new DirectConnection("host", 1, 9UL, 3));
        // Redacting the token from ToString must not collapse equality: the token is still compared.
        Assert.NotEqual(a, new DirectConnection("host", 1, 2UL, 99));
    }

    [Fact]
    public void Companion_Equality_UsesEveryMember()
    {
        var a = new CompanionConnection(ServerId, "a", 2UL, 3);

        Assert.Equal(a, new CompanionConnection(ServerId, "a", 2UL, 3));
        Assert.Equal(a.GetHashCode(), new CompanionConnection(ServerId, "a", 2UL, 3).GetHashCode());
        Assert.NotEqual(a, new CompanionConnection(Guid.NewGuid(), "a", 2UL, 3));
        Assert.NotEqual(a, new CompanionConnection(ServerId, "b", 2UL, 3));
        Assert.NotEqual(a, new CompanionConnection(ServerId, "a", 9UL, 3));
        Assert.NotEqual(a, new CompanionConnection(ServerId, "a", 2UL, 99));
    }

    [Fact]
    public void Equality_DirectAndCompanion_AreNeverEqual()
    {
        RustPlusConnection direct = new DirectConnection("host", 1, 2UL, 3);
        RustPlusConnection companion = new CompanionConnection(ServerId, "a", 2UL, 3);

        Assert.NotEqual(direct, companion);
    }

    [Fact]
    public void Direct_NullServer_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new DirectConnection(null!, 1, 2UL, 3));

        Assert.Equal("server", ex.ParamName);
    }

    [Fact]
    public void Companion_EmptyServerId_Throws()
    {
        // Guid.Empty is what ServerPairing.ServerId holds when a push carried no id; the companion socket
        // would answer it with a bare 404, so it is refused before any connection is attempted.
        var ex = Assert.Throws<ArgumentException>(() => new CompanionConnection(Guid.Empty, "token", 2UL, 3));

        Assert.Equal("serverId", ex.ParamName);
    }

    [Fact]
    public void Companion_NullAuthToken_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new CompanionConnection(ServerId, null!, 2UL, 3));

        Assert.Equal("authToken", ex.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Companion_BlankAuthToken_Throws(string authToken)
    {
        var ex = Assert.Throws<ArgumentException>(() => new CompanionConnection(ServerId, authToken, 2UL, 3));

        Assert.Equal("authToken", ex.ParamName);
    }
}
