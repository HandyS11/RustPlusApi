using System.Runtime.InteropServices;
using RustPlusApi.Fcm.Registration.Steps.Browser;
using RustPlusApi.Fcm.Registration.UnitTests.TestHelpers;
using Xunit;

namespace RustPlusApi.Fcm.Registration.UnitTests;

/// <summary>Pins browser discovery on Windows, macOS and Linux through a fake environment.</summary>
public class BrowserLocatorTests
{
    private const string Edge86 = @"C:\PF86\Microsoft\Edge\Application\msedge.exe";
    private const string EdgePf = @"C:\PF\Microsoft\Edge\Application\msedge.exe";
    private const string ChromePf = @"C:\PF\Google\Chrome\Application\chrome.exe";
    private const string ChromePf86 = @"C:\PF86\Google\Chrome\Application\chrome.exe";
    private const string ChromeLocal = @"C:\Local\Google\Chrome\Application\chrome.exe";
    private const string BravePf = @"C:\PF\BraveSoftware\Brave-Browser\Application\brave.exe";
    private const string BraveLocal = @"C:\Local\BraveSoftware\Brave-Browser\Application\brave.exe";

    private const string UbuntuChromiumBrowserWrapper =
        "#! /bin/sh\n# Transitional wrapper: Chromium is a Snap on this release.\nexec /snap/bin/chromium \"$@\"\n";

    private static FakeBrowserEnvironment Windows()
    {
        var env = new FakeBrowserEnvironment
        {
            IsWindows = true
        };
        env.Folders[Environment.SpecialFolder.ProgramFilesX86] = @"C:\PF86";
        env.Folders[Environment.SpecialFolder.ProgramFiles] = @"C:\PF";
        env.Folders[Environment.SpecialFolder.LocalApplicationData] = @"C:\Local";
        return env;
    }

    private static BrowserInfo? FindBrowser(FakeBrowserEnvironment env) => new BrowserLocator(env).Find(null).Browser;

    [Fact]
    public void Windows_FindsEdge()
    {
        var env = Windows();
        env.Files.Add(Edge86);

        var result = new BrowserLocator(env).Find(null);

        Assert.Equal("Microsoft Edge", result.Browser!.Name);
        Assert.Equal(Edge86, result.Browser.ExecutablePath);
        Assert.Empty(result.Browser.PrefixArguments);
        Assert.Null(result.Browser.FlatpakAppId);
        Assert.Empty(result.SkippedSnapPaths);
    }

    [Theory]
    [InlineData(new[]
    {
        Edge86, EdgePf, ChromePf, BravePf
    }, "Microsoft Edge", Edge86)]
    [InlineData(new[]
    {
        EdgePf, ChromePf, BravePf
    }, "Microsoft Edge", EdgePf)]
    [InlineData(new[]
    {
        ChromePf, ChromePf86, ChromeLocal, BravePf
    }, "Google Chrome", ChromePf)]
    [InlineData(new[]
    {
        ChromePf86, ChromeLocal, BravePf
    }, "Google Chrome", ChromePf86)]
    [InlineData(new[]
    {
        ChromeLocal, BravePf
    }, "Google Chrome", ChromeLocal)]
    [InlineData(new[]
    {
        BravePf, BraveLocal
    }, "Brave", BravePf)]
    [InlineData(new[]
    {
        BraveLocal
    }, "Brave", BraveLocal)]
    public void Windows_Order(string[] present, string name, string path)
    {
        var env = Windows();
        foreach (var file in present)
        {
            env.Files.Add(file);
        }

        var browser = FindBrowser(env)!;

        Assert.Equal(name, browser.Name);
        Assert.Equal(path, browser.ExecutablePath);
    }

    [Fact]
    public void Windows_NothingInstalled_ReturnsNull() => Assert.Null(FindBrowser(Windows()));

    [Fact]
    public void Windows_UnresolvedFolders_AreSkipped()
    {
        var env = new FakeBrowserEnvironment
        {
            IsWindows = true
        };
        env.Folders[Environment.SpecialFolder.LocalApplicationData] = @"C:\Local";
        env.Files.Add(BraveLocal);

        Assert.Equal(BraveLocal, FindBrowser(env)!.ExecutablePath);
    }

