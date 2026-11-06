# Steam Login via Facepunch's Rust+ App Redirect: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `SteamLoginService.LoginAsync` and `FcmRegistration.RegisterWithRustPlusAsync` work again after Facepunch's October 2026 login rework. The library drives a Chromium-family browser over the Chrome DevTools Protocol (CDP) and captures the `rust-plus://rust-auth-session?steamId=…&token=…` redirect. A paste fallback covers machines without such a browser.

**Architecture:**
- Pure, fully tested pieces: browser lookup, launch arguments, profile lifecycle, a minimal CDP session over a transport seam, and the Rust+ login conversation over CDP.
- `SteamLoginService` composes them behind an internal launcher seam. Only the real process launch and the real WebSocket connect are excluded from coverage.
- The loopback `HttpListener` flow is removed.

**Tech Stack:** C# (`LangVersion` latest), netstandard2.0 + net10.0, System.Text.Json 10 (`JsonNode`), `System.Net.WebSockets`, xUnit (test hosts net8.0 + net10.0).

**Spec:** `docs/superpowers/specs/2026-11-05-rust-plus-app-steam-login-design.md`. Read it first. Its "What we know" and spike-result sections are the evidence this plan builds on.

## Global Constraints

- Library TFMs stay `netstandard2.0; net10.0`. No new NuGet dependency. System.Text.Json and Microsoft.Bcl.AsyncInterfaces are already available; `System.Threading.Channels` is **not** referenced by `RustPlusApi.Fcm.Registration`, so don't use it.
- `TreatWarningsAsErrors` + `AnalysisLevel latest-all` + Roslynator/Sonar/VSTHRD. Every `Regex` needs a match timeout (Sonar S6444). Custom exceptions need the three standard constructors. Unread primary-constructor parameters (CS9113) need a targeted `#pragma` with a comment.
- Never start a process by bare program name with `UseShellExecute = false`. The .NET Unix launcher resolves bare names from the current directory first (see the existing `BrowserStartInfo_ShellExecutesTheUrlItself_NotABareOpenerName` test). Browser and `flatpak` paths must be absolute.
- Never put the token, a `Location` value, a query string from Facepunch, or cookies into exception messages, logs or `Debug` output. Paths and Facepunch URL *paths* are fine.
- CDP interception covers **only** `https://companion-rust.facepunch.com/app*` (Request + Response stages) and `https://companion-rust.facepunch.com/login-failed*` (Request stage). No script injection, no clicking, no other page changes.
- Facepunch constants, copied from the spec:
  - login URL: `https://companion-rust.facepunch.com/login?returnUrl=%2Fapp%3FreturnUrl%3Drust-plus%3A%2F%2Frust-auth-session`
  - redirect prefix: `rust-plus://rust-auth-session`
  - Windows detection: substring `windows`, case-insensitive
- Existing public signatures stay source-compatible: `SteamLoginService(int port = 3000)`, `SteamLoginService.LoginAsync(Action<string>?, CancellationToken)`, `BuildLoginUrl(string)`, `ParseCallback(Uri)`, `FcmRegistration(HttpClient?, int steamLoginPort = 3000)`, `RegisterWithRustPlusAsync(Credentials, Action<string>?, CancellationToken)`. Don't use `[Obsolete]`.
- Coverage is 100% line and branch except members listed in `docs/development/testing.md` with a justification. Both TFMs run the same tests. Assertions are exact.
- Work on branch `fix/steam-login-rust-plus-app`, created from `develop` by the main session before Task 1. Subagents never run `git checkout`, `switch`, `reset` or `stash`.
- **Commits:** the owner does not want commits unless they have asked. A "Commit" step means stage with `git add` and commit **only if the owner has authorised commits for this execution**; otherwise stage and stop.
- Use `dtk dotnet …` instead of bare `dotnet …` for build, test and publish (project convention).

## Review Focus

1. **A Windows profile path with spaces and non-ASCII characters** (`C:\Users\Jöhn Smith\AppData\Local\Temp\…`) must reach the browser as a single argument. Pinned in Task 2 (`BuildStartInfo_QuotesProfilePathWithSpacesAndNonAscii`).
2. **`Location` header casing** (`Location`, `location`, `LOCATION`) must all be recognised. Pinned in Task 5 (`RunAsync_FindsLocationHeaderCaseInsensitively`).
3. **A token with URL-encoded `+`, `/` and `=`** must decode byte-exact; real tokens contain all three. Pinned in Task 1 (`ParseCallback_RustPlusRedirect_DecodesTokenExactly`).
4. **Two logins at once** (two terminals) must not sweep each other's live profile. Pinned in Task 3 (`SweepStale_SkipsProfileWhoseLockIsHeld`).
5. **The player closes the browser window mid-login** must give a clear error, and the profile must still be deleted. Pinned in Task 6 (`LoginAsync_WindowClosed_ThrowsAndDeletesProfile`).

---

### Task 1: Constants, redirect and paste parsing, UA rewrite

**Files:**
- Modify: `src/RustPlusApi.Fcm.Registration/RegistrationConstants.cs` (after `SteamLoginUrl`)
- Modify: `src/RustPlusApi.Fcm.Registration/Steps/SteamLoginService.cs` (add `ParsePastedAddress`, `PasteInstructions`)
- Create: `src/RustPlusApi.Fcm.Registration/Steps/Browser/UserAgentRewriter.cs`
- Test: `tests/RustPlusApi.Fcm.Registration.UnitTests/SteamLoginServiceTests.cs`, `tests/RustPlusApi.Fcm.Registration.UnitTests/UserAgentRewriterTests.cs`

