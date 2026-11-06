using RustPlusApi.Fcm.Registration.Steps.Browser;
using RustPlusApi.Fcm.Registration.UnitTests.TestHelpers;
using Xunit;

namespace RustPlusApi.Fcm.Registration.UnitTests;

/// <summary>Pins the quoting and launch arguments of the controlled browser.</summary>
public class BrowserLaunchArgumentsTests
{
    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("", "\"\"")]
    [InlineData(@"C:\Program Files\x", "\"C:\\Program Files\\x\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    [InlineData(@"C:\a b\", "\"C:\\a b\\\\\"")]
    [InlineData("a b\\\"c", "\"a b\\\\\\\"c\"")]
    public void Quote_FollowsCommandLineToArgvW(string input, string expected) =>
        Assert.Equal(expected, BrowserLaunchArguments.Quote(input));

    [Fact]
    public void BuildStartInfo_QuotesProfilePathWithSpacesAndNonAscii()
    {
        var browser = new BrowserInfo("Microsoft Edge", @"C:\PF\msedge.exe", [], null);

        var info = BrowserLaunchArguments.BuildStartInfo(
            browser, @"C:\Users\Jöhn Smith\Temp\RustPlusApi.SteamLogin\p1");

        Assert.Equal(@"C:\PF\msedge.exe", info.FileName);
        Assert.False(info.UseShellExecute);
        Assert.True(info.RedirectStandardOutput);
        Assert.True(info.RedirectStandardError);
        // Port 0: the browser binds a free port itself and reports it in DevToolsActivePort, so no other process can
        // claim a port the library picked before the browser binds it.
        Assert.Equal(
            "--remote-debugging-port=0 \"--user-data-dir=C:\\Users\\Jöhn Smith\\Temp\\RustPlusApi.SteamLogin\\p1\" --no-first-run --no-default-browser-check --new-window about:blank",
            info.Arguments);
    }

    [Fact]
    public void BuildStartInfo_Flatpak_StartsWithRunAndAppId()
    {
        var browser = new BrowserInfo("Google Chrome", "/usr/bin/flatpak", ["run", "com.google.Chrome"],
            "com.google.Chrome");

        var info = BrowserLaunchArguments.BuildStartInfo(browser, "/p");

        Assert.Equal("/usr/bin/flatpak", info.FileName);
        Assert.Equal(
            "run com.google.Chrome --remote-debugging-port=0 --user-data-dir=/p --no-first-run --no-default-browser-check --new-window about:blank",
            info.Arguments);
    }

    [Fact]
    public void GetProfileRoot_Flatpak_UsesTheAppSandbox()
    {
        var env = new FakeBrowserEnvironment
        {
            HomeDirectory = "/home/u", TempDirectory = "/tmp"
        };
        env.Variables["XDG_CACHE_HOME"] = "/home/u/.xdg-cache";
        var browser = new BrowserInfo("Google Chrome", "/usr/bin/flatpak", ["run", "com.google.Chrome"],
            "com.google.Chrome");

        Assert.Equal(
            Path.Combine("/home/u", ".var", "app", "com.google.Chrome", "cache", "RustPlusApi.SteamLogin"),
            BrowserLaunchArguments.GetProfileRoot(browser, env));
    }

    [Fact]
    public void GetProfileRoot_Linux_UsesXdgCacheHomeWhenSet()
    {
        // Linux /tmp is shared between local users; the profile holds Steam and Facepunch sessions.
        var env = new FakeBrowserEnvironment
        {
            HomeDirectory = "/home/u", TempDirectory = "/tmp"
        };
        env.Variables["XDG_CACHE_HOME"] = "/home/u/.xdg-cache";
        var browser = new BrowserInfo("chromium", "/usr/bin/chromium", [], null);

        Assert.Equal(Path.Combine("/home/u/.xdg-cache", "RustPlusApi.SteamLogin"),
            BrowserLaunchArguments.GetProfileRoot(browser, env));
    }

    [Fact]
    public void GetProfileRoot_Linux_XdgCacheHomeUnset_UsesHomeCache()
    {
        var env = new FakeBrowserEnvironment
        {
            HomeDirectory = "/home/u", TempDirectory = "/tmp"
        };
        var browser = new BrowserInfo("chromium", "/usr/bin/chromium", [], null);

        Assert.Equal(Path.Combine("/home/u", ".cache", "RustPlusApi.SteamLogin"),
            BrowserLaunchArguments.GetProfileRoot(browser, env));
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/cache")]
    public void GetProfileRoot_Linux_XdgCacheHomeNotAbsolute_IsIgnored(string xdgCacheHome)
    {
        // The XDG Base Directory spec says relative values are invalid and must be ignored.
        var env = new FakeBrowserEnvironment
        {
            HomeDirectory = "/home/u", TempDirectory = "/tmp"
        };
        env.Variables["XDG_CACHE_HOME"] = xdgCacheHome;
        var browser = new BrowserInfo("chromium", "/usr/bin/chromium", [], null);

        Assert.Equal(Path.Combine("/home/u", ".cache", "RustPlusApi.SteamLogin"),
            BrowserLaunchArguments.GetProfileRoot(browser, env));
    }

    [Fact]
    public void GetProfileRoot_Windows_UsesTheUsersTemp()
    {
        var env = new FakeBrowserEnvironment
        {
            IsWindows = true, HomeDirectory = @"C:\Users\u", TempDirectory = @"C:\Users\u\AppData\Local\Temp"
        };
        env.Variables["XDG_CACHE_HOME"] = "/home/u/.xdg-cache";
        var browser = new BrowserInfo("Microsoft Edge", @"C:\PF\msedge.exe", [], null);

        Assert.Equal(
            Path.Combine(@"C:\Users\u\AppData\Local\Temp", "RustPlusApi.SteamLogin"),
            BrowserLaunchArguments.GetProfileRoot(browser, env));
    }

    [Fact]
    public void GetProfileRoot_MacOS_UsesTheUsersTemp()
    {
        var env = new FakeBrowserEnvironment
        {
            IsMacOS = true, HomeDirectory = "/Users/u", TempDirectory = "/var/folders/ab/T"
        };
        env.Variables["XDG_CACHE_HOME"] = "/Users/u/.xdg-cache";
        var browser = new BrowserInfo("Google Chrome", "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
            [], null);

        Assert.Equal(Path.Combine("/var/folders/ab/T", "RustPlusApi.SteamLogin"),
            BrowserLaunchArguments.GetProfileRoot(browser, env));
    }
}
