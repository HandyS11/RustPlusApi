# Browser-agnostic Steam Login Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Chrome DevTools Protocol Steam login in `RustPlusApi.Fcm.Registration` with a
plain `returnUrl` redirect flow, removing the Chrome/Chromium requirement from the library entirely.

**Architecture:** `SteamLoginService` splits into a pure half (`BuildLoginUrl`, `ParseCallback` —
public, no I/O, fully unit-testable) and an interactive half (`LoginAsync` — binds a loopback
`HttpListener`, reports the login URL, best-effort opens the default browser, awaits the redirect
Facepunch performs to `http://localhost:<port>/callback/<nonce>?steamId=…&token=…`). All CDP,
Chrome-discovery and Flatpak code is deleted.

**Tech Stack:** C# multi-targeting `netstandard2.0` + `net10.0`, xUnit, `System.Net.HttpListener`,
`System.Security.Cryptography.RandomNumberGenerator`.

**Spec:** `docs/superpowers/specs/2026-09-02-browser-agnostic-steam-login-design.md`

## Global Constraints

- **Both TFMs must compile and behave identically.** `netstandard2.0` has no `Convert.ToHexString`,
  no `[NotNullWhen]` on `string.IsNullOrEmpty`, no `CancellationTokenRegistration.DisposeAsync`.
  Use `is not { Length: > 0 }` patterns rather than `string.IsNullOrEmpty` where nullability must be
  proven, matching the existing convention in this package.
- **`dtk dotnet build` runs with `TreatWarningsAsErrors` and latest-all analyzers** (Roslynator, Sonar,
  VSTHRD). Any suppression needs an inline `Justification`.
- **Tests run on both hosts:** `dtk dotnet test RustPlusApi.sln` executes every test project under
  net8.0 (which resolves the netstandard2.0 asset) and net10.0.
- **Coverage gate:** `tools/coverage/report.sh` enforces line 95 / branch 90. Anything excluded
  needs a justification entry in `docs/development/testing.md`.
- **Formatting gate:** a pre-push hook runs `dotnet jb cleanupcode RustPlusApi.sln
  --profile="ReformatAndReorder"` and rejects the push if it changes anything. Run it before
  committing.
- **Do not bump versions in project files.** Local builds are always 1.0.0.
- **Namespaces:** `SteamLoginResult` goes in the root namespace `RustPlusApi.Fcm.Registration`
  (alongside `ServerPairing`); `SteamLoginService` stays in `RustPlusApi.Fcm.Registration.Steps`.
- **Test project:** `tests/RustPlusApi.Fcm.Registration.UnitTests` already has
  `InternalsVisibleTo` from the package csproj. No csproj changes are needed.

## File Structure

| File | Responsibility |
| --- | --- |
| `src/RustPlusApi.Fcm.Registration/SteamLoginResult.cs` | **New.** Record carrying `SteamId` + `Token` from the callback. |
| `src/RustPlusApi.Fcm.Registration/Steps/SteamLoginService.cs` | **Rewritten.** Pure URL build/parse + the interactive loopback flow. ~400 lines → ~180. |
| `src/RustPlusApi.Fcm.Registration/FcmRegistration.cs` | **Modified.** `RegisterWithRustPlusAsync` gains `onLoginUrl` and returns `SteamLoginResult`. |
| `tests/RustPlusApi.Fcm.Registration.UnitTests/SteamLoginServiceTests.cs` | **New.** Covers the pure half and the interactive loop offline. |
| `samples/RustPlus.Register.ConsoleApp/Program.cs` | **Modified.** Prints the login URL instead of announcing Chrome. |
| Docs (8 files) + `CLAUDE.md` + `docs/development/testing.md` | **Modified.** Task 5. |

---

### Task 1: Pure half — `SteamLoginResult`, `BuildLoginUrl`, `ParseCallback`

**Files:**
- Create: `src/RustPlusApi.Fcm.Registration/SteamLoginResult.cs`
- Modify: `src/RustPlusApi.Fcm.Registration/Steps/SteamLoginService.cs` (add the two static methods; leave the existing CDP code alone for now so the build stays green)
- Test: `tests/RustPlusApi.Fcm.Registration.UnitTests/SteamLoginServiceTests.cs` (create)

**Interfaces:**
- Consumes: `RegistrationConstants.SteamLoginUrl` (existing, `https://companion-rust.facepunch.com/login`).
- Produces:
  - `public sealed record SteamLoginResult { public ulong SteamId { get; init; } public string Token { get; init; } }` in namespace `RustPlusApi.Fcm.Registration`
  - `public static string SteamLoginService.BuildLoginUrl(string returnUrl)`
  - `internal static string SteamLoginService.BuildLoginUrl(string loginUrlBase, string returnUrl)`
  - `public static SteamLoginResult SteamLoginService.ParseCallback(Uri callbackUri)`

- [ ] **Step 1: Write the failing tests**

Create `tests/RustPlusApi.Fcm.Registration.UnitTests/SteamLoginServiceTests.cs`:

```csharp
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
    [InlineData("http://localhost:3000/callback?steamId=7")]                  // no token
    [InlineData("http://localhost:3000/callback?steamId=7&token=")]           // empty token
    [InlineData("http://localhost:3000/callback?steamId=7&token=%20%20")]     // whitespace token
    [InlineData("http://localhost:3000/callback?token=abc")]                  // no steamId
    [InlineData("http://localhost:3000/callback?steamId=nope&token=abc")]     // non-numeric steamId
    [InlineData("http://localhost:3000/callback?steamId=-1&token=abc")]       // negative steamId
    [InlineData("http://localhost:3000/callback")]                            // no query at all
    public void ParseCallback_InvalidCallback_Throws(string uri) =>
        Assert.Throws<InvalidOperationException>(() => SteamLoginService.ParseCallback(new Uri(uri)));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet test RustPlusApi.sln --filter "FullyQualifiedName~SteamLoginServiceTests"`
Expected: FAIL — compile errors, `BuildLoginUrl` / `ParseCallback` / `SteamLoginResult` do not exist.

- [ ] **Step 3: Create `SteamLoginResult`**

Create `src/RustPlusApi.Fcm.Registration/SteamLoginResult.cs`:

```csharp
namespace RustPlusApi.Fcm.Registration;

/// <summary>
/// The Steam identity captured from the Facepunch login redirect — the auth token needed to
/// register the device with Rust Companion, plus the Steam64 ID of the account that signed in.
/// </summary>
public sealed record SteamLoginResult
{
    /// <summary>The Steam64 ID of the account that signed in. Surfaces again later as
    /// <see cref="ServerPairing.PlayerId"/> once a server is paired.</summary>
    public ulong SteamId { get; init; }

    /// <summary>The Rust+ auth token handed back by the Facepunch login.</summary>
    public string Token { get; init; } = null!;
}
```

- [ ] **Step 4: Add the pure methods to `SteamLoginService`**

Add these members to `src/RustPlusApi.Fcm.Registration/Steps/SteamLoginService.cs`. Add
`using System.Globalization;` and `using System.Net;` (for `WebUtility`) to the file's usings if
absent. Leave every existing member in place for now.

```csharp
    /// <summary>Builds the Facepunch login URL that redirects back to <paramref name="returnUrl"/>
    /// with <c>steamId</c> and <c>token</c> appended as query parameters.</summary>
    /// <param name="returnUrl">The absolute URL Facepunch should redirect the browser back to.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="returnUrl"/> is blank.</exception>
    public static string BuildLoginUrl(string returnUrl) =>
        BuildLoginUrl(RegistrationConstants.SteamLoginUrl, returnUrl);

    /// <summary>Builds the login URL against an arbitrary base (the Facepunch login in production).</summary>
    /// <param name="loginUrlBase">The login page to send the user to.</param>
    /// <param name="returnUrl">The absolute URL Facepunch should redirect the browser back to.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="returnUrl"/> is blank.</exception>
    internal static string BuildLoginUrl(string loginUrlBase, string returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            throw new ArgumentException("The return URL must not be blank.", nameof(returnUrl));
        }

        return loginUrlBase + "?returnUrl=" + Uri.EscapeDataString(returnUrl);
    }

    /// <summary>Extracts the Steam identity from the callback Facepunch redirects the browser to.</summary>
    /// <param name="callbackUri">The full callback URI, including its query string.</param>
    /// <exception cref="InvalidOperationException">Thrown when the callback carries no usable
    /// <c>token</c> or <c>steamId</c> — i.e. when Facepunch has changed the callback contract.</exception>
    public static SteamLoginResult ParseCallback(Uri callbackUri)
    {
        var query = ParseQuery(callbackUri.Query);

        // Narrow with a pattern rather than string.IsNullOrEmpty: netstandard2.0's reference assembly
        // lacks the [NotNullWhen(false)] annotation, so only the pattern proves non-nullness on both TFMs.
        query.TryGetValue("token", out var token);
        if (token is not { Length: > 0 } || token.Trim().Length == 0)
        {
            throw new InvalidOperationException(
                "The Facepunch login callback carried no 'token' parameter. The login contract has likely "
                + "changed upstream — re-check RegistrationConstants against rustplus.js.");
        }

        if (!query.TryGetValue("steamId", out var steamIdText)
            || !ulong.TryParse(steamIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var steamId))
        {
            throw new InvalidOperationException(
                "The Facepunch login callback carried no usable 'steamId' parameter. The login contract has "
                + "likely changed upstream — re-check RegistrationConstants against rustplus.js.");
        }

        return new SteamLoginResult
        {
            SteamId = steamId, Token = token
        };
    }

    /// <summary>Parses a URI query string into its decoded key/value pairs. Later duplicates win.</summary>
    /// <param name="query">The query string, with or without its leading <c>?</c>.</param>
    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var trimmed = query.TrimStart('?');
        if (trimmed.Length == 0)
        {
            return result;
        }

        foreach (var pair in trimmed.Split('&'))
        {
            if (pair.Length == 0)
            {
                continue;
            }

            var separator = pair.IndexOf('=');
            if (separator < 0)
            {
                result[WebUtility.UrlDecode(pair)] = string.Empty;
                continue;
            }

            var key = WebUtility.UrlDecode(pair.Substring(0, separator));
            result[key] = WebUtility.UrlDecode(pair.Substring(separator + 1));
        }

        return result;
    }
```