**Interfaces:**
- Produces:
  - `RegistrationConstants.RustPlusAppReturnUrl = "/app?returnUrl=rust-plus://rust-auth-session"`
  - `RegistrationConstants.RustPlusAuthRedirectPrefix = "rust-plus://rust-auth-session"`
  - `RegistrationConstants.CompanionAppUrlPrefix = "https://companion-rust.facepunch.com/app"`
  - `RegistrationConstants.CompanionLoginFailedUrlPrefix = "https://companion-rust.facepunch.com/login-failed"`
  - all `public const string`
- Produces: `public static SteamLoginResult SteamLoginService.ParsePastedAddress(string pasted)`.
- Produces: `public const string SteamLoginService.PasteInstructions`.
- Produces: `internal static class UserAgentRewriter` with:
  - `bool SaysWindows(string? userAgent)`
  - `string ToNonWindows(string userAgent)`
  - `const string NonWindowsPlatformHint = "\"Linux\""`

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void ParseCallback_RustPlusRedirect_DecodesTokenExactly()
{
    var uri = new Uri("rust-plus://rust-auth-session?steamId=76561198249527954&token=eyJzIjoxfQ%3D%3D.a%2Bb%2Fc%3D");
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
public void ParsePastedAddress_RejectsAnythingButTheRustPlusRedirect(string pasted)
{
    var ex = Assert.Throws<InvalidOperationException>(() => SteamLoginService.ParsePastedAddress(pasted));
    Assert.Contains("rust-plus://rust-auth-session", ex.Message, StringComparison.Ordinal);
}

[Fact]
public void ParsePastedAddress_Null_ThrowsArgumentNull() =>
    Assert.Throws<ArgumentNullException>(() => SteamLoginService.ParsePastedAddress(null!));
```

`UserAgentRewriterTests`:
- `ToNonWindows_EdgeOnWindows`: input `Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36 Edg/142.0.0.0`, expected `Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36 Edg/142.0.0.0`.
- `ToNonWindows_StrayWindowsTokenOutsideParentheses_IsRemoved`: `Foo/1 WINDOWS Bar/2` becomes `Foo/1  Bar/2`.
- `ToNonWindows_LinuxUa_Unchanged`.
- `SaysWindows` theory: `null`, `""` and a Linux UA give `false`; `windows` and `(Windows NT 10.0)` give `true`.
- Every `ToNonWindows` output satisfies `Assert.False(UserAgentRewriter.SaysWindows(output))`.

`ParsePastedAddress_*` and `SaysWindows`/`ToNonWindows` will not compile until the code exists. `ParseCallback_RustPlusRedirect_DecodesTokenExactly` may already pass; it pins existing behaviour for the new URI shape.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet test RustPlusApi.sln --filter "FullyQualifiedName~SteamLoginServiceTests|FullyQualifiedName~UserAgentRewriterTests"`
Expected: build errors for the missing members.

- [ ] **Step 3: Implement**
- **Constants:** add the four constants with XML docs. Update `SteamLoginUrl`'s doc comment: the login page now ends in Facepunch's Rust+ app redirect.
- **`ParsePastedAddress`:**
  1. Null-check the input.
  2. Trim whitespace.
  3. Strip one optional leading `location:` (case-insensitive) and trim again.
  4. Strip one pair of surrounding double quotes.
  5. Require an ordinal-ignore-case `StartsWith(RustPlusAuthRedirectPrefix)`. Otherwise throw `InvalidOperationException`: `"That is not the Rust+ login address. Paste the address that starts with rust-plus://rust-auth-session."`
  6. Return `ParseCallback(new Uri(value))`.
- **`PasteInstructions`:** exact copy:
  `"Open the login address in any browser and sign in to Steam. Then open the browser's developer tools (F12), go to the Network panel, and find the last request whose name starts with app?returnUrl=rust-plus. Copy the value of its Location response header: it starts with rust-plus://rust-auth-session. On Windows, first switch the developer tools to a non-Windows user agent, otherwise Facepunch answers \"Login Failed\"."`
- **`UserAgentRewriter.ToNonWindows`:**
  1. Replace the first parenthesised group containing `windows` (case-insensitive) with `(X11; Linux x86_64)`.
  2. Remove any remaining `windows` (case-insensitive).
  3. Use regexes with a 1-second match timeout.

- [ ] **Step 4: Run the tests to verify they pass** (same command; expected: all pass on net8.0 and net10.0)

- [ ] **Step 5: Commit**: `fix(registration): parse Facepunch's rust-plus:// login redirect and pasted addresses`

---

### Task 2: Browser lookup and launch arguments

**Files:**
- Create: `src/RustPlusApi.Fcm.Registration/Steps/Browser/BrowserInfo.cs` (both records below)
- Create: `src/RustPlusApi.Fcm.Registration/Steps/Browser/IBrowserEnvironment.cs`
- Create: `src/RustPlusApi.Fcm.Registration/Steps/Browser/SystemBrowserEnvironment.cs`
- Create: `src/RustPlusApi.Fcm.Registration/Steps/Browser/BrowserLocator.cs`
- Create: `src/RustPlusApi.Fcm.Registration/Steps/Browser/BrowserLaunchArguments.cs`
- Test: `tests/RustPlusApi.Fcm.Registration.UnitTests/BrowserLocatorTests.cs`, `BrowserLaunchArgumentsTests.cs`, `TestHelpers/FakeBrowserEnvironment.cs`

**Interfaces:**
- Produces:
  - `internal sealed record BrowserInfo(string Name, string ExecutablePath, IReadOnlyList<string> PrefixArguments, string? FlatpakAppId)`
  - `internal sealed record BrowserSearchResult(BrowserInfo? Browser, IReadOnlyList<string> SkippedSnapPaths)`
- Produces: `internal interface IBrowserEnvironment` with:
  - `bool IsWindows { get; }`
  - `bool IsMacOS { get; }`
  - `string HomeDirectory { get; }`
  - `string TempDirectory { get; }`
  - `string? GetEnvironmentVariable(string name)`
  - `string GetFolderPath(Environment.SpecialFolder folder)`
  - `bool FileExists(string path)`
  - `bool DirectoryExists(string path)`
- Produces: `internal sealed class SystemBrowserEnvironment : IBrowserEnvironment` with `public static readonly SystemBrowserEnvironment Instance`. Use `RuntimeInformation.IsOSPlatform`, since `OperatingSystem.IsWindows` is not on netstandard2.0.
- Produces: `internal sealed class BrowserLocator(IBrowserEnvironment environment)` with `BrowserSearchResult Find(string? explicitPath)`.
- Produces: `internal static class BrowserLaunchArguments` with:
  - `string Quote(string value)`
  - `ProcessStartInfo BuildStartInfo(BrowserInfo browser, int port, string profileDirectory)`
  - `string GetProfileRoot(BrowserInfo browser, IBrowserEnvironment environment)`

- [ ] **Step 1: Write the failing tests**

`FakeBrowserEnvironment` is a settable in-memory implementation: `HashSet<string> Files`, `HashSet<string> Directories`, `Dictionary<string,string> Variables`, folder map. `BrowserLocatorTests` (exact paths):
- **Windows**, with `ProgramFilesX86=C:\PF86`, `ProgramFiles=C:\PF`, `LocalApplicationData=C:\Local` and only `C:\PF86\Microsoft\Edge\Application\msedge.exe` existing: `Browser` is `("Microsoft Edge", that path, [], null)`.
- **Windows order:** with Edge **and** `C:\PF\Google\Chrome\Application\chrome.exe` both present, Edge wins. Order:
  1. Edge `PF86`, then `PF`
  2. Chrome `PF`, then `PF86`, then `Local`
  3. Brave `PF\BraveSoftware\Brave-Browser\Application\brave.exe`, then `Local\BraveSoftware\Brave-Browser\Application\brave.exe`
- **macOS:** `/Applications/Google Chrome.app/Contents/MacOS/Google Chrome` is found. Order: Chrome, Microsoft Edge, Brave Browser, Chromium; same bundle pattern for each.
- **Linux `PATH`:** `PATH=/snap/bin:/usr/bin` with `/snap/bin/chromium` and `/usr/bin/google-chrome` both present gives `/usr/bin/google-chrome`, and `SkippedSnapPaths == ["/snap/bin/chromium"]`. Names in order: `google-chrome`, `google-chrome-stable`, `chromium`, `chromium-browser`, `microsoft-edge`, `microsoft-edge-stable`, `brave-browser`.
- **Linux Flatpak:** with no `PATH` browser, `/usr/bin/flatpak` present and `/var/lib/flatpak/app/com.google.Chrome` present, the result is `("Google Chrome", "/usr/bin/flatpak", ["run", "com.google.Chrome"], "com.google.Chrome")`. Also found under `<home>/.local/share/flatpak/app/<id>`. Ids in order: `com.google.Chrome`, `com.microsoft.Edge`, `com.brave.Browser`, `org.chromium.Chromium`.
- **Flatpak without a `flatpak` executable** on `PATH` gives `Browser == null`.
- **Only a Snap browser** gives `Browser == null` with one skipped path.
- **Explicit path** that exists gives `Name == Path.GetFileNameWithoutExtension(path)`. A missing explicit path throws `InvalidOperationException` whose message contains the path.
- `SystemBrowserEnvironment` smoke tests: `TempDirectory == Path.GetTempPath()`, and `FileExists` is true for a file the test creates.

`BrowserLaunchArgumentsTests`:
- `Quote`:
  - `abc` → `abc`
  - `""` → `""` (two quote characters)
  - `C:\Program Files\x` → `"C:\Program Files\x"`
  - `a"b` → `"a\"b"`
  - `C:\a b\` → `"C:\a b\\"`
- `BuildStartInfo_QuotesProfilePathWithSpacesAndNonAscii`: for browser `("Microsoft Edge", @"C:\PF\msedge.exe", [], null)`, port `9333` and profile `C:\Users\Jöhn Smith\Temp\RustPlusApi.SteamLogin\p1`, assert:
  - `FileName == @"C:\PF\msedge.exe"`
  - `UseShellExecute == false`
  - stdout and stderr are redirected
  - `Arguments == "--remote-debugging-port=9333 \"--user-data-dir=C:\\Users\\Jöhn Smith\\Temp\\RustPlusApi.SteamLogin\\p1\" --no-first-run --no-default-browser-check --new-window about:blank"`
- **Flatpak `BuildStartInfo`:** `Arguments` starts with `run com.google.Chrome --remote-debugging-port=`.
- **`GetProfileRoot`:**
  - Flatpak: `Path.Combine(home, ".var", "app", "com.google.Chrome", "cache", "RustPlusApi.SteamLogin")`
  - otherwise: `Path.Combine(temp, "RustPlusApi.SteamLogin")`

- [ ] **Step 2: Run to verify failure:** `dtk dotnet test RustPlusApi.sln --filter "FullyQualifiedName~BrowserLocatorTests|FullyQualifiedName~BrowserLaunchArgumentsTests"`. Expected: build errors.

- [ ] **Step 3: Implement**
- `Quote` follows the `CommandLineToArgvW` escaping rules. .NET parses `ProcessStartInfo.Arguments` the same way on Unix.
- `PATH` is split on `Path.PathSeparator` from `environment.GetEnvironmentVariable("PATH")`.
- A path is Snap when it starts with `/snap/`.
- The pre-#118 code is optional reference only: `git show 0c66a38^:src/RustPlusApi.Fcm.Registration/Steps/SteamLoginService.cs`.

- [ ] **Step 4: Run to verify the tests pass** on both TFMs.
- [ ] **Step 5: Commit**: `feat(registration): find Chromium-family browsers and build their launch arguments`

---

### Task 3: Login profile lifecycle

**Files:**
- Create: `src/RustPlusApi.Fcm.Registration/Steps/Browser/LoginProfile.cs`
- Test: `tests/RustPlusApi.Fcm.Registration.UnitTests/LoginProfileTests.cs` (each test uses its own temp root, deleted in `Dispose`)

**Interfaces:**
- Produces: `internal sealed class LoginProfile : IDisposable` with:
  - `static LoginProfile Create(string root)`
  - `string DirectoryPath { get; }`
  - `Task<bool> TryDeleteAsync(int attempts, TimeSpan delay)`
  - `static int SweepStale(string root)`
  - `const string LockFileName = "rustplusapi-login.lock"`

- [ ] **Step 1: Write the failing tests**
- `Create_MakesUniqueDirectoryUnderRootHoldingTheLock`: the directory exists under the root, its name is 32 hex characters, and the lock file exists. Opening the lock file with `FileShare.None` from the test throws `IOException`.
- `SweepStale_SkipsProfileWhoseLockIsHeld`: with a live profile, `SweepStale(root)` returns `0` and the directory still exists.
- `SweepStale_DeletesReleasedAndLockless`: one profile disposed (lock released) plus one bare directory with no lock file. `SweepStale` returns `2` and both are gone.
- `SweepStale_MissingRoot_ReturnsZero`.
- `TryDeleteAsync_DeletesDirectoryAndReleasesLock`: returns `true` and the directory is gone.
- `TryDeleteAsync_AlreadyGone_ReturnsTrue`.
- `TryDeleteAsync_StillLockedAfterRetries_ReturnsFalse`: the test holds a `FileStream` with `FileShare.None` on a file inside the directory. On Unix, .NET's share-mode emulation does not block deletion, so mark this test Windows-only by returning early when not on Windows. **Note this TFM/OS limitation in `testing.md`.**

- [ ] **Step 2: Run to verify failure:** `dtk dotnet test RustPlusApi.sln --filter "FullyQualifiedName~LoginProfileTests"`
- [ ] **Step 3: Implement**
- **`Create`:** `<root>/<Guid N>`; open `LockFileName` with `FileMode.CreateNew`, `FileAccess.ReadWrite`, `FileShare.None`, and keep the stream.
- **`Dispose`:** release the stream only.
- **`TryDeleteAsync`:** release the stream, then retry `Directory.Delete(recursive: true)` on `IOException` or `UnauthorizedAccessException`, waiting `delay` between attempts.
- **`SweepStale`:** for each subdirectory, a missing lock file means stale. Otherwise, try opening the lock exclusively; on success, close it and delete the directory. Swallow `IOException` and `UnauthorizedAccessException` per directory.
- [ ] **Step 4: Run to verify the tests pass.**
- [ ] **Step 5: Commit**: `feat(registration): temporary browser profiles with a sweep that spares live logins`

---

### Task 4: CDP plumbing (transport, session, DevTools endpoint)

**Files:**
- Create: `src/RustPlusApi.Fcm.Registration/Steps/Cdp/ICdpTransport.cs`
- Create: `src/RustPlusApi.Fcm.Registration/Steps/Cdp/WebSocketCdpTransport.cs`
- Create: `src/RustPlusApi.Fcm.Registration/Steps/Cdp/CdpSession.cs`
- Create: `src/RustPlusApi.Fcm.Registration/Steps/Cdp/CdpEvent.cs`
- Create: `src/RustPlusApi.Fcm.Registration/Steps/Cdp/CdpException.cs`
- Create: `src/RustPlusApi.Fcm.Registration/Steps/Cdp/DevToolsEndpoint.cs`
- Test: `tests/RustPlusApi.Fcm.Registration.UnitTests/CdpSessionTests.cs`, `WebSocketCdpTransportTests.cs`, `DevToolsEndpointTests.cs`, `TestHelpers/FakeCdpTransport.cs`

**Interfaces:**
- Produces: `internal interface ICdpTransport : IAsyncDisposable` with:
  - `Task SendAsync(string message, CancellationToken cancellationToken)`
  - `Task<string?> ReceiveAsync(CancellationToken cancellationToken)`, which returns `null` when the connection is closed.
- Produces: `internal sealed class WebSocketCdpTransport(WebSocket socket) : ICdpTransport`. It takes the abstract `WebSocket` and has `[ExcludeFromCodeCoverage] static Task<WebSocketCdpTransport> ConnectAsync(Uri uri, CancellationToken cancellationToken)`.
- Produces: `internal readonly record struct CdpEvent(string Method, JsonNode? Params)`.
- Produces: `internal sealed class CdpException : Exception` with the three standard constructors.
- Produces: `internal sealed class CdpSession : IAsyncDisposable`. Its constructor `CdpSession(ICdpTransport transport)` starts the read loop. Members:
  - `Task<JsonNode?> SendAsync(string method, JsonObject? parameters, CancellationToken cancellationToken)`, which returns the `result` node, throws `CdpException(error.message)` on an error reply, and throws `CdpException("The browser closed the debugging connection.")` if the connection closes while a reply is pending.
  - `Task<CdpEvent?> ReadEventAsync(CancellationToken cancellationToken)`, which returns `null` once the connection is closed and queued events are drained.
- Produces: `internal static class DevToolsEndpoint` with `Task<Uri> WaitForPageAsync(HttpClient http, int port, TimeSpan timeout, TimeSpan pollInterval, CancellationToken cancellationToken)`.
- Produces (test helper): `FakeCdpTransport : ICdpTransport`:
  - `List<JsonObject> Sent`
  - `void Push(string json)` delivers an incoming message
  - `void FailCommand(string method, string message)` makes the auto-reply an error for that method
  - `void Close()`
  - `Action<JsonObject>? OnCommand`, invoked after each auto-reply, so tests can push events in response to a command such as `Page.navigate`
  - every command gets an auto-reply of `{"id":<id>,"result":{}}` unless failed

- [ ] **Step 1: Write the failing tests**
- **`CdpSessionTests`:**
  - The first command's JSON is exactly `{"id":1,"method":"Page.enable","params":{}}`, and ids increase.
  - Replies are matched by id even when they arrive out of order.
  - An error reply throws `CdpException` with message `boom`.
  - Events are returned in order. Messages without an `id` are events.
  - After `Close()`, a pending `SendAsync` throws `CdpException` and `ReadEventAsync` returns `null` once queued events are drained.
  - A cancelled `ReadEventAsync` throws `OperationCanceledException`.
- **`WebSocketCdpTransportTests`:** build a connected client/server pair with `WebSocket.CreateFromStream` over a loopback `TcpListener` socket pair. This is available on the test hosts.
  - A message sent in three fragments is received as one string.
  - The server closing makes `ReceiveAsync` return `null`.
  - `SendAsync` reaches the server byte-exact.
- **`DevToolsEndpointTests`** (`StubHttpMessageHandler`):
  - Call 0 throws `HttpRequestException`, call 1 returns 404, call 2 returns a list without a `page`, call 3 returns `[{"type":"service_worker"},{"type":"page","webSocketDebuggerUrl":"ws://127.0.0.1:9333/devtools/page/X"}]`. The result is that URI, and every request URL is `http://127.0.0.1:9333/json/list`.
  - If no page appears within `timeout`, it throws `TimeoutException` whose message is `"The browser did not open its debugging port within 1 seconds."` (for `timeout = 1s`).
- [ ] **Step 2: Run to verify failure:** `dtk dotnet test RustPlusApi.sln --filter "FullyQualifiedName~CdpSessionTests|FullyQualifiedName~WebSocketCdpTransportTests|FullyQualifiedName~DevToolsEndpointTests"`
- [ ] **Step 3: Implement**
- **Event queue:** `CdpSession` runs one background read loop and queues events in a `ConcurrentQueue<CdpEvent>` signalled by a `SemaphoreSlim`. No Channels.
- **Pending replies:** `ConcurrentDictionary<int, TaskCompletionSource<JsonNode?>>`, created with `RunContinuationsAsynchronously`.
- **Sending:** serialise sends with a `SemaphoreSlim`.
- **Reading:** the transport reassembles fragments up to `EndOfMessage`. It treats a `Close` frame or a `WebSocketException` as closed.
- **`ConnectAsync`:** `ClientWebSocket` with `KeepAliveInterval = TimeSpan.Zero`.
- [ ] **Step 4: Run to verify the tests pass.**
- [ ] **Step 5: Commit**: `feat(registration): minimal Chrome DevTools Protocol session over a transport seam`

---

### Task 5: The Rust+ login conversation over CDP

**Files:**
- Create: `src/RustPlusApi.Fcm.Registration/Steps/Browser/RustPlusAppLogin.cs`
- Test: `tests/RustPlusApi.Fcm.Registration.UnitTests/RustPlusAppLoginTests.cs`

**Interfaces:**
- Consumes:
  - `CdpSession`, `CdpEvent` (Task 4)
  - `UserAgentRewriter` and the `RegistrationConstants.*` prefixes (Task 1)
  - `SteamLoginService.ParseCallback`
- Produces: `internal static class RustPlusAppLogin` with:
  - `Task<SteamLoginResult> RunAsync(CdpSession session, string loginUrl, CancellationToken cancellationToken)`
  - `const string DoneHtml`

- [ ] **Step 1: Write the failing tests** (drive `FakeCdpTransport` by pushing `Fetch.requestPaused` events)
- `RunAsync_EnablesFetchForAppAndLoginFailedOnly_ThenNavigates`: the first two commands sent, exactly:
  1. `Fetch.enable` with `patterns` = `[{"urlPattern":"https://companion-rust.facepunch.com/app*","requestStage":"Request"},{"urlPattern":"https://companion-rust.facepunch.com/app*","requestStage":"Response"},{"urlPattern":"https://companion-rust.facepunch.com/login-failed*","requestStage":"Request"}]`
  2. `Page.navigate` with `{"url":<loginUrl>}`
- `RunAsync_WindowsRequest_RewritesUserAgentAndPlatformOnly`: a request-stage pause with headers `User-Agent: …(Windows NT 10.0; Win64; x64)…`, `sec-ch-ua-platform: "Windows"` and `Cookie: c=1` produces `Fetch.continueRequest` with `headers` that:
  - holds exactly those three names;
  - has the UA equal to `UserAgentRewriter.ToNonWindows(original)`;
  - has the platform equal to `"\"Linux\""`;
  - leaves the cookie unchanged.
- `RunAsync_NonWindowsRequest_ContinuesWithoutHeaders`: `continueRequest` params are exactly `{"requestId":"r1"}`.
- `RunAsync_MiddleHopRedirect_Continues`: a response-stage pause with `302` to `/app?returnUrl=rust-plus://rust-auth-session` produces `Fetch.continueRequest {"requestId":…}` and keeps waiting.
- `RunAsync_ContinueRequestRejectedAtResponseStage_FallsBackToContinueResponse`: use `FailCommand("Fetch.continueRequest", "x")`. Then `Fetch.continueResponse` is sent with the same `requestId`.
- `RunAsync_FinalRedirect_ReturnsTokenAndServesDonePage`:
  - Given a `302` with `Location: rust-plus://rust-auth-session?steamId=7&token=a%2Bb`, the result is `(7, "a+b")`.
  - The last command is `Fetch.fulfillRequest` with `responseCode` 200, headers `Content-Type: text/html; charset=utf-8` and `Cache-Control: no-store`, and `body == Convert.ToBase64String(UTF8(DoneHtml))`.
  - **No** command navigates to `rust-plus:`.
- `RunAsync_FindsLocationHeaderCaseInsensitively`: theory over `Location`, `location` and `LOCATION`.
- `RunAsync_AppAnswersPage_ThrowsRefusal`: a response-stage `200` throws `InvalidOperationException` whose message starts with `"Facepunch refused the Steam login at its last step"`. When a Windows UA was rewritten earlier in the run, the message also contains `"reported as non-Windows"`. The page is continued, not fulfilled.
- `RunAsync_LoginFailedPage_ThrowsWithReason`: a request-stage pause on `https://companion-rust.facepunch.com/login-failed?returnUrl=&reason=cancelled` throws with a message containing `"reason: cancelled"` and `"Start the login again"`.
- `RunAsync_ConnectionClosed_ThrowsWindowClosed`: the message is `"The browser window was closed before the Steam login finished."`
- `RunAsync_Cancelled_ThrowsOperationCanceled`.
- `RunAsync_IgnoresUnrelatedEvents`: for example `Page.frameNavigated`.
- `RunAsync_NeverPutsTokenInExceptionMessages`: a final redirect whose token is `SECRETTOKEN` but whose `steamId` is invalid throws, and the message does not contain `SECRETTOKEN`.
- [ ] **Step 2: Run to verify failure:** `dtk dotnet test RustPlusApi.sln --filter "FullyQualifiedName~RustPlusAppLoginTests"`
- [ ] **Step 3: Implement** per the spec's Flow steps 4–6 with the per-request override (the spec's "Decision"). There is no whole-tab override.
- `responseHeaders` is an array of `{name,value}`; request `headers` is an object.
- A 3xx whose `Location` does not start with the prefix is continued.
- Any other non-3xx status at the response stage is continued, then throws. Use the 200 refusal message for 2xx and `"Facepunch answered HTTP <status> at its last step."` for the rest.
- `DoneHtml` is a minimal page saying `"Done. You can close this window and go back to your terminal."`
- [ ] **Step 4: Run to verify the tests pass.**
- [ ] **Step 5: Commit**: `fix(registration): capture the Rust+ token from Facepunch's app redirect over CDP`