    [Theory]
    [InlineData("Google Chrome")]
    [InlineData("Microsoft Edge")]
    [InlineData("Brave Browser")]
    [InlineData("Chromium")]
    public void MacOS_FindsEachBundle(string app)
    {
        var env = new FakeBrowserEnvironment
        {
            IsMacOS = true
        };
        var path = $"/Applications/{app}.app/Contents/MacOS/{app}";
        env.Files.Add(path);

        var browser = FindBrowser(env)!;

        Assert.Equal(app, browser.Name);
        Assert.Equal(path, browser.ExecutablePath);
    }

    [Fact]
    public void MacOS_ChromeBeatsEdge()
    {
        var env = new FakeBrowserEnvironment
        {
            IsMacOS = true
        };
        env.Files.Add("/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge");
        env.Files.Add("/Applications/Google Chrome.app/Contents/MacOS/Google Chrome");

        Assert.Equal("Google Chrome", FindBrowser(env)!.Name);
    }

    [Fact]
    public void MacOS_NothingInstalled_ReturnsNull() => Assert.Null(FindBrowser(new FakeBrowserEnvironment
    {
        IsMacOS = true
    }));

    [Fact]
    public void Linux_PrefersNonSnapAndReportsSkippedSnap()
    {
        var env = new FakeBrowserEnvironment();
        env.Variables["PATH"] = "/snap/bin:/usr/bin";
        env.Files.Add("/snap/bin/chromium");
        env.Files.Add("/usr/bin/google-chrome");

        var result = new BrowserLocator(env).Find(null);

        Assert.Equal("/usr/bin/google-chrome", result.Browser!.ExecutablePath);
        Assert.Equal("google-chrome", result.Browser.Name);
        Assert.Equal(["/snap/bin/chromium"], result.SkippedSnapPaths);
    }

    [Fact]
    public void Linux_OnlySnap_ReturnsNullWithOneSkippedPath()
    {
        var env = new FakeBrowserEnvironment();
        env.Variables["PATH"] = "/snap/bin:/usr/bin";
        env.Files.Add("/snap/bin/chromium");

        var result = new BrowserLocator(env).Find(null);

        Assert.Null(result.Browser);
        Assert.Equal(["/snap/bin/chromium"], result.SkippedSnapPaths);
    }

    [Fact]
    public void Linux_AptChromiumBrowserWrapperScript_IsSkippedAsSnap()
    {
        // Ubuntu's apt chromium-browser is a transitional package: /usr/bin/chromium-browser is a shell script that
        // execs the Snap.
        var env = new FakeBrowserEnvironment();
        env.Variables["PATH"] = "/usr/bin:/snap/bin";
        env.Files.Add("/snap/bin/chromium");
        env.Files.Add("/usr/bin/chromium-browser");
        env.FileHeads["/usr/bin/chromium-browser"] = UbuntuChromiumBrowserWrapper;

        var result = new BrowserLocator(env).Find(null);

        Assert.Null(result.Browser);
        Assert.Equal(["/snap/bin/chromium", "/usr/bin/chromium-browser"], result.SkippedSnapPaths);
        Assert.Equal([("/usr/bin/chromium-browser", 4096)], env.HeadReads);
    }

    [Fact]
    public void Linux_SnapWrapperScript_IsReportedWhileARealBrowserWins()
    {
        var env = new FakeBrowserEnvironment();
        env.Variables["PATH"] = "/usr/bin";
        env.Files.Add("/usr/bin/google-chrome");
        env.Files.Add("/usr/bin/chromium-browser");
        env.FileHeads["/usr/bin/google-chrome"] = "\u007FELF\u0002\u0001\u0001";
        env.FileHeads["/usr/bin/chromium-browser"] = UbuntuChromiumBrowserWrapper;

        var result = new BrowserLocator(env).Find(null);

        Assert.Equal("/usr/bin/google-chrome", result.Browser!.ExecutablePath);
        Assert.Equal(["/usr/bin/chromium-browser"], result.SkippedSnapPaths);
    }

    [Theory]
    [InlineData("\u007FELF\u0002\u0001\u0001 /snap/bin/chromium")] // a binary that merely contains the text
    [InlineData("#!/bin/sh\nexec /usr/lib/chromium/chromium \"$@\"\n")] // Debian's own wrapper script
    [InlineData("")]
    public void Linux_ExecutableThatIsNoSnapWrapper_IsUsed(string head)
    {
        var env = new FakeBrowserEnvironment();
        env.Variables["PATH"] = "/usr/bin";
        env.Files.Add("/usr/bin/chromium");
        env.FileHeads["/usr/bin/chromium"] = head;

        var result = new BrowserLocator(env).Find(null);

        Assert.Equal("/usr/bin/chromium", result.Browser!.ExecutablePath);
        Assert.Empty(result.SkippedSnapPaths);
    }

