using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using RustPlusApi.Fcm.Registration;
using RustPlusApi.Fcm.Registration.Steps;
using RustPlusApi.Fcm.Registration.Steps.Browser;
using RustPlusApi.Fcm.Registration.Steps.Cdp;
using RustPlusApi.Fcm.Registration.UnitTests.TestHelpers;
using Xunit;

namespace RustPlusApi.Fcm.Registration.UnitTests;

/// <summary>Offline coverage of the Steam login: URL construction, redirect and paste parsing, and the
/// browser-driven orchestration (lookup, profile lifecycle, launch, cleanup, paste fallback) over fakes.</summary>
public sealed class SteamLoginServiceTests : IDisposable
{
    private const string ExpectedLoginUrl =
        "https://companion-rust.facepunch.com/login?returnUrl=%2Fapp%3FreturnUrl%3Drust-plus%3A%2F%2Frust-auth-session";

    private const string MacChrome = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
    private const string PastedAddress = "rust-plus://rust-auth-session?steamId=7&token=abc";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);
    private readonly FakeBrowserEnvironment _environment;
    private readonly FakeBrowserLauncher _launcher = new();

    /// <summary>A per-test folder. macOS mode keeps the profile root under TempDirectory, which points here.</summary>
    private readonly string _temp =
        Path.Combine(Path.GetTempPath(), "rustplusapi-steamlogin-tests-" + Guid.NewGuid().ToString("N"));

    public SteamLoginServiceTests()
    {
        _environment = new FakeBrowserEnvironment
        {
            IsMacOS = true, TempDirectory = _temp, HomeDirectory = _temp
        };
        _environment.Files.Add(MacChrome);
    }

    private string ProfileRoot =>
        BrowserLaunchArguments.GetProfileRoot(new BrowserLocator(_environment).Find(null).Browser!, _environment);

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            Directory.Delete(_temp, recursive: true);
        }
    }

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
    public void ParseCallback_RustPlusRedirect_DecodesTokenExactly()
    {
        var uri = new Uri(
            "rust-plus://rust-auth-session?steamId=76561198249527954&token=eyJzIjoxfQ%3D%3D.a%2Bb%2Fc%3D");

        var result = SteamLoginService.ParseCallback(uri);

        Assert.Equal(76561198249527954UL, result.SteamId);
        Assert.Equal("eyJzIjoxfQ==.a+b/c=", result.Token);
    }

    [Theory]
    [InlineData("rust-plus://rust-auth-session?steamId=7&token=abc")]
    [InlineData("  rust-plus://rust-auth-session?steamId=7&token=abc \r\n")]
    [InlineData("\"rust-plus://rust-auth-session?steamId=7&token=abc\"")]
    [InlineData("location: rust-plus://rust-auth-session?steamId=7&token=abc")]
    [InlineData("Location:rust-plus://rust-auth-session?steamId=7&token=abc")]
    public void ParsePastedAddress_AcceptsTheRedirectAsCopied(string pasted)
    {
        var result = SteamLoginService.ParsePastedAddress(pasted);

        Assert.Equal(7UL, result.SteamId);
        Assert.Equal("abc", result.Token);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://companion-rust.facepunch.com/app?returnUrl=rust-plus://rust-auth-session")]
    [InlineData("http://localhost:3000/callback?steamId=7&token=abc")]
    [InlineData("\"rust-plus://rust-auth-session?steamId=7&token=abc")]
    [InlineData("\"")]
    [SuppressMessage("Design", "CA1054:URI parameters should not be strings",
        Justification = "Test parameters use string for testability")]
    public void ParsePastedAddress_RejectsAnythingButTheRustPlusRedirect(string pasted)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SteamLoginService.ParsePastedAddress(pasted));

        Assert.Contains("rust-plus://rust-auth-session", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("rust-plus://rust-auth-session bad")]
    [InlineData("rust-plus://rust-auth-session bad?steamId=7&token=SECRETTOKEN")]
    [InlineData("rust-plus://rust-auth-session:99999?steamId=7&token=SECRETTOKEN")]
    [InlineData("rust-plus://rust-auth-session:SECRETPORT?steamId=7&token=SECRETTOKEN")]
    [InlineData("rust-plus://rust-auth-session%")]
    [InlineData("rust-plus://rust-auth-session[")]
    [SuppressMessage("Design", "CA1054:URI parameters should not be strings",
        Justification = "Test parameters use string for testability")]
    public void ParsePastedAddress_RightPrefixButNotAnAddress_ThrowsTheFixedMessage(string pasted)
    {
        // The documented contract is InvalidOperationException, so a caller can ask again; never UriFormatException.
        var ex = Assert.Throws<InvalidOperationException>(() => SteamLoginService.ParsePastedAddress(pasted));

        Assert.Equal(
            "That is not the Rust+ login address. Paste the address that starts with rust-plus://rust-auth-session.",
            ex.Message);
        Assert.Null(ex.InnerException);
        Assert.DoesNotContain("SECRET", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ParsePastedAddress_Null_ThrowsArgumentNull() =>
        Assert.Throws<ArgumentNullException>(() => SteamLoginService.ParsePastedAddress(null!));

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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SteamLoginResult_BlankToken_Throws(string? token) =>
        Assert.Throws<ArgumentException>(() => new SteamLoginResult(7, token!));

    [Fact]
    public void SteamLoginResult_WithNonBlankToken_ReplacesTheToken()
    {
        var original = new SteamLoginResult(7, "first");

        var copy = original with
        {
            Token = "second"
        };

        Assert.Equal("second", copy.Token);
        Assert.Equal(7UL, copy.SteamId);
        Assert.Equal("first", original.Token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SteamLoginResult_WithBlankToken_Throws(string? token)
    {
        // The `with` path runs the init accessor rather than the field initializer the constructor
        // uses, so it needs pinning separately: without it a copy could carry a blank token past
        // validation the constructor already rejects.
        var original = new SteamLoginResult(7, "first");

        Assert.Throws<ArgumentException>(() => original with
        {
            Token = token!
        });
    }

    [Fact]
    public void ParseCallback_DuplicateParameter_LastValueWins()
    {
        var uri = new Uri("http://localhost:3000/callback?steamId=100&token=first&token=second&steamId=200");

        var result = SteamLoginService.ParseCallback(uri);

        Assert.Equal(200UL, result.SteamId);
        Assert.Equal("second", result.Token);
    }

    [Fact]
    public void ParseCallback_BareValuelessSegment_IgnoredParsesSuccessfully()
    {
        var uri = new Uri("http://localhost:3000/callback?steamId=42&token=valid&barekey&extra=1");

        var result = SteamLoginService.ParseCallback(uri);

        Assert.Equal(42UL, result.SteamId);
        Assert.Equal("valid", result.Token);
    }

    [Fact]
    public void ParseCallback_EmptySegments_AreSkipped()
    {
        var uri = new Uri("rust-plus://rust-auth-session?&steamId=7&&token=abc&");

        var result = SteamLoginService.ParseCallback(uri);

        Assert.Equal(7UL, result.SteamId);
        Assert.Equal("abc", result.Token);
    }

    [Fact]
    public void Options_DefaultsToEmptyOptions()
    {
        var options = new SteamLoginService().Options;

        Assert.Null(options.ReadPastedAddress);
        Assert.Null(options.BrowserPath);
    }

    [Fact]
    public void Options_InitRoundTrips()
    {
        var options = new SteamLoginOptions
        {
            BrowserPath = "/opt/chrome/chrome"
        };

        Assert.Same(options, new SteamLoginService
        {
            Options = options
        }.Options);
        Assert.Equal("/opt/chrome/chrome", options.BrowserPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void BrowserPath_UnsetValues_AreKept(string? path) =>
        Assert.Equal(path, new SteamLoginOptions
        {
            BrowserPath = path
        }.BrowserPath);

    [Theory]
    [InlineData("chrome")]
    [InlineData("./chrome")]
    [InlineData("bin/chrome")]
    public void BrowserPath_Relative_Throws(string path)
    {
        // A relative path only passes the existence check when the file sits in the current directory, which is
        // exactly where a planted look-alike would be: the browser must be named by an absolute path.
        var ex = Assert.Throws<ArgumentException>(() => new SteamLoginOptions
        {
            BrowserPath = path
        });

        Assert.Equal("value", ex.ParamName);
        Assert.StartsWith("The browser path must be absolute.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginAsync_ReportsLoginUrlFirst()
    {
        var steps = new List<string>();
        RedirectAfterNavigate();
        _launcher.OnLaunch = () => steps.Add("launch");

        await new SteamLoginService().LoginAsync(steps.Add, _environment, _launcher, CancellationToken.None)
            .WaitAsync(TestTimeout);

        Assert.Equal([ExpectedLoginUrl, "launch"], steps);
        var navigate = _launcher.Transport.Sent.Single(c => (string)c["method"]! == "Page.navigate");
        Assert.Equal(ExpectedLoginUrl, (string)navigate["params"]!["url"]!);
    }

    [Fact]
    public async Task LoginAsync_Success_ReturnsTokenClosesBrowserDeletesProfile()
    {
        RedirectAfterNavigate();

        var result = await LoginAsync(new SteamLoginService());

        Assert.Equal(7UL, result.SteamId);
        Assert.Equal("a+b", result.Token);
        Assert.Equal(["Fetch.enable", "Page.navigate", "Fetch.fulfillRequest", "Browser.close"], Methods());
        Assert.Equal("Google Chrome", _launcher.Browser!.Name);
        Assert.Equal(ProfileRoot, Path.GetDirectoryName(_launcher.ProfileDirectory));
        Assert.True(_launcher.Transport.Disposed);
        Assert.True(_launcher.Session!.Disposed);
        Assert.True(_launcher.Session.TransportDisposedFirst);
        Assert.True(_launcher.Session.ProfileExistedAtDispose);
        Assert.Empty(Directory.GetDirectories(ProfileRoot));
    }

    [Fact]
    public async Task LoginAsync_ExplicitBrowserPath_LaunchesThatBrowser()
    {
        _environment.Files.Add("/opt/custom/chrome");
        RedirectAfterNavigate();

        await LoginAsync(new SteamLoginService
        {
            Options = new SteamLoginOptions
            {
                BrowserPath = "/opt/custom/chrome"
            }
        });

        Assert.Equal("/opt/custom/chrome", _launcher.Browser!.ExecutablePath);
    }

    [Fact]
    public async Task LoginAsync_WindowClosed_ThrowsAndDeletesProfile()
    {
        _launcher.Transport.OnCommand = command =>
        {
            if ((string)command["method"]! == "Page.navigate")
            {
                _launcher.Transport.Close();
            }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => LoginAsync(new SteamLoginService()));

        Assert.Equal("The browser window was closed before the Steam login finished.", ex.Message);
        Assert.True(_launcher.Session!.Disposed);
        Assert.Empty(Directory.GetDirectories(ProfileRoot));
    }

    [Fact]
    public async Task LoginAsync_Cancelled_DeletesProfile()
    {
        using var cts = new CancellationTokenSource();
        _launcher.Transport.OnCommand = command =>
        {
            if ((string)command["method"]! == "Page.navigate")
            {
                cts.Cancel();
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SteamLoginService().LoginAsync(null, _environment, _launcher, cts.Token).WaitAsync(TestTimeout));

        // Cleanup ignores the cancelled token: the browser is still asked to close, then shut down.
        Assert.Equal("Browser.close", Methods()[^1]);
        Assert.True(_launcher.Session!.Disposed);
        Assert.Empty(Directory.GetDirectories(ProfileRoot));
    }

    [Fact]
    public async Task LoginAsync_AlreadyCancelled_NeverStartsTheBrowser()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SteamLoginService().LoginAsync(null, _environment, _launcher, cts.Token).WaitAsync(TestTimeout));

        Assert.Equal(0, _launcher.Calls);
        Assert.False(Directory.Exists(_temp));
    }

    [Fact]
    public async Task LoginAsync_CancelledDuringLaunch_DeletesProfileWithoutPasting()
    {
        var pasted = false;
        _launcher.Failure = new OperationCanceledException();
        var service = new SteamLoginService
        {
            Options = new SteamLoginOptions
            {
                ReadPastedAddress = (_, _) =>
                {
                    pasted = true;
                    return Task.FromResult(PastedAddress);
                },
            },
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() => LoginAsync(service));

        Assert.False(pasted);
        Assert.Empty(Directory.GetDirectories(ProfileRoot));
    }

    [Fact]
    public async Task LoginAsync_SweepsStaleProfilesBeforeLaunching()
    {
        var stale = Path.Combine(ProfileRoot, "stale");
        Directory.CreateDirectory(stale);
        Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow - LoginProfile.StaleLocklessAge - TimeSpan.FromMinutes(1));
        bool? staleExistedAtLaunch = null;
        _launcher.OnLaunch = () => staleExistedAtLaunch = Directory.Exists(stale);
        RedirectAfterNavigate();

        await LoginAsync(new SteamLoginService());

        Assert.False(staleExistedAtLaunch);
    }

    [Fact]
    public async Task LoginAsync_NoBrowser_UsesPastedAddress()
    {
        _environment.Files.Clear();
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;
        string? reason = null;
        var service = new SteamLoginService
        {
            Options = new SteamLoginOptions
            {
                ReadPastedAddress = (why, token) =>
                {
                    reason = why;
                    seen = token;
                    return Task.FromResult(" rust-plus://rust-auth-session?steamId=7&token=abc ");
                },
            },
        };

        var result = await service.LoginAsync(null, _environment, _launcher, cts.Token).WaitAsync(TestTimeout);

        Assert.Equal(7UL, result.SteamId);
        Assert.Equal("abc", result.Token);
        Assert.Equal("No Chrome, Edge, Brave or Chromium browser was found.", reason);
        Assert.Equal(cts.Token, seen);
        Assert.Equal(0, _launcher.Calls);
        Assert.False(Directory.Exists(_temp));
    }

    [Fact]
    public async Task LoginAsync_NoBrowser_NoPasteCallback_ThrowsWithInstructions()
    {
        _environment.Files.Clear();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => LoginAsync(new SteamLoginService()));

        Assert.Equal(
            "No Chrome, Edge, Brave or Chromium browser was found. " + SteamLoginService.PasteInstructions
                                                                     + " Login address: " + ExpectedLoginUrl,
            ex.Message);
    }

    [Fact]
    public async Task LoginAsync_OnlySnapBrowser_SaysWhyBeforePasting()
    {
        var linux = new FakeBrowserEnvironment
        {
            HomeDirectory = _temp
        };
        linux.Variables["PATH"] = "/snap/bin";
        linux.Files.Add("/snap/bin/chromium");
        linux.Files.Add("/snap/bin/brave-browser");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SteamLoginService().LoginAsync(null, linux, _launcher, CancellationToken.None).WaitAsync(TestTimeout));

        Assert.StartsWith("Snap browsers are not supported", ex.Message, StringComparison.Ordinal);
        Assert.Equal(
            "Snap browsers are not supported (/snap/bin/chromium, /snap/bin/brave-browser). "
            + SteamLoginService.PasteInstructions + " Login address: " + ExpectedLoginUrl,
            ex.Message);
        Assert.Equal(0, _launcher.Calls);
    }

    [Fact]
    public async Task LoginAsync_OnlySnapBrowser_TellsThePasteCallbackWhy()
    {
        var linux = new FakeBrowserEnvironment
        {
            HomeDirectory = _temp
        };
        linux.Variables["PATH"] = "/snap/bin";
        linux.Files.Add("/snap/bin/chromium");
        linux.Files.Add("/snap/bin/brave-browser");
        string? reason = null;
        var service = new SteamLoginService
        {
            Options = new SteamLoginOptions
            {
                ReadPastedAddress = (why, _) =>
                {
                    reason = why;
                    return Task.FromResult(PastedAddress);
                },
            },
        };

        var result = await service.LoginAsync(null, linux, _launcher, CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Equal("abc", result.Token);
        Assert.Equal("Snap browsers are not supported (/snap/bin/chromium, /snap/bin/brave-browser).", reason);
        Assert.Equal(0, _launcher.Calls);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("nope.")]
    public async Task LoginAsync_LaunchFails_FallsBackToPasteAndDeletesProfile(string failure)
    {
        bool? profileExistedAtPaste = null;
        string? reason = null;
        _launcher.Failure = new InvalidOperationException(failure);
        var service = new SteamLoginService
        {
            Options = new SteamLoginOptions
            {
                ReadPastedAddress = (why, _) =>
                {
                    reason = why;
                    profileExistedAtPaste = Directory.Exists(_launcher.ProfileDirectory);
                    return Task.FromResult(PastedAddress);
                },
            },
        };

        var result = await LoginAsync(service);

        Assert.Equal(7UL, result.SteamId);
        Assert.Equal("abc", result.Token);
        Assert.Equal("Could not start Google Chrome: nope.", reason);
        Assert.Equal(1, _launcher.Calls);
        Assert.False(profileExistedAtPaste);
        Assert.Empty(Directory.GetDirectories(ProfileRoot));
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("nope.")]
    public async Task LoginAsync_LaunchFails_NoPasteCallback_SaysWhichBrowserFailed(string failure)
    {
        _launcher.Failure = new InvalidOperationException(failure);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => LoginAsync(new SteamLoginService()));

        Assert.StartsWith("Could not start Google Chrome: nope.", ex.Message, StringComparison.Ordinal);
        Assert.Equal(
            "Could not start Google Chrome: nope. " + SteamLoginService.PasteInstructions + " Login address: " +
            ExpectedLoginUrl,
            ex.Message);
        Assert.Empty(Directory.GetDirectories(ProfileRoot));
    }

    [Fact]
    public async Task LoginAsync_ProfileRootUnusable_FallsBackToPasteWithoutLaunching()
    {
        // The profile root lies under a file, so the profile directory cannot be created.
        _environment.TempDirectory = CreateBlockingFile();
        string? reason = null;
        var service = new SteamLoginService
        {
            Options = new SteamLoginOptions
            {
                ReadPastedAddress = (why, _) =>
                {
                    reason = why;
                    return Task.FromResult(PastedAddress);
                },
            },
        };

        var result = await LoginAsync(service);

        Assert.Equal(new SteamLoginResult(7, "abc"), result);
        Assert.Matches(@"^Could not create the temporary browser profile: [^\r\n]*[^.]\.$", reason);
        Assert.Equal(0, _launcher.Calls);
        Assert.True(File.Exists(_environment.TempDirectory));
    }

    [Fact]
    public async Task LoginAsync_ProfileRootUnusable_NoPasteCallback_SaysWhy()
    {
        _environment.TempDirectory = CreateBlockingFile();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => LoginAsync(new SteamLoginService()));

        Assert.StartsWith("Could not create the temporary browser profile: ", ex.Message, StringComparison.Ordinal);
        Assert.EndsWith(". " + SteamLoginService.PasteInstructions + " Login address: " + ExpectedLoginUrl, ex.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, _launcher.Calls);
    }

    [Fact]
    public async Task LoginAsync_BrowserRejectsCommand_ThrowsWithoutDebuggingDetails()
    {
        _launcher.Transport.FailCommand("Page.navigate", "x");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => LoginAsync(new SteamLoginService()));

        Assert.Equal("The browser rejected a debugging command during the Steam login.", ex.Message);
        var inner = Assert.IsType<CdpException>(ex.InnerException);
        Assert.Equal("x", inner.Message);
        Assert.Equal("Browser.close", Methods()[^1]);
        Assert.True(_launcher.Session!.Disposed);
        Assert.Empty(Directory.GetDirectories(ProfileRoot));
    }

    [Fact]
    public async Task LoginAsync_BrowserShutdownFails_StillReturnsTokenAndDeletesProfile()
    {
        _launcher.SessionDisposeFailure = new InvalidOperationException("stuck");
        RedirectAfterNavigate();

        var result = await LoginAsync(new SteamLoginService());

        Assert.Equal("a+b", result.Token);
        Assert.True(_launcher.Session!.Disposed);
        Assert.Empty(Directory.GetDirectories(ProfileRoot));
    }

    [Fact]
    public async Task DeleteProfileAsync_StillHeldAfterRetries_LeavesItWithoutThrowing()
    {
        // The next login's sweep removes it; a valid login must not fail because of it.
        var profile = LoginProfile.Create(Path.Combine(_temp, "root"), _ => throw new IOException("held"));

        await SteamLoginService.DeleteProfileAsync(profile, 2, TimeSpan.Zero).WaitAsync(TestTimeout);

        Assert.True(Directory.Exists(profile.DirectoryPath));
    }

    private string CreateBlockingFile()
    {
        Directory.CreateDirectory(_temp);
        var file = Path.Combine(_temp, "not-a-directory");
        File.WriteAllText(file, string.Empty);
        return file;
    }

    private List<string> Methods() => [.. _launcher.Transport.Sent.Select(c => (string)c["method"]!)];

    private Task<SteamLoginResult> LoginAsync(SteamLoginService service) =>
        service.LoginAsync(null, _environment, _launcher, CancellationToken.None).WaitAsync(TestTimeout);

    private void RedirectAfterNavigate()
    {
        // Answers Page.navigate with Facepunch's final /app redirect, at the response stage.
        _launcher.Transport.OnCommand = command =>
        {
            if ((string)command["method"]! != "Page.navigate")
            {
                return;
            }

            _launcher.Transport.Push(new JsonObject
            {
                ["method"] = "Fetch.requestPaused",
                ["params"] = new JsonObject
                {
                    ["requestId"] = "final",
                    ["request"] = new JsonObject
                    {
                        ["url"] =
                            "https://companion-rust.facepunch.com/app?returnUrl=rust-plus%3A%2F%2Frust-auth-session",
                        ["headers"] = new JsonObject(),
                    },
                    ["responseStatusCode"] = 302,
                    ["responseHeaders"] = new JsonArray(
                        new JsonObject
                        {
                            ["name"] = "Location", ["value"] = "rust-plus://rust-auth-session?steamId=7&token=a%2Bb"
                        }),
                },
            }.ToJsonString());
        };
    }
}