---

### Task 6: Launcher seam, `SteamLoginService` rewrite, options, `FcmRegistration`

**Files:**
- Create: `src/RustPlusApi.Fcm.Registration/SteamLoginOptions.cs`
- Create: `src/RustPlusApi.Fcm.Registration/Steps/Browser/IBrowserLauncher.cs` (both interfaces)
- Create: `src/RustPlusApi.Fcm.Registration/Steps/Browser/ChromiumLauncher.cs`
- Modify: `src/RustPlusApi.Fcm.Registration/Steps/SteamLoginService.cs`. Remove the `HttpListener` flow, nonce, `SuccessHtml`/`FailureHtml`/`BuildNotFoundHtml`, `TryOpenBrowser`, `BuildBrowserStartInfo` and `RespondAsync`. Keep `BuildLoginUrl`, `ParseCallback`, `ParseQuery` and `GetFreePort`.
- Modify: `src/RustPlusApi.Fcm.Registration/FcmRegistration.cs`
- Test: `tests/RustPlusApi.Fcm.Registration.UnitTests/SteamLoginServiceTests.cs` (delete the listener and `BrowserStartInfo` tests), `FcmRegistrationTests.cs`, `TestHelpers/FakeBrowserLauncher.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–5.
- Produces: `public sealed class SteamLoginOptions` with:
  - `Func<CancellationToken, Task<string>>? ReadPastedAddress { get; init; }`
  - `string? BrowserPath { get; init; }`
  - XML docs on both
- Produces:
  - `internal interface IBrowserSession : IAsyncDisposable { ICdpTransport Transport { get; } }`
  - `internal interface IBrowserLauncher { Task<IBrowserSession> LaunchAsync(BrowserInfo browser, int port, string profileDirectory, CancellationToken cancellationToken); }`
- Produces: `[ExcludeFromCodeCoverage] internal sealed class ChromiumLauncher : IBrowserLauncher`:
  - starts the process with `BrowserLaunchArguments.BuildStartInfo` and drains stdout and stderr;
  - calls `DevToolsEndpoint.WaitForPageAsync` with an `HttpClient` that has `UseProxy = false`, a 45 s timeout and 300 ms polling, then `WebSocketCdpTransport.ConnectAsync`;
  - on session `DisposeAsync`, disposes the transport, waits up to 10 s for exit, then kills it. Use `Kill(entireProcessTree: true)` under `#if NET10_0_OR_GREATER` and `Kill()` otherwise.
