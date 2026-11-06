using RustPlusApi.Fcm.Registration.Steps.Browser;
using Xunit;

namespace RustPlusApi.Fcm.Registration.UnitTests;

/// <summary>Pins the user-agent rewrite that keeps Facepunch from answering "Login Failed" to a Windows browser.</summary>
public class UserAgentRewriterTests
{
    private const string EdgeOnWindows =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36 Edg/142.0.0.0";

    private const string EdgeOnLinux =
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36 Edg/142.0.0.0";

    [Fact]
    public void ToNonWindows_EdgeOnWindows()
    {
        var output = UserAgentRewriter.ToNonWindows(EdgeOnWindows);

        Assert.Equal(EdgeOnLinux, output);
        Assert.False(UserAgentRewriter.SaysWindows(output));
    }

    [Fact]
    public void ToNonWindows_StrayWindowsTokenOutsideParentheses_IsRemoved()
    {
        var output = UserAgentRewriter.ToNonWindows("Foo/1 WINDOWS Bar/2");

        Assert.Equal("Foo/1  Bar/2", output);
        Assert.False(UserAgentRewriter.SaysWindows(output));
    }

    [Fact]
    public void ToNonWindows_LinuxUa_Unchanged()
    {
        var output = UserAgentRewriter.ToNonWindows(EdgeOnLinux);

        Assert.Equal(EdgeOnLinux, output);
        Assert.False(UserAgentRewriter.SaysWindows(output));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(EdgeOnLinux, false)]
    [InlineData("windows", true)]
    [InlineData("(Windows NT 10.0)", true)]
    public void SaysWindows_DetectsTheWordCaseInsensitively(string? userAgent, bool expected) =>
        Assert.Equal(expected, UserAgentRewriter.SaysWindows(userAgent));

    [Fact]
    public void NonWindowsPlatformHint_IsTheQuotedLinuxName() =>
        Assert.Equal("\"Linux\"", UserAgentRewriter.NonWindowsPlatformHint);
}
