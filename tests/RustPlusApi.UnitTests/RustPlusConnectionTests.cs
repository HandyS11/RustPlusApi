using System;
using Xunit;

namespace RustPlusApi.UnitTests;

public class RustPlusConnectionTests
{
    [Fact]
    public void ToString_RedactsPlayerToken()
    {
        var connection = new RustPlusConnection("12.34.56.78", 28082, 76561198000000000UL, 1234567890);

        var text = connection.ToString();

        // The credential must never appear in the string form.
        Assert.DoesNotContain("1234567890", text, StringComparison.Ordinal);
        Assert.Contains("PlayerToken = ***", text, StringComparison.Ordinal);

        // Non-sensitive fields stay visible for debuggability.
        Assert.Contains("Server = 12.34.56.78", text, StringComparison.Ordinal);
        Assert.Contains("Port = 28082", text, StringComparison.Ordinal);
        Assert.Contains("PlayerId = 76561198000000000", text, StringComparison.Ordinal);
        Assert.Contains("UseFacepunchProxy = False", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Equality_UsesEveryMember_DespiteRedactedToString()
    {
        var a = new RustPlusConnection("host", 1, 2UL, 3);
        var b = new RustPlusConnection("host", 1, 2UL, 3);
        var differentToken = a with
        {
            PlayerToken = 99
        };

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        // Redacting the token from ToString must not collapse equality: the token is still compared.
        Assert.NotEqual(a, differentToken);
    }
}