- Produces: `public SteamLoginOptions SteamLoginService.Options { get; init; } = new();`
- Produces: `internal Task<SteamLoginResult> SteamLoginService.LoginAsync(Action<string>? onLoginUrl, IBrowserEnvironment environment, IBrowserLauncher launcher, CancellationToken cancellationToken)`. The public overload delegates with `SystemBrowserEnvironment.Instance` and `new ChromiumLauncher()`.
- Produces: `public SteamLoginOptions FcmRegistration.SteamLoginOptions { get; init; } = new();`. `RegisterWithRustPlusAsync` builds `new SteamLoginService { Options = SteamLoginOptions }` at call time.

- [ ] **Step 1: Write the failing tests**

`FakeBrowserLauncher` records its calls, returns a session wrapping a `FakeCdpTransport`, and can be told to throw. Each test uses a `FakeBrowserEnvironment` whose `TempDirectory` is a per-test temp folder, and the internal `LoginAsync` overload.
- `LoginAsync_ReportsLoginUrlFirst`: `onLoginUrl` receives exactly `https://companion-rust.facepunch.com/login?returnUrl=%2Fapp%3FreturnUrl%3Drust-plus%3A%2F%2Frust-auth-session`.
- `LoginAsync_Success_ReturnsTokenClosesBrowserDeletesProfile`:
  - The test pushes the final redirect once `Page.navigate` is seen.
  - The result is correct and `Browser.close` was sent.
  - The session was disposed.
  - `<temp>/RustPlusApi.SteamLogin` contains no directories afterwards.
  - The launcher received a `profileDirectory` under that root and a port > 0.