    [Fact]
    public void Linux_UnreadableExecutable_IsUsed()
    {
        var env = new FakeBrowserEnvironment();
        env.Variables["PATH"] = "/usr/bin";
        env.Files.Add("/usr/bin/chromium");

        var result = new BrowserLocator(env).Find(null);

        Assert.Equal("/usr/bin/chromium", result.Browser!.ExecutablePath);
        Assert.Empty(result.SkippedSnapPaths);
        Assert.Equal([("/usr/bin/chromium", 4096)], env.HeadReads);
    }

    [Fact]
    public void Linux_SnapdInstallUnderVarLib_IsSkippedAsSnap()
    {
        // Fedora and Arch install snapd under /var/lib/snapd/snap rather than /snap.
        var env = new FakeBrowserEnvironment();
        env.Variables["PATH"] = "/var/lib/snapd/snap/bin";
        env.Files.Add("/var/lib/snapd/snap/bin/chromium");

        var result = new BrowserLocator(env).Find(null);

        Assert.Null(result.Browser);
        Assert.Equal(["/var/lib/snapd/snap/bin/chromium"], result.SkippedSnapPaths);
        Assert.Empty(env.HeadReads);
    }

    [Theory]
    [InlineData("google-chrome-stable")]
    [InlineData("chromium")]
    [InlineData("chromium-browser")]
    [InlineData("microsoft-edge")]
    [InlineData("microsoft-edge-stable")]
    [InlineData("brave-browser")]
    public void Linux_FindsEachName(string executable)
    {
        var env = new FakeBrowserEnvironment();
        env.Variables["PATH"] = "/usr/local/bin:/usr/bin";
        env.Files.Add($"/usr/bin/{executable}");

        Assert.Equal($"/usr/bin/{executable}", FindBrowser(env)!.ExecutablePath);
    }

    [Fact]
    public void Linux_NameOrderBeatsDirectoryOrder()
    {
        var env = new FakeBrowserEnvironment();
        env.Variables["PATH"] = "/a:/b";
        env.Files.Add("/a/chromium");
        env.Files.Add("/b/google-chrome");

        Assert.Equal("/b/google-chrome", FindBrowser(env)!.ExecutablePath);
    }

    [Fact]
    public void Linux_NoPath_ReturnsNull() => Assert.Null(FindBrowser(new FakeBrowserEnvironment()));

    [Fact]
    public void Linux_RelativePathEntries_AreSkipped()
    {
        // A relative PATH entry resolves against the current directory, where a planted look-alike could sit; the
        // browser is started by this path with UseShellExecute off, so it must be absolute.
        var env = new FakeBrowserEnvironment();
        env.Variables["PATH"] = ".:/usr/bin";
        env.Files.Add(Path.Combine(".", "google-chrome"));
        env.Files.Add("/usr/bin/chromium");

        Assert.Equal("/usr/bin/chromium", FindBrowser(env)!.ExecutablePath);
    }

    [Theory]
    [InlineData("bin")]
    [InlineData("~/bin")]
    [InlineData("node_modules/.bin")]
    public void Linux_OnlyRelativePathEntries_FindsNothing(string directory)
    {
        var env = new FakeBrowserEnvironment();
        env.Variables["PATH"] = directory;
        env.Files.Add(Path.Combine(directory, "google-chrome"));

        var result = new BrowserLocator(env).Find(null);

        Assert.Null(result.Browser);
        Assert.Empty(result.SkippedSnapPaths);
    }

    [Fact]
    public void Linux_FlatpakOnRelativePathEntry_IsSkipped()
    {
        var env = new FakeBrowserEnvironment();
        env.Variables["PATH"] = "bin:/usr/bin";
        env.Files.Add(Path.Combine("bin", "flatpak"));
        env.Files.Add("/usr/bin/flatpak");
        env.Directories.Add("/var/lib/flatpak/app/com.google.Chrome");

        Assert.Equal("/usr/bin/flatpak", FindBrowser(env)!.ExecutablePath);
    }

    [Fact]
    public void Linux_FlatpakOnlyOnRelativePathEntry_FindsNothing()
    {
        var env = new FakeBrowserEnvironment();
        env.Variables["PATH"] = "bin";
        env.Files.Add(Path.Combine("bin", "flatpak"));
        env.Directories.Add("/var/lib/flatpak/app/com.google.Chrome");

        Assert.Null(FindBrowser(env));
    }