Note `NumberStyles.None` is what rejects `-1` and `+7`; do not use the default `ulong.TryParse`
overload, which permits a leading sign and would let the negative-steamId test through.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dtk dotnet test RustPlusApi.sln --filter "FullyQualifiedName~SteamLoginServiceTests"`
Expected: PASS — 13 tests (2 facts + 3 theory cases + 2 facts + 7 theory cases) on both TFM hosts.

- [ ] **Step 6: Format and commit**

```bash
dotnet tool restore
dotnet jb cleanupcode RustPlusApi.sln --profile="ReformatAndReorder"
git add src/RustPlusApi.Fcm.Registration/SteamLoginResult.cs \
        src/RustPlusApi.Fcm.Registration/Steps/SteamLoginService.cs \
        tests/RustPlusApi.Fcm.Registration.UnitTests/SteamLoginServiceTests.cs
git commit -m "feat: add pure Steam login URL builder and callback parser"
```

---

### Task 2: Interactive half — rewrite `LoginAsync`, delete the CDP path

**Files:**
- Modify: `src/RustPlusApi.Fcm.Registration/Steps/SteamLoginService.cs`
- Test: `tests/RustPlusApi.Fcm.Registration.UnitTests/SteamLoginServiceTests.cs` (append)

**Interfaces:**
- Consumes: `BuildLoginUrl(loginUrlBase, returnUrl)`, `ParseCallback(Uri)`, `SteamLoginResult` from Task 1.
- Produces:
  - `public Task<SteamLoginResult> LoginAsync(Action<string>? onLoginUrl = null, CancellationToken cancellationToken = default)`
  - `internal Task<SteamLoginResult> LoginAsync(string loginUrlBase, Action<string>? onLoginUrl, bool openBrowser, CancellationToken cancellationToken)`

The `openBrowser` flag is the seam that makes the whole accept loop testable offline: tests pass
`false` so no `xdg-open` fires in CI, take the login URL from `onLoginUrl`, and drive the callback
themselves with an `HttpClient`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/RustPlusApi.Fcm.Registration.UnitTests/SteamLoginServiceTests.cs` (add
`using System.Net;`, `using System.Net.Http;` and `using System.Threading;` to the file's usings):

```csharp
    /// <summary>Starts the interactive flow with the browser suppressed and returns the login URL
    /// it reported, plus the running task.</summary>
    private static async Task<(Task<SteamLoginResult> Login, Uri ReturnUrl)> StartLoginAsync(
        SteamLoginService service,
        CancellationToken cancellationToken)
    {
        var reported = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var login = service.LoginAsync("https://example.invalid/login",
            url => reported.TrySetResult(url),
            openBrowser: false,
            cancellationToken);

        var loginUrl = await reported.Task;
        var query = new Uri(loginUrl).Query;
        var returnUrl = Uri.UnescapeDataString(query.Substring(query.IndexOf("returnUrl=", StringComparison.Ordinal)
                                                               + "returnUrl=".Length));
        return (login, new Uri(returnUrl));
    }

    [Fact]
    public async Task LoginAsync_ReturnsResultFromCallbackRedirect()
    {
        var service = new SteamLoginService(port: 0);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var (login, returnUrl) = await StartLoginAsync(service, cts.Token);

        using var http = new HttpClient();
        using var response = await http.GetAsync(new Uri($"{returnUrl}?steamId=7&token=abc"), cts.Token);

        var result = await login;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(7UL, result.SteamId);
        Assert.Equal("abc", result.Token);
    }

    [Fact]
    public async Task LoginAsync_IgnoresCallbackWithWrongNonce_ThenAcceptsTheRealOne()
    {
        var service = new SteamLoginService(port: 0);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var (login, returnUrl) = await StartLoginAsync(service, cts.Token);

        using var http = new HttpClient();
        var forged = new Uri($"{returnUrl.GetLeftPart(UriPartial.Authority)}/callback/forged?steamId=1&token=evil");
        using var forgedResponse = await http.GetAsync(forged, cts.Token);
        Assert.Equal(HttpStatusCode.NotFound, forgedResponse.StatusCode);
        Assert.False(login.IsCompleted);

        using var real = await http.GetAsync(new Uri($"{returnUrl}?steamId=7&token=abc"), cts.Token);

        var result = await login;
        Assert.Equal("abc", result.Token);
    }

    [Fact]
    public async Task LoginAsync_KeepsListeningAfterCallbackWithoutToken()
    {
        var service = new SteamLoginService(port: 0);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var (login, returnUrl) = await StartLoginAsync(service, cts.Token);

        using var http = new HttpClient();
        using var bad = await http.GetAsync(new Uri($"{returnUrl}?steamId=7"), cts.Token);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.False(login.IsCompleted);

        using var good = await http.GetAsync(new Uri($"{returnUrl}?steamId=7&token=abc"), cts.Token);

        var result = await login;
        Assert.Equal("abc", result.Token);
    }

    [Fact]
    public async Task LoginAsync_ReportsUrlPointingAtTheLoopbackCallback()
    {
        var service = new SteamLoginService(port: 0);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var (login, returnUrl) = await StartLoginAsync(service, cts.Token);

        Assert.Equal("localhost", returnUrl.Host);
        Assert.NotEqual(0, returnUrl.Port);
        Assert.StartsWith("/callback/", returnUrl.AbsolutePath, StringComparison.Ordinal);
        Assert.True(returnUrl.AbsolutePath.Length > "/callback/".Length);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => login);
    }

    [Fact]
    public async Task LoginAsync_Cancelled_Throws()
    {
        var service = new SteamLoginService(port: 0);
        using var cts = new CancellationTokenSource();
        var (login, _) = await StartLoginAsync(service, cts.Token);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => login);
    }

    [Fact]
    public async Task LoginAsync_PortAlreadyBound_ThrowsWithGuidance()
    {
        var occupied = new SteamLoginService(port: 0);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var (holder, returnUrl) = await StartLoginAsync(occupied, cts.Token);

        var conflicting = new SteamLoginService(returnUrl.Port);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => conflicting.LoginAsync("https://example.invalid/login", null, openBrowser: false, cts.Token));
        Assert.Contains("steamLoginPort: 0", ex.Message, StringComparison.Ordinal);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => holder);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test RustPlusApi.sln --filter "FullyQualifiedName~SteamLoginServiceTests"`
Expected: FAIL — compile errors; the `LoginAsync(string, Action<string>?, bool, CancellationToken)`
overload does not exist, and the existing `LoginAsync` returns `Task<string>`.

- [ ] **Step 3: Rewrite `SteamLoginService`**

Replace the whole of `src/RustPlusApi.Fcm.Registration/Steps/SteamLoginService.cs` with the
following. Keep the `BuildLoginUrl` / `ParseCallback` / `ParseQuery` members from Task 1 exactly as
written there.

```csharp
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace RustPlusApi.Fcm.Registration.Steps;

/// <summary>
/// Step 5 — interactive Steam login. Sends the user's own browser to the Facepunch login page with
/// a loopback <c>returnUrl</c>, and captures the <c>steamId</c> and <c>token</c> Facepunch appends
/// when it redirects back.
/// </summary>
/// <param name="port">The loopback port the callback listener binds to; <c>0</c> picks a free one.</param>
/// <remarks>
/// Any browser works — the flow is an ordinary redirect, with no page scripting involved. The
/// callback path carries a per-run nonce so a page the user happens to be browsing cannot feed a
/// token of its own choosing into the loopback listener.
/// </remarks>
public sealed class SteamLoginService(int port = 3000)
{
    private const string SuccessHtml =
        "<!doctype html><meta charset=\"utf-8\"><title>Rust+ login complete</title>"
        + "<script>history.replaceState(null,'',location.pathname);</script>"
        + "<h1>Done. You can close this window.</h1>";

    private const string FailureHtml =
        "<!doctype html><meta charset=\"utf-8\"><title>Rust+ login failed</title>"
        + "<h1>That callback carried no Rust+ token. Try the login link again.</h1>";

    /// <summary>Sends the user's browser to the Facepunch Steam login and returns the captured identity.</summary>
    /// <param name="onLoginUrl">Invoked with the login URL before the browser is opened, so callers
    /// can print it. Always invoked, including when a browser is opened successfully.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled before a callback arrives.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the callback port cannot be bound.</exception>
    public Task<SteamLoginResult> LoginAsync(Action<string>? onLoginUrl = null,
        CancellationToken cancellationToken = default) =>
        LoginAsync(RegistrationConstants.SteamLoginUrl, onLoginUrl, openBrowser: true, cancellationToken);

    /// <summary>Drives the login against an arbitrary login page, optionally without opening a browser.</summary>
    /// <param name="loginUrlBase">The login page to send the user to.</param>
    /// <param name="onLoginUrl">Invoked with the login URL before any browser is opened.</param>
    /// <param name="openBrowser">Whether to attempt to open the user's default browser.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled before a callback arrives.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the callback port cannot be bound.</exception>
    internal async Task<SteamLoginResult> LoginAsync(string loginUrlBase,
        Action<string>? onLoginUrl,
        bool openBrowser,
        CancellationToken cancellationToken)
    {
        // HttpListener cannot bind port 0, so a free port is resolved up front. The window between
        // resolving and binding is racy in theory and has never mattered in practice.
        var boundPort = port == 0 ? GetFreePort() : port;
        var nonce = CreateNonce();
        var callbackPath = "/callback/" + nonce;
        var returnUrl = string.Create(CultureInfo.InvariantCulture, $"http://localhost:{boundPort}{callbackPath}");

        using var listener = new HttpListener();
        listener.Prefixes.Add(string.Create(CultureInfo.InvariantCulture, $"http://localhost:{boundPort}/"));

        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            throw new InvalidOperationException(
                $"Could not bind the Steam login callback listener to http://localhost:{boundPort}/. "
                + $"Another process is probably using port {boundPort} — pass steamLoginPort: 0 to pick a "
                + "free port automatically.", ex);
        }

        // GetContextAsync takes no cancellation token: stopping the listener is the only way to
        // unblock the wait promptly, so cancellation must not have to wait for the next request.
#pragma warning disable RCS1261 // CancellationTokenRegistration.DisposeAsync is not available on netstandard2.0
        using var cancellationRegistration = cancellationToken.Register(listener.Stop);
#pragma warning restore RCS1261

        try
        {
            var loginUrl = BuildLoginUrl(loginUrlBase, returnUrl);
            onLoginUrl?.Invoke(loginUrl);
            if (openBrowser)
            {
                TryOpenBrowser(loginUrl);
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
                {
                    // The listener was stopped under the wait — by the cancellation registration
                    // above (expected; rethrow as cancellation) or by an unrelated teardown.
                    cancellationToken.ThrowIfCancellationRequested();
                    throw;
                }

                if (!string.Equals(context.Request.Url?.AbsolutePath, callbackPath, StringComparison.Ordinal))
                {
                    await RespondAsync(context, HttpStatusCode.NotFound, FailureHtml).ConfigureAwait(false);
                    continue;
                }

                SteamLoginResult result;
                try
                {
                    result = ParseCallback(context.Request.Url!);
                }
                catch (InvalidOperationException)
                {
                    await RespondAsync(context, HttpStatusCode.BadRequest, FailureHtml).ConfigureAwait(false);
                    continue;
                }

                await RespondAsync(context, HttpStatusCode.OK, SuccessHtml).ConfigureAwait(false);
                return result;
            }
        }
        finally
        {
            listener.Stop();
        }

        throw new OperationCanceledException(cancellationToken);
    }

    // --- Task 1 members go here: BuildLoginUrl x2, ParseCallback, ParseQuery ---

    /// <summary>Generates the per-run callback nonce as lowercase hex.</summary>
    private static string CreateNonce()
    {
        var bytes = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);

        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var value in bytes)
        {
            builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>Opens the user's default browser at <paramref name="url"/>, ignoring any failure.</summary>
    /// <param name="url">The login URL to open.</param>
    /// <remarks>Excluded from coverage: launches a real browser process, and failure is by design
    /// unobservable — the URL has already been reported through <c>onLoginUrl</c>, so a headless
    /// host simply opens it by hand.</remarks>
    [ExcludeFromCodeCoverage]
    private static void TryOpenBrowser(string url)
    {
        try
        {
            var startInfo = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new ProcessStartInfo(url)
                {
                    UseShellExecute = true
                }
                : new ProcessStartInfo(RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open", url)
                {
                    UseShellExecute = false
                };
            Process.Start(startInfo)?.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[{nameof(SteamLoginService)}] Could not open a browser: {ex.Message}");
        }
    }

    private static int GetFreePort()
    {
#pragma warning disable CA2000 // TcpListener is not IDisposable; Stop() + Server.Dispose() is the correct cleanup pattern.
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
#pragma warning restore CA2000
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
            listener.Server.Dispose();
        }
    }

    private static async Task RespondAsync(HttpListenerContext context, HttpStatusCode status, string html)
    {
        var buffer = Encoding.UTF8.GetBytes(html);
        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = buffer.Length;
#if NET10_0_OR_GREATER
        await context.Response.OutputStream.WriteAsync(buffer.AsMemory()).ConfigureAwait(false);
#else
        await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
#endif
        context.Response.Close();
    }
}
```

`string.Create(CultureInfo.InvariantCulture, $"…")` is available on netstandard2.0 only via
`DefaultInterpolatedStringHandler`, which it lacks. If the netstandard2.0 build fails on those two
lines, replace both with `FormattableString.Invariant($"…")`, which exists on both TFMs.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test RustPlusApi.sln --filter "FullyQualifiedName~SteamLoginServiceTests"`
Expected: PASS on both TFM hosts. If `LoginAsync_PortAlreadyBound_ThrowsWithGuidance` fails on
Windows with an `HttpListenerException` about URL reservations rather than the wrapped message,
that is the same exception type and the wrap still applies — check the message assertion, not the
type.

- [ ] **Step 5: Verify no Chrome code survives**

Run:
```bash
grep -rniE "chrome|chromium|flatpak|devtools|websocket|ReactNativeWebView" \
  src/RustPlusApi.Fcm.Registration/Steps/SteamLoginService.cs
```
Expected: no output.

- [ ] **Step 6: Format and commit**

```bash
dotnet jb cleanupcode RustPlusApi.sln --profile="ReformatAndReorder"
git add src/RustPlusApi.Fcm.Registration/Steps/SteamLoginService.cs \
        tests/RustPlusApi.Fcm.Registration.UnitTests/SteamLoginServiceTests.cs
git commit -m "feat!: replace CDP Steam login with a returnUrl redirect flow"
```

---

### Task 3: Thread `SteamLoginResult` through `FcmRegistration` and the sample

**Files:**
- Modify: `src/RustPlusApi.Fcm.Registration/FcmRegistration.cs:62-84`
- Modify: `samples/RustPlus.Register.ConsoleApp/Program.cs:23-25`
- Test: `tests/RustPlusApi.Fcm.Registration.UnitTests/FcmRegistrationTests.cs` (existing test must still pass unchanged)

**Interfaces:**
- Consumes: `SteamLoginService.LoginAsync(Action<string>?, CancellationToken)` and `SteamLoginResult` from Task 2.
- Produces: `public Task<SteamLoginResult> FcmRegistration.RegisterWithRustPlusAsync(Credentials credentials, Action<string>? onLoginUrl = null, CancellationToken cancellationToken = default)`

- [ ] **Step 1: Change the signature and body**

In `src/RustPlusApi.Fcm.Registration/FcmRegistration.cs`, replace the `RegisterWithRustPlusAsync`
declaration and its final three statements:

```csharp
    /// <summary>
    /// Steps 5–6: interactive Steam login, then register the device's Expo token with Rust
    /// Companion so it receives pairing pushes. Returns the captured Steam identity.
    /// </summary>
    /// <param name="credentials">Credentials obtained from <see cref="AcquireCredentialsAsync"/>.</param>
    /// <param name="onLoginUrl">Invoked with the Steam login URL before the browser is opened, so
    /// callers can print it — the flow still completes on a headless host if the user opens it by hand.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="credentials"/> is missing the Expo push token.</exception>
    /// <remarks>Excluded from coverage: post-guard flow drives live Steam login and Companion
    /// registration; the guard (missing ExpoPushToken) is unit-tested, the remainder is only
    /// validatable by a real run against the live endpoints.</remarks>
    [ExcludeFromCodeCoverage]
    public async Task<SteamLoginResult> RegisterWithRustPlusAsync(Credentials credentials,
        Action<string>? onLoginUrl = null,
        CancellationToken cancellationToken = default)
    {
        // Narrow with a pattern rather than string.IsNullOrEmpty: netstandard2.0's reference assembly lacks the
        // [NotNullWhen(false)] annotation, so only the pattern proves non-nullness to the compiler on both TFMs.
        var expoPushToken = credentials.ExpoPushToken;
        if (expoPushToken is not { Length: > 0 })
        {
            throw new InvalidOperationException(
                "Credentials are missing the Expo push token; call AcquireCredentialsAsync first.");
        }

        var steamLogin = await _steamLoginService.LoginAsync(onLoginUrl, cancellationToken).ConfigureAwait(false);
        await _rustCompanionClient
            .RegisterAsync(steamLogin.Token, expoPushToken, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return steamLogin;
    }
```

- [ ] **Step 2: Update the sample**

In `samples/RustPlus.Register.ConsoleApp/Program.cs`, replace lines 23-25 (the two `Console.WriteLine`
calls announcing Chrome and the bare `await registration.RegisterWithRustPlusAsync(credentials);`):

```csharp
Console.WriteLine("2/4  Opening your browser for the Steam login — sign in through Steam…");
var steamLogin = await registration.RegisterWithRustPlusAsync(credentials, onLoginUrl: url =>
{
    Console.WriteLine("     If your browser didn't open, visit this URL yourself:");
    Console.WriteLine($"     {url}");
});
Console.WriteLine($"     Signed in as {steamLogin.SteamId}.");
```

- [ ] **Step 3: Build and run the whole suite**

Run: `dotnet build && dotnet test RustPlusApi.sln`
Expected: PASS. `FcmRegistrationTests.RegisterWithRustPlusAsync_MissingExpoToken_Throws` calls the
method with one argument and still compiles against the new optional parameters.

- [ ] **Step 4: Format and commit**

```bash
dotnet jb cleanupcode RustPlusApi.sln --profile="ReformatAndReorder"
git add src/RustPlusApi.Fcm.Registration/FcmRegistration.cs samples/RustPlus.Register.ConsoleApp/Program.cs
git commit -m "feat!: return SteamLoginResult from RegisterWithRustPlusAsync"
```

---

### Task 4: Coverage exclusion list

**Files:**
- Modify: `docs/development/testing.md:183-196`

**Interfaces:**
- Consumes: the final shape of `SteamLoginService` from Task 2.

- [ ] **Step 1: Replace the `SteamLoginService` exclusion entry**

Replace the whole `### SteamLoginService (whole class)` section (heading, File, Justification and
the "No pure helpers were extractable" paragraph) with:

```markdown
### `SteamLoginService.TryOpenBrowser`

**File:** `src/RustPlusApi.Fcm.Registration/Steps/SteamLoginService.cs`

**Justification:** Launches a real browser process via the platform opener (`ShellExecute`,
`open`, `xdg-open`). There is nothing to assert: the method is deliberately best-effort and
swallows every failure, because the login URL has already been reported through the `onLoginUrl`
callback and a headless host completes the flow by opening it by hand.

The rest of the class is covered offline. `BuildLoginUrl` and `ParseCallback` are pure. The
interactive `LoginAsync` is driven through the `internal LoginAsync(loginUrlBase, onLoginUrl,
openBrowser, cancellationToken)` seam: tests pass `openBrowser: false`, read the login URL from
`onLoginUrl`, and drive the loopback callback with an `HttpClient`, covering the success path,
the nonce mismatch, a callback with no token, cancellation, and the port-already-bound wrap.
```

- [ ] **Step 2: Run the coverage gate**

Run: `tools/coverage/report.sh`
Expected: the CI gate passes (line 95 / branch 90). If a residual branch in `SteamLoginService` is
genuinely unreachable offline, add it to this exclusion list with its own justification rather than
contorting the code to reach it — and say which branch in the commit message.

- [ ] **Step 3: Commit**

```bash
git add docs/development/testing.md
git commit -m "docs: narrow the SteamLoginService coverage exclusion to the browser launch"
```

---

### Task 5: Documentation sweep

**Files:**
- Modify: `src/RustPlusApi.Fcm.Registration/README.md:35-38`
- Modify: `docs/articles/getting-started.md:8-10`
- Modify: `docs/articles/credentials.md` (Mermaid participant + step table row 5 + the "Steam login requires Chrome/Chromium" section, L75-111)
- Modify: `docs/articles/troubleshooting.md:51-87`
- Modify: `docs/articles/introduction.md:48`
- Modify: `docs/articles/recipes.md:198`
- Modify: `docs/articles/samples.md:23,30-31`
- Modify: `samples/README.md:21,28-29`
- Modify: `CLAUDE.md:61-62`

- [ ] **Step 1: `src/RustPlusApi.Fcm.Registration/README.md`**

Replace the first bullet under `## Requirements & caveats` (the three lines starting
`- **Steam login requires Chrome/Chromium.**`) with:

```markdown
- **Steam login opens your default browser.** The flow is an ordinary redirect: the login page is
  opened with a `returnUrl` pointing at a loopback listener, and Facepunch redirects back with the
  Steam id and auth token. Any browser works. If no browser can be opened (containers, SSH), the
  URL is handed to the `onLoginUrl` callback so you can open it yourself — including on another
  machine, since the callback is served from your own loopback address.
```

- [ ] **Step 2: `docs/articles/getting-started.md`**

Delete the entire `- **Google Chrome or Chromium** — …` prerequisite bullet (lines 8-10). It has no
replacement: there is no longer a browser prerequisite.

- [ ] **Step 3: `docs/articles/credentials.md`**

Three edits:

1. In the Mermaid block, change `participant St as Steam (via Chrome)` to
   `participant St as Steam (your browser)` and
   `App->>St: 5. Interactive Steam login (Chrome DevTools)` to
   `App->>St: 5. Interactive Steam login (browser redirect)`.
2. In the step table, change the row-5 result cell from `Steam auth token` to
   `Steam auth token + Steam64 id`.
3. In the code block, change the comment
   `// Steps 5–6: interactive Steam login (launches Chrome) + Rust Companion device registration.`
   to `// Steps 5–6: interactive Steam login (opens your browser) + Rust Companion device registration.`
4. Replace the entire `## Steam login requires Chrome/Chromium` section — heading, both paragraphs,
   the `### Browser discovery order` subsection and its five numbered items, through the
   `If no browser is found at all, LaunchChrome throws…` paragraph — with:

```markdown
## How the Steam login works

`SteamLoginService` binds an `HttpListener` on `http://localhost:<port>/` and sends the browser to:

```
https://companion-rust.facepunch.com/login?returnUrl=http://localhost:<port>/callback/<nonce>
```

Facepunch carries that `returnUrl` through the Steam OpenID round-trip and redirects the browser
back to it with the credentials appended:

```
http://localhost:<port>/callback/<nonce>?steamId=765611…&token=eyJhbGciOi…
```

Any browser works — nothing is injected into the page. `SteamLoginService.LoginAsync` opens your
default browser on a best-effort basis and always reports the URL through its `onLoginUrl`
callback first, so the flow still completes on a container or over SSH by opening the link by hand.

The callback path carries a per-run random nonce, and any request to a different path is answered
with a 404 and ignored, so a page you happen to be browsing cannot push a token of its own choosing
into the listener. The response page calls `history.replaceState` to strip the token from the URL
your browser records.

`port` defaults to `3000`; pass `steamLoginPort: 0` to `FcmRegistration` to have a free port picked
automatically if 3000 is taken.
```

- [ ] **Step 4: `docs/articles/troubleshooting.md`**

Replace the entire `## Chrome/Chromium not found during registration` section — through the
`See [Credentials](credentials.md#steam-login-requires-chromechromium) for the full discovery-order
documentation.` line — with:

```markdown
## The browser doesn't open during registration

**Symptom:** registration prints the Steam login URL and then waits, but no browser window appears.
Common on containers, SSH sessions, WSL and minimal desktop installs.

**Fix:** open the printed URL yourself, in any browser. It is not a degraded path — the callback is
served from your own machine's loopback address, so it works even if you open the link on a
different device that can reach `localhost:<port>` of the machine running registration.

`SteamLoginService` reports the URL through the `onLoginUrl` callback *before* attempting to open a
browser, and never fails just because no browser could be launched.

## Port already in use during registration

**Symptom:** `FcmRegistration.RegisterWithRustPlusAsync` throws `InvalidOperationException` saying
the callback listener could not bind to `http://localhost:3000/`.

**Fix:** pass a different port, or `0` to pick a free one automatically:

```csharp
var registration = new FcmRegistration(steamLoginPort: 0);
```
```

- [ ] **Step 5: `docs/articles/introduction.md:48`**

Change `an interactive Steam login via Chrome DevTools,` to
`an interactive Steam login in your own browser,`.

- [ ] **Step 6: `docs/articles/recipes.md:198`**

Change `Console.WriteLine("No credentials found — running registration flow (Chrome will open).");`
to `Console.WriteLine("No credentials found — running registration flow (your browser will open).");`

- [ ] **Step 7: `docs/articles/samples.md`**

Change line 23 `GCM/Firebase/FCM/Expo registration, opens **Chrome/Chromium** for the Steam login, registers with`
to `GCM/Firebase/FCM/Expo registration, opens **your default browser** for the Steam login, registers with`.

Replace the two-line bullet at lines 30-31 (`- Requires Chrome or Chromium (native or Flatpak; …`
through `[discovery](credentials.md#browser-discovery-order)). Firefox/Safari won't work.`) with:

```markdown
- Works with any browser. If none opens, the sample prints the login URL — open it yourself.
```

- [ ] **Step 8: `samples/README.md`**

Change line 21 `GCM/Firebase/FCM/Expo registration, opens **Chrome/Chromium** for Steam login, registers with`
to `GCM/Firebase/FCM/Expo registration, opens **your default browser** for Steam login, registers with`.

Replace the two-line bullet at lines 28-29 (`- **Requires Chrome or Chromium** …` through
`Firefox/Safari won't work — the Steam step drives Chrome via the DevTools protocol.`) with:

```markdown
- **Any browser works.** If none opens (container, SSH), the sample prints the login URL — open it
  yourself and the flow continues.
```

- [ ] **Step 9: `CLAUDE.md:61-62`**

Change `CLI: GCM check-in → Firebase/FCM/Expo registration (`Steps/`), Steam login via Chrome DevTools
Protocol (`SteamLoginService`), Rust Companion registration, `CredentialsStore` persistence.` to:

```markdown
  CLI: GCM check-in → Firebase/FCM/Expo registration (`Steps/`), Steam login via a browser redirect
  to a loopback callback (`SteamLoginService`), Rust Companion registration, `CredentialsStore`
  persistence.
```

- [ ] **Step 10: Verify no stale Chrome claims or dead anchors remain**

Run:
```bash
grep -rniE "chrome|chromium|flatpak|CHROME_PATH|ReactNativeWebView|DevTools" \
  --exclude-dir=.git --exclude-dir=obj --exclude-dir=bin --exclude-dir=_site \
  --exclude-dir=plans --exclude-dir=specs \
  docs/ samples/ src/ CLAUDE.md README.md
grep -rn "steam-login-requires-chromechromium\|browser-discovery-order" \
  --exclude-dir=.git --exclude-dir=_site docs/ samples/
```
Expected from the first command: only the GCM check-in device-identity matches —
`RegistrationConstants.ChromeVersion`, `Protobuf/CheckinContracts.cs`, `AndroidFcmRegister.cs`.
These describe the *spoofed device* sent to Google and have nothing to do with an installed
browser. Expected from the second: no output (both anchors are gone, so any surviving link to them
would be dead).

- [ ] **Step 11: Build the docs site and commit**

Run: `docfx docs/docfx.json` (skip if `docfx` is not installed; note it in the commit message)
Expected: no warnings about broken links.

```bash
git add docs/ samples/README.md src/RustPlusApi.Fcm.Registration/README.md CLAUDE.md
git commit -m "docs: drop the Chrome requirement from the credential flow"
```

---

### Task 6: Live verification (human, not automatable)

This task requires a real Steam account and cannot be completed by an agent. Stop here and hand
back to the user.

**Interfaces:**
- Consumes: everything from Tasks 1-5.

- [ ] **Step 1: Confirm the nonce survives the Facepunch round-trip**

The spec flags this as unverified: the manual check used a bare `/callback`, while the
implementation uses `/callback/<nonce>`.

Run: `dotnet run --project samples/RustPlus.Register.ConsoleApp`
Complete the Steam login. Expected: the browser lands on `http://localhost:3000/callback/<nonce>?steamId=…&token=…`
and the sample proceeds to step 3/4.

**Outcome (verified 2026-09-03):** Facepunch preserves the path segment. A full registration
completed against the live endpoint through **Firefox**, on a machine with no Chromium-family
binary on `PATH`. The fallback described below was not needed. The final review additionally made
a path mismatch diagnosable (the 404 body and a `Debug.WriteLine` name the expected and received
paths) rather than presenting as a silent hang.

**If Facepunch strips or normalises the path segment**, the callback will 404 and the sample will
hang. Fall back to carrying the nonce in the `returnUrl` query string
(`http://localhost:<port>/callback?n=<nonce>`), match on `context.Request.Url.AbsolutePath ==
"/callback"` plus an `n` parameter equal to the nonce, and note in `ParseQuery`'s tests that
Facepunch's own parameters are appended after it. Update the Task 4 exclusion note and the Task 5
`credentials.md` URLs to match.

- [ ] **Step 2: Verify the no-Chromium success criterion**

On a machine (or container) with **no Chromium-family browser installed** and Firefox as the
default, run the sample again through to a completed pairing.
Expected: full registration, `rustplus.config.json` written, `new RustPlus(new RustPlusConnection(…))`
line printed.

- [ ] **Step 3: Verify the headless fallback**

Run the sample with the browser open sabotaged — e.g. `PATH=/usr/bin/env dotnet run --project
samples/RustPlus.Register.ConsoleApp` on Linux so `xdg-open` cannot be resolved, or simply close the
window that opens — then paste the printed URL into a browser by hand.
Expected: identical outcome. The sample must not have thrown when the browser failed to open.

- [ ] **Step 4: Full gate**

```bash
dotnet build
dotnet test RustPlusApi.sln
tools/coverage/report.sh
dotnet jb cleanupcode RustPlusApi.sln --profile="ReformatAndReorder" && git diff --exit-code
```
Expected: all clean, and the `git diff --exit-code` confirms the formatter changed nothing (the
pre-push hook rejects the push otherwise).

- [ ] **Step 5: PR**

The PR targets `develop`. The body must call out the **breaking changes**, since the repo has no
CHANGELOG:

- `SteamLoginService.LoginAsync` returns `SteamLoginResult` instead of `string`, and takes an
  optional `Action<string>? onLoginUrl` as its first parameter.
- `FcmRegistration.RegisterWithRustPlusAsync` returns `SteamLoginResult` instead of `string`, and
  gained an optional `onLoginUrl` parameter *before* `cancellationToken` — positional calls passing
  a `CancellationToken` second must switch to a named argument.
- The `CHROME_PATH` environment variable is no longer read.