- `LoginAsync_WindowClosed_ThrowsAndDeletesProfile`: the transport closes mid-wait. The exception message is the window-closed message and no profile remains.
- `LoginAsync_Cancelled_DeletesProfile`.
- `LoginAsync_SweepsStaleProfilesBeforeLaunching`: a pre-existing lock-less directory under the root is gone by the time `LaunchAsync` is called.
- `LoginAsync_NoBrowser_UsesPastedAddress`: no browser in the environment, and `Options.ReadPastedAddress` returns `" rust-plus://rust-auth-session?steamId=7&token=abc "`. The result is `(7, "abc")` and the launcher is never called.
- `LoginAsync_NoBrowser_NoPasteCallback_ThrowsWithInstructions`: the message contains `SteamLoginService.PasteInstructions` and the login URL.
- `LoginAsync_OnlySnapBrowser_SaysWhyBeforePasting`: the message starts with `"Snap browsers are not supported"` and names the skipped path.
- `LoginAsync_LaunchFails_FallsBackToPasteAndDeletesProfile`: the launcher throws `InvalidOperationException("nope")`, and the paste callback is used. Without a callback, the message starts with `"Could not start Google Chrome: nope."`.
- `FcmRegistrationTests.SteamLoginOptions_DefaultsToEmptyOptions`, plus `init` round-trip.
- [ ] **Step 2: Run to verify failure:** `dtk dotnet test RustPlusApi.sln --filter "FullyQualifiedName~SteamLoginServiceTests|FullyQualifiedName~FcmRegistrationTests"`
- [ ] **Step 3: Implement** `LoginAsync` in this order:
  1. Report the URL.
  2. Find the browser. If none is found, go to the paste fallback with a reason: `"Snap browsers are not supported (<paths>)."` or `"No Chrome, Edge, Brave or Chromium browser was found."`.
  3. Sweep the profile root and create a profile.
  4. Launch. On a non-cancellation exception, delete the profile and go to the paste fallback with `"Could not start <Name>: <message>."`.
  5. Run `RustPlusAppLogin` inside `try`.
  6. In `finally`:
     - send `Browser.close` with its own 5 s timeout and swallow any failure;
     - dispose the CDP session, then the browser session;
     - delete the profile with `TryDeleteAsync(20, 500 ms)`;
     - on `false`, `Debug.WriteLine` the profile path.

  The paste fallback throws `InvalidOperationException($"{reason} {PasteInstructions} Login address: {loginUrl}")` when there is no callback. Otherwise it returns `ParsePastedAddress(await ReadPastedAddress(ct))`.