    [Theory]
    [InlineData("/var/lib/flatpak/app/com.google.Chrome", "Google Chrome", "com.google.Chrome")]
    [InlineData("/home/u/.local/share/flatpak/app/com.google.Chrome", "Google Chrome", "com.google.Chrome")]
    [InlineData("/var/lib/flatpak/app/com.microsoft.Edge", "Microsoft Edge", "com.microsoft.Edge")]
    [InlineData("/var/lib/flatpak/app/com.brave.Browser", "Brave", "com.brave.Browser")]
    [InlineData("/var/lib/flatpak/app/org.chromium.Chromium", "Chromium", "org.chromium.Chromium")]
    public void Linux_Flatpak(string directory, string name, string appId)
    {
        var env = new FakeBrowserEnvironment();
        env.Variables["PATH"] = "/bin:/usr/bin";
        env.Files.Add("/usr/bin/flatpak");
        env.Directories.Add(directory);

        var browser = FindBrowser(env)!;

        Assert.Equal(name, browser.Name);
        Assert.Equal("/usr/bin/flatpak", browser.ExecutablePath);
        Assert.Equal(["run", appId], browser.PrefixArguments);
        Assert.Equal(appId, browser.FlatpakAppId);
    }

    [Fact]
    public void Linux_FlatpakAppButNoFlatpakExecutable_ReturnsNull()
    {
        var env = new FakeBrowserEnvironment();
        env.Variables["PATH"] = "/usr/bin";
        env.Directories.Add("/var/lib/flatpak/app/com.google.Chrome");

        Assert.Null(FindBrowser(env));
    }

    [Fact]
    public void Linux_FlatpakExecutableButNoApp_ReturnsNull()
    {
        var env = new FakeBrowserEnvironment();
        env.Variables["PATH"] = "/usr/bin";
        env.Files.Add("/usr/bin/flatpak");

        Assert.Null(FindBrowser(env));
    }

    [Fact]
    public void ExplicitPath_Exists_UsesItsFileName()
    {
        var env = new FakeBrowserEnvironment();
        env.Files.Add("/opt/vivaldi/vivaldi.exe");

        var browser = new BrowserLocator(env).Find("/opt/vivaldi/vivaldi.exe").Browser!;

        Assert.Equal("vivaldi", browser.Name);
        Assert.Equal("/opt/vivaldi/vivaldi.exe", browser.ExecutablePath);
        Assert.Empty(browser.PrefixArguments);
        Assert.Null(browser.FlatpakAppId);
    }

    [Fact]
    public void ExplicitPath_Missing_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new BrowserLocator(new FakeBrowserEnvironment()).Find("/nope/browser"));

        Assert.Contains("/nope/browser", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemBrowserEnvironment_MatchesTheRealSystem()
    {
        var env = SystemBrowserEnvironment.Instance;
        var file = Path.GetTempFileName();
        try
        {
            Assert.Equal(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), env.IsWindows);
            Assert.Equal(RuntimeInformation.IsOSPlatform(OSPlatform.OSX), env.IsMacOS);
            Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), env.HomeDirectory);
            Assert.Equal(Path.GetTempPath(), env.TempDirectory);
            Assert.Equal(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                env.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            Assert.Equal(Environment.GetEnvironmentVariable("PATH"), env.GetEnvironmentVariable("PATH"));
            Assert.True(env.FileExists(file));
            Assert.False(env.FileExists(file + ".missing"));
            Assert.True(env.DirectoryExists(Path.GetTempPath()));
            Assert.False(env.DirectoryExists(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void SystemBrowserEnvironment_ReadFileHead_ReadsAtMostMaxBytes()
    {
        var env = SystemBrowserEnvironment.Instance;
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, UbuntuChromiumBrowserWrapper);

            Assert.Equal(UbuntuChromiumBrowserWrapper, env.ReadFileHead(file, 4096));
            Assert.Equal("#! /", env.ReadFileHead(file, 4));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void SystemBrowserEnvironment_ReadFileHead_UnreadablePath_ReturnsNull()
    {
        var env = SystemBrowserEnvironment.Instance;
        var directory = Path.Combine(Path.GetTempPath(), "rustplusapi-head-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Assert.Null(env.ReadFileHead(Path.Combine(directory, "missing"), 4096));
            Assert.Null(env.ReadFileHead(directory, 4096));
        }
        finally
        {
            Directory.Delete(directory);
        }
    }
}