- Update the XML docs on `SteamLoginService` and `FcmRegistration`. Remove "loopback listener" wording. Document `port` and `steamLoginPort` as kept for compatibility and no longer used, each with a `#pragma warning disable CS9113` and a reason.
- [ ] **Step 4: Run to verify the tests pass**, then run the whole suite: `dtk dotnet test RustPlusApi.sln`. Expected: all pass on both TFMs, and the CredentialsWeb tests still compile and pass.
- [ ] **Step 5: Commit**: `fix(registration): drive the Steam login in a browser the library controls`. No `!`: every removed member was internal.

---

### Task 7: Sample, documentation, coverage justifications

**Files:**
- Modify: `samples/RustPlus.Register.ConsoleApp/Program.cs`
- Modify:
  - `samples/README.md`
  - `src/RustPlusApi.Fcm.Registration/README.md`
  - `docs/articles/credentials.md`
  - `docs/articles/troubleshooting.md`
  - `docs/articles/getting-started.md`
  - `docs/articles/introduction.md`
  - `apps/RustPlusApi.CredentialsWeb/README.md`
  - `docs/development/testing.md`
  - `CLAUDE.md` (line 69–70)
  - `AGENTS.md` (line 39)
  - `.github/copilot-instructions.md` (line 36)

**Interfaces:**
- Consumes: `FcmRegistration.SteamLoginOptions`, `SteamLoginOptions.ReadPastedAddress`, `SteamLoginService.PasteInstructions`.

- [ ] **Step 1: Sample**
- Create one `CancellationTokenSource`. `Console.CancelKeyPress` sets `e.Cancel = true` and cancels it. Pass the token to every awaited call.
- Construct `new FcmRegistration { SteamLoginOptions = new() { ReadPastedAddress = … } }`. The callback prints `SteamLoginService.PasteInstructions`, prompts `"Paste the address here: "`, and reads a line, returning `""` on end of input.
- Step 2's message becomes: `"2/4  Opening a browser window (Edge, Chrome, Brave or Chromium) for the Steam login: sign in there…"`.
- [ ] **Step 2: Run the sample's build:** `dtk dotnet build RustPlusApi.sln`. Expected: success with zero warnings.
- [ ] **Step 3: Docs.** These describe the console flow per the spec's "Docs to update":
  - The library opens its own browser window: Edge on Windows, otherwise Chrome, Brave or Chromium, including Flatpak.
  - It uses a fresh profile, so you sign in to Steam there.
  - Firefox-only, Snap and headless machines use the paste fallback.
  - The Windows step tells Facepunch the browser is not on Windows, which Facepunch appears to block deliberately, so it may break again.
  - `steamLoginPort` is unused.
  - The token lives 14 days.
  - If deleting the temporary profile fails, the login still succeeds and the next login sweeps it.
  - A process killed hard (debugger stop, `kill`) can leave the browser window open for the player to close.

  In `troubleshooting.md`:
  - replace "The browser doesn't open during registration" with the paste fallback;
  - add "Login Failed" (Windows block, or `reason=cancelled`: restart the login);
  - add "browser window closed".

  Passages about the credentials **website** get the spec's notice ("broken since Facepunch's October 2026 change; use the console tool until it is fixed") and lose any "recommended" wording. Do not rewrite them.
- [ ] **Step 4: `testing.md`**
  - Replace the `SteamLoginService.TryOpenBrowser` section and its residual-gap list with sections for `ChromiumLauncher` (whole class: real process, real port, real WebSocket) and `WebSocketCdpTransport.ConnectAsync`.
  - Update the `FcmRegistration.RegisterWithRustPlusAsync` justification to "drives a real browser and the live Companion endpoint".
  - Record the Windows-only `TryDeleteAsync_StillLockedAfterRetries_ReturnsFalse` limitation and any residual gap the coverage run reveals.
  - Update the `CLAUDE.md`, `AGENTS.md` and copilot lines to: "Steam login in a browser the library drives over the Chrome DevTools Protocol, capturing Facepunch's `rust-plus://` app redirect (`SteamLoginService`)".
- [ ] **Step 5: Commit**: `docs: describe the browser-driven Steam login and its paste fallback`

---

### Task 8: Verification and manual acceptance

- [ ] **Step 1:** `dtk dotnet build RustPlusApi.sln`. Expected: success, 0 warnings.
- [ ] **Step 2:** `dtk dotnet test RustPlusApi.sln`. Expected: all pass on net8.0 and net10.0.
- [ ] **Step 3:** `tools/coverage/report.sh`. Expected: both gates pass. `RustPlusApi.Fcm.Registration` classes are at 100/100 except the members justified in `testing.md`.
- [ ] **Step 4:** `cd tests/RustPlusApi.Fcm.Registration.UnitTests && dtk dotnet stryker --config-file stryker-config.json --project RustPlusApi.Fcm.Registration.csproj`. Expected: above the `break` threshold of 75; aim for ≥ 90. Record the score in `testing.md`'s achieved-scores table.
- [ ] **Step 5:** `dotnet tool restore && dotnet jb cleanupcode RustPlusApi.sln --profile="ReformatAndReorder"`, then `git status`. Expected: no further changes. The pre-push hook rejects otherwise.
- [ ] **Step 6: Manual acceptance on Linux.** The main session runs `samples/RustPlus.Register.ConsoleApp` in the background. The owner signs in to Steam in the Flatpak Chrome window, then pairs a server in game. Expected:
  - the sample prints the `ServerId` + `AuthToken` connection and `credentials.json`;
  - no folder remains under `~/.var/app/com.google.Chrome/cache/RustPlusApi.SteamLogin/`.
- [ ] **Step 7: Manual acceptance on Windows.** Publish the sample as a self-contained `win-x64` single file and hand it to the owner (as the spike was). The owner runs it with Edge and pairs a server. Expected:
  - same output as Step 6;
  - no folder remains under `%TEMP%\RustPlusApi.SteamLogin\`.
- [ ] **Step 8:** Ask the owner whether to commit any remaining staged work and whether to push and open a PR to `develop`.
