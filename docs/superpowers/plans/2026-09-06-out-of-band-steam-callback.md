# Out-of-band Steam callback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let `RustPlusApi.CredentialsWeb` run as a public website again, by having the visitor paste
the Facepunch callback address back instead of relying on a redirect that only fires for loopback,
and let them reveal and copy their credentials rather than only download them.

**Architecture:** A request is *local* when its connection address is loopback **and** its `Host`
header names a loopback host. Local requests keep today's automatic redirect, with the return URL
derived from the request itself. Every other request gets a return URL pointing at a loopback port
nothing is listening on; the visitor's browser fails to connect, shows the address, and they paste it
into a new `POST /api/callback`. The pairing wait is offered only to local visitors. `PublicBaseUrl`
and `AllowInsecureBaseUrl` are deleted, because nothing needs the external origin any more.

**Tech Stack:** .NET 10 minimal API, xUnit + `WebApplicationFactory` + `FakeTimeProvider`, hand-written
static HTML/CSS/JS with no build step.

**Spec:** `docs/superpowers/specs/2026-09-06-out-of-band-steam-callback-design.md`

## Global Constraints

- **The app is `net10.0` only**, `IsPackable=false`. Its test project targets `net10.0` alone. The
  netstandard2.0 multi-TFM parity rule that governs `src/` does not apply here.
- **Nothing under `src/` changes.** `SteamLoginService.BuildLoginUrl` and
  `SteamLoginService.ParseCallback` are already public and already do what this needs.
- **`TreatWarningsAsErrors` with `AnalysisLevel=latest-all`** (Roslynator, Sonar, VSTHRD). A warning
  is a build failure. Suppress with `#pragma` plus a comment saying why, as the existing code does.
- **Every type and member in the app is `internal`**, and carries XML doc comments in the house
  style: what it does, and why where the why is not obvious.
- **All logging goes through `[LoggerMessage]` source-generated partial methods** (CA1848 is
  enforced). Mirror `apps/RustPlusApi.CredentialsWeb/Flow/CredentialFlowLog.cs`.
- **No secret ever reaches a log record.** `SecretsAreNeverLoggedTests` asserts this; Task 9 extends
  it to the new path.
- **Coverage gate: line 95 / branch 90** for the app assembly, checked by `tools/coverage/report.sh`.
  Do not write catch blocks for exceptions that cannot occur; an unreachable branch costs coverage.
- **Formatting:** `dotnet tool restore` then
  `dotnet jb cleanupcode RustPlusApi.sln --profile="ReformatAndReorder"` must produce no diff. It
  also reorders members, so run it before committing and take whatever ordering it produces. The
  committed pre-push hook rejects a push that the formatter would change.
- **Test command:** always scope a filtered run to the project. Verified 2026-09-06 in this
  repo: `dotnet test RustPlusApi.sln --filter ...` reports **"0 tests found" with exit code 0**,
  so a filtered solution-level run looks green while running nothing. It fails the same way with
  the rtk wrapper bypassed, so it is the toolchain, not the proxy. `CLAUDE.md` still documents the
  solution form; do not use it.
  - Fast loop: `dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests/RustPlusApi.CredentialsWeb.UnitTests.csproj --filter "FullyQualifiedName~SomeTests"`
  - Whole app suite: `dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests/RustPlusApi.CredentialsWeb.UnitTests.csproj`
  - Everything, before the final commit: `dotnet test RustPlusApi.sln` (unfiltered, works)
  - **A run reporting "0 tests found" is a failed verification, never a pass.**
- **Commits:** the repo owner's standing rule is that the assistant does not run `git commit` unless
  asked. Treat every "Commit" step as: stage exactly the listed files, show the message, and get the
  owner's go-ahead, unless they have said to commit freely for this plan.

---

## File Structure

**New, app:**

| File | Responsibility |
|---|---|
| `apps/RustPlusApi.CredentialsWeb/Endpoints/RequestMode.cs` | The loopback predicate. Pure, no I/O. |
| `apps/RustPlusApi.CredentialsWeb/Endpoints/CallbackParsing.cs` | Reads a pasted callback address. Pure, no session lookup. |
| `apps/RustPlusApi.CredentialsWeb/StartupLog.cs` | One source-generated warning for retired settings. |

**New, tests:**

| File | Responsibility |
|---|---|
| `tests/RustPlusApi.CredentialsWeb.UnitTests/RequestModeTests.cs` | The predicate, both halves. |
| `tests/RustPlusApi.CredentialsWeb.UnitTests/CallbackParsingTests.cs` | Every paste shape. |
| `tests/RustPlusApi.CredentialsWeb.UnitTests/PasteCallbackEndpointTests.cs` | `POST /api/callback`. |
| `tests/RustPlusApi.CredentialsWeb.UnitTests/RemoteIpStartupFilter.cs` | Test-only middleware that stamps the connection address, so both modes are reachable from `WebApplicationFactory`. |

**Modified, app:** `AppOptions.cs`, `Program.cs`, `Endpoints/SessionEndpoints.cs`,
`Endpoints/CallbackEndpoints.cs`, `Sessions/Session.cs`, `Sessions/SessionStore.cs`,
`wwwroot/index.html`, `wwwroot/app.js`, `wwwroot/app.css`.

**Modified, tests:** `CredentialsWebFactory.cs`, `AppOptionsValidatorTests.cs`,
`CallbackEndpointTests.cs`, `SessionEndpointTests.cs`, `PairingEndpointTests.cs`,
`SecretsAreNeverLoggedTests.cs`, and every file calling `SessionStore.TryCreate` (mechanical, see
Task 3).

---

## Task 1: Verify the mechanism

**This is a human task. It needs a real Steam login, so no agent can do it. Nothing below may start
until it passes.**

Everything in this plan assumes Facepunch still takes the legacy redirect branch for a loopback
`returnUrl` **supplied from a non-loopback origin**. Issue #126 proved the branch keys on the URL's
shape rather than its reachability, but every successful run so far also had a loopback *origin*, so
an origin or referrer gate has not been ruled out.

**Files:** none. Nothing is committed by this task.

- [ ] **Step 1: Build the probe page**

Save this as `/tmp/probe.html` on the machine that serves the LAN address from row 2 of #126:

```html
<!DOCTYPE html>
<html lang="en">
<body>
<a id="go" href="">Start the Steam login</a>
<script>
  const returnUrl = "http://localhost:54321/callback/0123456789abcdef0123456789abcdef";
  document.getElementById("go").href =
      "https://companion-rust.facepunch.com/login?returnUrl=" + encodeURIComponent(returnUrl);
</script>
</body>
</html>
```

- [ ] **Step 2: Serve it from the LAN address, not from loopback**

```bash
cd /tmp && python3 -m http.server 8099 --bind 0.0.0.0
```

- [ ] **Step 3: Run the probe from another machine on the LAN**

Open `http://<lan-ip>:8099/probe.html`, click the link, complete a real Steam login.

- [ ] **Step 4: Record the outcome**

**Pass:** the browser ends on a connection error for
`http://localhost:54321/callback/0123456789abcdef0123456789abcdef?steamId=...&token=...`, with the
address visible and complete in the address bar.

**Fail:** the visitor sees *"Failed to send login message to the Rust+ app."*

- [ ] **Step 5: Repeat from a typed URL, with no referrer**

Paste the same Facepunch login URL straight into the address bar. This separates "no `Referer`" from
"LAN `Referer`" and tells you which signal, if any, Facepunch is reading.

- [ ] **Step 6: Decide**

On a pass, continue to Task 2. On a fail, stop: this design is dead, and the browser extension under
*Later* in the spec is the remaining route. Record the outcome in the spec's *Verify this first*
section either way.

---

## Task 2: The loopback predicate

**Files:**
- Create: `apps/RustPlusApi.CredentialsWeb/Endpoints/RequestMode.cs`
- Test: `tests/RustPlusApi.CredentialsWeb.UnitTests/RequestModeTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `internal static class RustPlusApi.CredentialsWeb.Endpoints.RequestMode` with
  `internal static bool IsLocal(HttpContext context)`,
  `internal static bool IsLoopbackAddress(IPAddress? address)` and
  `internal static bool IsLoopbackHost(string? host)`.

- [ ] **Step 1: Write the failing test**

Create `tests/RustPlusApi.CredentialsWeb.UnitTests/RequestModeTests.cs`:

```csharp
using Microsoft.AspNetCore.Http;
using RustPlusApi.CredentialsWeb.Endpoints;
using System.Net;
using Xunit;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class RequestModeTests
{
    private static HttpContext Context(string? remoteIp, string host)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteIp is null ? null : IPAddress.Parse(remoteIp);
        context.Request.Host = new HostString(host);
        return context;
    }

    [Theory]
    [InlineData("127.0.0.1", "localhost:8080")]
    [InlineData("127.0.0.5", "127.0.0.1:8080")]
    [InlineData("::1", "[::1]:8080")]
    [InlineData("::ffff:127.0.0.1", "localhost")]
    [InlineData("127.0.0.1", "app.localhost:8080")]
    public void IsLocal_True_WhenBothTheConnectionAndTheHostAreLoopback(string remoteIp, string host) =>
        Assert.True(RequestMode.IsLocal(Context(remoteIp, host)));

    [Theory]
    // A reverse proxy on the same host: the connection looks loopback, the Host does not.
    [InlineData("127.0.0.1", "creds.example.org")]
    // A forged Host header from a remote caller.
    [InlineData("203.0.113.7", "localhost:8080")]
    [InlineData("203.0.113.7", "creds.example.org")]
    public void IsLocal_False_UnlessBothHalvesAreLoopback(string remoteIp, string host) =>
        Assert.False(RequestMode.IsLocal(Context(remoteIp, host)));

    [Fact]
    public void IsLocal_False_WhenThereIsNoConnectionAddress() =>
        Assert.False(RequestMode.IsLocal(Context(null, "localhost")));

    [Fact]
    public void IsLoopbackHost_False_ForABlankHost() =>
        Assert.False(RequestMode.IsLoopbackHost("   "));

    [Fact]
    public void IsLoopbackHost_False_ForAHostThatMerelyEndsInTheWordLocalhost() =>
        Assert.False(RequestMode.IsLoopbackHost("notlocalhost"));

    [Fact]
    public void IsLoopbackAddress_True_ForTheIPv4MappedFormKestrelReportsOnADualStackSocket() =>
        Assert.True(RequestMode.IsLoopbackAddress(IPAddress.Parse("::ffff:127.0.0.1")));
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests/RustPlusApi.CredentialsWeb.UnitTests.csproj --filter "FullyQualifiedName~RequestModeTests"
```

Expected: build failure, `RequestMode` does not exist.

- [ ] **Step 3: Write the implementation**

Create `apps/RustPlusApi.CredentialsWeb/Endpoints/RequestMode.cs`:

```csharp
using System.Net;

namespace RustPlusApi.CredentialsWeb.Endpoints;

/// <summary>Decides whether a request reached the app from the machine it is running on. That one
/// question settles two things: whether Facepunch's redirect can land here, and whether the visitor
/// is entitled to the pairing wait.
/// <para>Both halves are required. The <c>Host</c> header alone is forgeable, but a forged value
/// only sends the forger's own browser to their own machine, so nothing of ours leaks. The
/// connection address alone is wrong in the deployment that matters: a reverse proxy on the same
/// host makes every visitor look like loopback, which would hand strangers the local behaviour.</para></summary>
internal static class RequestMode
{
    /// <summary>True when the connection came from a loopback address and the request names a
    /// loopback host.</summary>
    /// <param name="context">The current request.</param>
    internal static bool IsLocal(HttpContext context) =>
        IsLoopbackAddress(context.Connection.RemoteIpAddress)
        && IsLoopbackHost(context.Request.Host.Host);

    /// <summary>True for 127.0.0.0/8, <c>::1</c>, and the IPv4-mapped form of either.
    /// <see cref="IPAddress.IsLoopback"/> rejects <c>::ffff:127.0.0.1</c>, which is exactly what
    /// Kestrel reports on a dual-stack socket, so the mapped form is unwrapped first.</summary>
    /// <param name="address">The address to test, or <see langword="null"/> when the request carries
    /// no connection address — as it does under <c>TestServer</c>.</param>
    internal static bool IsLoopbackAddress(IPAddress? address)
    {
        if (address is null)
        {
            return false;
        }

        var candidate = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        return IPAddress.IsLoopback(candidate);
    }

    /// <summary>True for <c>localhost</c>, any <c>*.localhost</c> — reserved by RFC 6761 and resolved
    /// to loopback by browsers without touching DNS — and any loopback IP literal. IPv6 literals
    /// arrive bracketed in a <c>Host</c> header, so brackets are trimmed first.</summary>
    /// <param name="host">The host from the request, without its port.</param>
    internal static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var trimmed = host.Trim('[', ']');

        return string.Equals(trimmed, "localhost", StringComparison.OrdinalIgnoreCase)
               || trimmed.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
               || (IPAddress.TryParse(trimmed, out var parsed) && IsLoopbackAddress(parsed));
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests/RustPlusApi.CredentialsWeb.UnitTests.csproj --filter "FullyQualifiedName~RequestModeTests"
```

Expected: all pass.

- [ ] **Step 5: Format and commit**

```bash
dotnet jb cleanupcode RustPlusApi.sln --profile="ReformatAndReorder"
git add apps/RustPlusApi.CredentialsWeb/Endpoints/RequestMode.cs \
        tests/RustPlusApi.CredentialsWeb.UnitTests/RequestModeTests.cs
git commit -m "feat(web): add the loopback request predicate"
```

---

## Task 3: Carry the mode on the session

**Files:**
- Modify: `apps/RustPlusApi.CredentialsWeb/Sessions/Session.cs`
- Modify: `apps/RustPlusApi.CredentialsWeb/Sessions/SessionStore.cs:171`
- Modify: `apps/RustPlusApi.CredentialsWeb/Endpoints/SessionEndpoints.cs:38`
- Test: `tests/RustPlusApi.CredentialsWeb.UnitTests/SessionStoreTests.cs` and every other test file
  calling `TryCreate` (mechanical)

**Interfaces:**
- Consumes: nothing from Task 2 yet.
- Produces: `Session.IsLocal` (`internal bool`, get-only) and
  `SessionStore.TryCreate(string clientIp, bool isLocal, out Session? session, out SessionCreateFailure failure)`.

- [ ] **Step 1: Write the failing test**

Append to `tests/RustPlusApi.CredentialsWeb.UnitTests/SessionStoreTests.cs`, inside the existing
class:

```csharp
    [Fact]
    public void TryCreate_RecordsWhetherTheVisitorReachedTheAppLocally()
    {
        using var store = new SessionStore(new AppOptions(), new FakeTimeProvider());

        Assert.True(store.TryCreate("203.0.113.7", isLocal: false, out var remote, out _));
        Assert.False(remote.IsLocal);
    }

    [Fact]
    public void TryCreate_RecordsALocalVisitor()
    {
        using var store = new SessionStore(new AppOptions(), new FakeTimeProvider());

        Assert.True(store.TryCreate("127.0.0.1", isLocal: true, out var local, out _));
        Assert.True(local.IsLocal);
    }
```

Check the file's existing `using` block first; add `using Microsoft.Extensions.Time.Testing;` and
`using RustPlusApi.CredentialsWeb;` only if they are not already there, and match how the existing
tests in that file construct a `SessionStore`.

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests/RustPlusApi.CredentialsWeb.UnitTests.csproj --filter "FullyQualifiedName~SessionStoreTests"
```

Expected: build failure, `TryCreate` takes three arguments and `IsLocal` does not exist.

- [ ] **Step 3: Add the property**

In `Sessions/Session.cs`, add `bool isLocal` to the primary constructor between `clientIp` and
`expiresAt`, with its `<param>` doc:

```csharp
/// <param name="clientIp">The caller's address, for per-IP accounting.</param>
/// <param name="isLocal">Whether the visitor reached the app from the machine it runs on.</param>
/// <param name="expiresAt">When this session becomes sweepable.</param>
internal sealed class Session(
    string sessionId,
    string returnToken,
    string clientIp,
    bool isLocal,
    DateTimeOffset expiresAt)
    : IDisposable
```

Add the property. Members in this file are alphabetical, so it belongs between `ExpiresAt` and
`Lifetime`:

```csharp
    /// <summary>Whether the visitor reached the app from the machine it runs on. Decided once, at
    /// creation, from the request that created the session — never recomputed, because a later
    /// request for the same session can arrive by a different route.</summary>
    internal bool IsLocal { get; } = isLocal;
```

- [ ] **Step 4: Thread it through the store**

In `Sessions/SessionStore.cs`, add the parameter to `TryCreate` and its doc, and pass it on:

```csharp
    /// <param name="clientIp">The caller's address.</param>
    /// <param name="isLocal">Whether the request came from the machine the app runs on.</param>
    /// <param name="session">The new session on success.</param>
    /// <param name="failure">Why creation was refused.</param>
    internal bool TryCreate(
        string clientIp,
        bool isLocal,
        [NotNullWhen(true)] out Session? session,
        out SessionCreateFailure failure)
```

and inside, at the construction site:

```csharp
                var created = new Session(
                    SessionIds.New(),
                    SessionIds.New(),
                    clientIp,
                    isLocal,
                    timeProvider.GetUtcNow().Add(options.CreatedTtl));
```

- [ ] **Step 5: Update the one production caller**

In `Endpoints/SessionEndpoints.cs:38`, pass `true` for now. Task 5 replaces it with the real
predicate:

```csharp
            // Task 5 replaces this with RequestMode.IsLocal(context).
            if (!store.TryCreate(ClientAddress.Of(context), isLocal: true, out var session, out var failure))
```

- [ ] **Step 6: Update every test call site mechanically**

All existing test sessions stand in for a local visitor, which is what keeps their behaviour
identical:

```bash
cd tests/RustPlusApi.CredentialsWeb.UnitTests
sed -i -E 's/TryCreate\((("[0-9.]+")|address|addresses\[index\]), out/TryCreate(\1, isLocal: true, out/g' *.cs
grep -rn "TryCreate(" *.cs | grep -v "isLocal:" || echo "all call sites updated"
```

The `grep` must print `all call sites updated`. If it prints a line instead, fix that call by hand.

- [ ] **Step 7: Run the full suite to verify it passes**

```bash
dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests/RustPlusApi.CredentialsWeb.UnitTests.csproj
```

Expected: all pass.

- [ ] **Step 8: Format and commit**

```bash
dotnet jb cleanupcode RustPlusApi.sln --profile="ReformatAndReorder"
git add apps/RustPlusApi.CredentialsWeb tests/RustPlusApi.CredentialsWeb.UnitTests
git commit -m "feat(web): record on each session whether the visitor is local"
```

---

## Task 4: Parse a pasted callback address

**Files:**
- Create: `apps/RustPlusApi.CredentialsWeb/Endpoints/CallbackParsing.cs`
- Test: `tests/RustPlusApi.CredentialsWeb.UnitTests/CallbackParsingTests.cs`

**Interfaces:**
- Consumes: `SteamLoginService.ParseCallback(Uri)` and `SteamLoginResult` from
  `RustPlusApi.Fcm.Registration`, both already public.
- Produces: `internal static class RustPlusApi.CredentialsWeb.Endpoints.CallbackParsing` with
  `internal static bool TryParsePastedCallback(string? pasted, out string? returnToken, out SteamLoginResult? login)`.

- [ ] **Step 1: Write the failing test**

Create `tests/RustPlusApi.CredentialsWeb.UnitTests/CallbackParsingTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests/RustPlusApi.CredentialsWeb.UnitTests.csproj --filter "FullyQualifiedName~CallbackParsingTests"
```

Expected: build failure, `CallbackParsing` does not exist.

- [ ] **Step 3: Write the implementation**

Create `apps/RustPlusApi.CredentialsWeb/Endpoints/CallbackParsing.cs`:

```csharp
using RustPlusApi.Fcm.Registration;
using RustPlusApi.Fcm.Registration.Steps;
using System.Diagnostics.CodeAnalysis;

namespace RustPlusApi.CredentialsWeb.Endpoints;

/// <summary>Reads a Facepunch callback address the visitor pasted in, rather than one their browser
/// delivered. Deliberately pure — no session lookup, no side effect — so a fumbled paste is rejected
/// before any single-use token is consumed and the visitor can simply try again.</summary>
internal static class CallbackParsing
{
    /// <summary>Splits a pasted address into the return token that identifies the session and the
    /// Steam identity Facepunch appended to it.</summary>
    /// <param name="pasted">Whatever the visitor put in the box.</param>
    /// <param name="returnToken">The single-use token from the address's last path segment.</param>
    /// <param name="login">The Steam identity carried in the query string.</param>
    internal static bool TryParsePastedCallback(
        string? pasted,
        [NotNullWhen(true)] out string? returnToken,
        [NotNullWhen(true)] out SteamLoginResult? login)
    {
        returnToken = null;
        login = null;

        if (string.IsNullOrWhiteSpace(pasted))
        {
            return false;
        }

        var trimmed = pasted.Trim();

        // The scheme is checked rather than assumed, and the prefixed form tried second, because
        // "localhost:54321/callback/..." is itself a well-formed absolute URI whose scheme is
        // "localhost" — so a scheme-less paste would otherwise parse into nonsense rather than fail.
        if (!TryAsWebUri(trimmed, out var uri) && !TryAsWebUri($"http://{trimmed}", out uri))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || !IsReturnToken(segments[^1]))
        {
            return false;
        }

        try
        {
            login = SteamLoginService.ParseCallback(uri);
        }
        catch (InvalidOperationException)
        {
            // No usable token or steamId: the Facepunch login URL pasted by mistake, a truncated
            // copy, or a contract change upstream. All three are the visitor's cue to try again.
            return false;
        }

        returnToken = segments[^1];
        return true;
    }

    /// <summary>True for 32 lowercase hex characters, which is what <see cref="Sessions.SessionIds"/>
    /// produces.</summary>
    /// <param name="value">The candidate path segment.</param>
    private static bool IsReturnToken(string value)
    {
        if (value.Length != 32)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Parses an absolute <c>http</c> or <c>https</c> URI, rejecting every other scheme.</summary>
    /// <param name="value">The candidate address.</param>
    /// <param name="uri">The parsed URI on success.</param>
    private static bool TryAsWebUri(string value, [NotNullWhen(true)] out Uri? uri) =>
        Uri.TryCreate(value, UriKind.Absolute, out uri)
        && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal));
}
```

If the compiler cannot prove `uri` is non-null after the guard, use `uri!` at the first use and add
a one-line comment saying the `&&` guarantees it.

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests/RustPlusApi.CredentialsWeb.UnitTests.csproj --filter "FullyQualifiedName~CallbackParsingTests"
```

Expected: all pass.

- [ ] **Step 5: Format and commit**

```bash
dotnet jb cleanupcode RustPlusApi.sln --profile="ReformatAndReorder"
git add apps/RustPlusApi.CredentialsWeb/Endpoints/CallbackParsing.cs \
        tests/RustPlusApi.CredentialsWeb.UnitTests/CallbackParsingTests.cs
git commit -m "feat(web): parse a pasted Facepunch callback address"
```

---

## Task 5: Mode-aware session creation

**Files:**
- Modify: `apps/RustPlusApi.CredentialsWeb/Endpoints/SessionEndpoints.cs`
- Modify: `apps/RustPlusApi.CredentialsWeb/Endpoints/CallbackEndpoints.cs:35-36`
- Create: `tests/RustPlusApi.CredentialsWeb.UnitTests/RemoteIpStartupFilter.cs`
- Modify: `tests/RustPlusApi.CredentialsWeb.UnitTests/CredentialsWebFactory.cs`
- Test: `tests/RustPlusApi.CredentialsWeb.UnitTests/SessionEndpointTests.cs`

**Interfaces:**
- Consumes: `RequestMode.IsLocal` (Task 2), `SessionStore.TryCreate(..., bool isLocal, ...)` (Task 3).
- Produces: `internal sealed record CreateSessionResponse(string SessionId, string LoginUrl, string CallbackMode, bool PairingAvailable)`,
  where `CallbackMode` is `"redirect"` or `"paste"`. Also
  `CredentialsWebFactory.RemoteIpAddress` (settable `IPAddress?`, default `IPAddress.Loopback`).

- [ ] **Step 1: Give the test host a controllable connection address**

`TestServer` leaves `RemoteIpAddress` null, which would make every integration test remote. Create
`tests/RustPlusApi.CredentialsWeb.UnitTests/RemoteIpStartupFilter.cs`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using System.Net;

namespace RustPlusApi.CredentialsWeb.UnitTests;

/// <summary>Stamps a connection address onto every request, because <c>TestServer</c> leaves
/// <c>RemoteIpAddress</c> null and the app's local/remote decision reads it. Registered ahead of the
/// app's own pipeline through <see cref="IStartupFilter"/>, so it runs before any endpoint.</summary>
/// <param name="address">Supplies the address per request, so a test can change it after the host
/// has started.</param>
internal sealed class RemoteIpStartupFilter(Func<IPAddress?> address) : IStartupFilter
{
    /// <inheritdoc/>
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        builder =>
        {
            builder.Use(async (context, chain) =>
            {
                context.Connection.RemoteIpAddress = address();
                await chain(context).ConfigureAwait(false);
            });

            next(builder);
        };
}
```

In `CredentialsWebFactory.cs`, add the property and register the filter. Add
`using System.Net;` and `using Microsoft.AspNetCore.Hosting;` if absent:

```csharp
    /// <summary>The connection address every request is stamped with. Loopback by default, so an
    /// unconfigured test exercises the local path the app was originally written for.</summary>
    internal IPAddress? RemoteIpAddress { get; set; } = IPAddress.Loopback;
```

and inside the existing `ConfigureServices` callback:

```csharp
            services.AddSingleton<IStartupFilter>(new RemoteIpStartupFilter(() => RemoteIpAddress));
```

- [ ] **Step 2: Write the failing tests**

Add to `tests/RustPlusApi.CredentialsWeb.UnitTests/SessionEndpointTests.cs`, inside the class:

```csharp
    private static Uri ReturnUrlOf(string loginUrl)
    {
        const string marker = "?returnUrl=";
        var index = loginUrl.IndexOf(marker, StringComparison.Ordinal);
        return new Uri(Uri.UnescapeDataString(loginUrl[(index + marker.Length)..]));
    }

    private static HttpClient HostedClient(CredentialsWebFactory factory)
    {
        factory.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        return factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://creds.example.org")
        });
    }

    [Fact]
    public async Task CreateSession_Local_ReturnsARedirectModeUrlPointingAtThisRequestsOrigin()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);
        var body = await response.Content.ReadFromJsonAsync<CreateSessionResponse>();

        Assert.Equal("redirect", body!.CallbackMode);
        Assert.True(body.PairingAvailable);
        var returnUrl = ReturnUrlOf(body.LoginUrl);
        Assert.Equal("localhost", returnUrl.Host);
        Assert.StartsWith("/callback/", returnUrl.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateSession_Hosted_ReturnsAPasteModeUrlPointingAtADeadLoopbackPort()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = HostedClient(factory);

        var response = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);
        var body = await response.Content.ReadFromJsonAsync<CreateSessionResponse>();

        Assert.Equal("paste", body!.CallbackMode);
        Assert.False(body.PairingAvailable);

        var returnUrl = ReturnUrlOf(body.LoginUrl);
        Assert.Equal("localhost", returnUrl.Host);
        Assert.Equal(Uri.UriSchemeHttp, returnUrl.Scheme);
        // The dynamic range: very unlikely to belong to something the visitor actually runs.
        Assert.InRange(returnUrl.Port, 49152, 65535);
    }

    [Fact]
    public async Task CreateSession_Hosted_TheReturnUrlNeverNamesThePublicHost()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = HostedClient(factory);

        var response = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);
        var body = await response.Content.ReadFromJsonAsync<CreateSessionResponse>();

        Assert.DoesNotContain("creds.example.org", body!.LoginUrl, StringComparison.Ordinal);
    }
```

Add `using System.Net;` to that file if it is not already imported.

Then fix the existing test `CreateSession_ReturnsSessionIdAndFacepunchLoginUrl`, whose last
assertion still expects the old configured origin. Replace that assertion with:

```csharp
        Assert.Contains(
            Uri.EscapeDataString("http://localhost/callback/"),
            body.LoginUrl,
            StringComparison.Ordinal);
```

- [ ] **Step 3: Run the tests to verify they fail**

```bash
dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests/RustPlusApi.CredentialsWeb.UnitTests.csproj --filter "FullyQualifiedName~SessionEndpointTests"
```

Expected: build failure, `CreateSessionResponse` has two properties.

- [ ] **Step 4: Widen the response record**

In `Endpoints/SessionEndpoints.cs`, replace the record and its docs:

```csharp
/// <summary>What the browser needs to start a flow.</summary>
/// <param name="SessionId">The handle for the event stream and follow-up calls.</param>
/// <param name="LoginUrl">The Facepunch login URL to send the visitor to.</param>
/// <param name="CallbackMode">"redirect" when Facepunch can deliver the callback here by itself,
/// "paste" when the visitor has to bring the address back by hand.</param>
/// <param name="PairingAvailable">Whether this session may start the pairing wait.</param>
internal sealed record CreateSessionResponse(
    string SessionId,
    string LoginUrl,
    string CallbackMode,
    bool PairingAvailable);
```

- [ ] **Step 5: Make session creation mode-aware**

Replace the body of the `POST /api/sessions` handler in `Endpoints/SessionEndpoints.cs`:

```csharp
        app.MapPost("/api/sessions", (HttpContext context, SessionStore store) =>
        {
            var isLocal = RequestMode.IsLocal(context);

            if (!store.TryCreate(ClientAddress.Of(context), isLocal, out var session, out var failure))
            {
                // ActiveSessionForIp means a resumable session already exists for this address —
                // "at capacity" would be false and would send the visitor into a five-minute wait
                // for no reason. GlobalLimit and HourlyLimit are genuine capacity/rate limits, so
                // they keep the existing message.
                var message = failure == SessionCreateFailure.ActiveSessionForIp
                    ? ActiveSessionMessage
                    : OverCapacityMessage;
                return Results.Json(new ErrorPayload(message), statusCode: 429);
            }

            // Local: the redirect can land here, so the return URL is this very request's origin.
            // Nothing is configured, so nothing can be configured wrong.
            //
            // Remote: Facepunch only honours a loopback returnUrl, and decides that from the URL's
            // shape rather than its reachability — their servers cannot reach a visitor's localhost
            // either. So it gets a loopback address nothing is listening on. The visitor's browser
            // fails to connect and shows the address, which they paste back at POST /api/callback.
            // The port comes from the dynamic range, so it is very unlikely to belong to something
            // the visitor actually runs; if it somehow does, the single-use return token means the
            // paste fails closed rather than the flow completing somewhere else.
            var returnUrl = isLocal
                ? $"{context.Request.Scheme}://{context.Request.Host}/callback/{session.ReturnToken}"
                : $"http://localhost:{RandomNumberGenerator.GetInt32(49152, 65536)}/callback/{session.ReturnToken}";

            return Results.Ok(new CreateSessionResponse(
                session.SessionId,
                SteamLoginService.BuildLoginUrl(returnUrl),
                isLocal ? "redirect" : "paste",
                isLocal));
        });
```

Add `using System.Security.Cryptography;` to the file. The `AppOptions options` parameter is no
longer used by this handler; remove it from the lambda.

- [ ] **Step 6: Build the callback URI from the request**

In `Endpoints/CallbackEndpoints.cs`, replace the `TryParseSteamLogin` call at line 35:

```csharp
            if (TryParseSteamLogin(
                    $"{context.Request.Scheme}://{context.Request.Host}",
                    context.Request.Path,
                    context.Request.QueryString,
                    out var login))
```

Leave `TryParseSteamLogin` itself alone. Its `publicBaseUrl` parameter becomes a plain base-URL
parameter; update only its `<param>` doc to say "the origin this request arrived on" and drop the
`<see cref="AppOptions.PublicBaseUrl"/>` reference, which stops compiling in Task 6.

- [ ] **Step 7: Run the tests to verify they pass**

```bash
dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests/RustPlusApi.CredentialsWeb.UnitTests.csproj
```

Expected: all pass.

- [ ] **Step 8: Format and commit**

```bash
dotnet jb cleanupcode RustPlusApi.sln --profile="ReformatAndReorder"
git add apps/RustPlusApi.CredentialsWeb tests/RustPlusApi.CredentialsWeb.UnitTests
git commit -m "feat(web): derive the Steam return URL from the request"
```

---

## Task 6: Retire the base-URL settings

**Files:**
- Modify: `apps/RustPlusApi.CredentialsWeb/AppOptions.cs`
- Create: `apps/RustPlusApi.CredentialsWeb/StartupLog.cs`
- Modify: `apps/RustPlusApi.CredentialsWeb/Program.cs`
- Modify: `apps/RustPlusApi.CredentialsWeb/Endpoints/SessionEndpoints.cs:18-20`
- Test: `tests/RustPlusApi.CredentialsWeb.UnitTests/AppOptionsValidatorTests.cs`,
  `CredentialsWebFactory.cs`, `CallbackEndpointTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `AppOptions.AllowRemotePairing` (`internal bool`, default `false`).
  `AppOptions.PublicBaseUrl` and `AppOptions.AllowInsecureBaseUrl` no longer exist.
  `StartupLog.LogRetiredSetting(this ILogger logger, string setting)`.

- [ ] **Step 1: Write the failing tests**

In `AppOptionsValidatorTests.cs`, delete every test that mentions `PublicBaseUrl` or
`AllowInsecureBaseUrl`, change the `Valid()` helper to `private static AppOptions Valid() => new();`,
and add:

```csharp
    [Fact]
    public void Validate_ReturnsNull_ForTheDefaults()
    {
        Assert.Null(AppOptionsValidator.Validate(Valid()));
    }

    [Fact]
    public void CreatedTtl_DefaultsToTenMinutes()
    {
        // The pre-login window now has to cover a real Steam login, two-factor included, plus a
        // copy and a paste.
        Assert.Equal(TimeSpan.FromMinutes(10), new AppOptions().CreatedTtl);
    }

    [Fact]
    public void AllowRemotePairing_DefaultsToOff()
    {
        Assert.False(new AppOptions().AllowRemotePairing);
    }
```

Create a new test in `SessionEndpointTests.cs` for the warning:

```csharp
    [Fact]
    public async Task Startup_WarnsThatARetiredSettingIsStillConfigured()
    {
        await using var factory = new CredentialsWebFactory(new Dictionary<string, string>
        {
            ["CredentialsWeb__PublicBaseUrl"] = "https://creds.example.org"
        });
        using var client = factory.CreateClient();

        // Force the host to build; the warning is emitted during startup.
        await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);

        Assert.Contains(
            factory.Logs.Records,
            record => record.Contains("PublicBaseUrl", StringComparison.Ordinal)
                      && record.Contains("no longer read", StringComparison.Ordinal));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests/RustPlusApi.CredentialsWeb.UnitTests.csproj --filter "FullyQualifiedName~AppOptionsValidatorTests"
```

Expected: failures on the new defaults and the missing `AllowRemotePairing`.

- [ ] **Step 3: Rewrite the options**

In `AppOptions.cs`, delete the `PublicBaseUrl` and `AllowInsecureBaseUrl` properties and add:

```csharp
    /// <summary>Allows the pairing wait for a visitor who did not reach the app over loopback. Off by
    /// default: the wait holds an MCS socket per visitor, which is the one genuinely scarce resource
    /// here, and a public instance has no reason to hold one for a stranger. Turn it on when
    /// self-hosting on an address that is yours but is not loopback, such as a LAN address.</summary>
    internal bool AllowRemotePairing { get; set; }
```

Change the `CreatedTtl` default and its doc:

```csharp
    /// <summary>Lifetime of a session that has not yet completed the Steam login. It has to cover a
    /// real Steam login, two-factor included, and on a hosted instance a copy and a paste as well.
    /// Still the shortest leash of the three: this is the cheapest state to create and so the
    /// cheapest to spam.</summary>
    internal TimeSpan CreatedTtl { get; set; } = TimeSpan.FromMinutes(10);
```

In `AppOptionsValidator.Validate`, delete the four blocks that check `PublicBaseUrl` and
`AllowInsecureBaseUrl`, so the method now begins with the `KnownProxies` loop.

- [ ] **Step 4: Add the startup warning**

Create `apps/RustPlusApi.CredentialsWeb/StartupLog.cs`:

```csharp
namespace RustPlusApi.CredentialsWeb;

/// <summary>Source-generated, structured log messages emitted while the host starts. Generated
/// bodies carry <c>[GeneratedCode]</c> and are excluded from the coverage gate automatically.</summary>
internal static partial class StartupLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "CredentialsWeb:{Setting} is set but is no longer read. The Steam return URL is "
                  + "now derived from the request, so this setting has no effect and can be removed.")]
    public static partial void LogRetiredSetting(this ILogger logger, string setting);
}
```

In `Program.cs`, immediately after `var app = builder.Build();`:

```csharp
// These two were required until the return URL started coming from the request. An instance that
// still sets them keeps working, so an existing `docker run` does not break — it is just told.
string[] retiredSettings = ["PublicBaseUrl", "AllowInsecureBaseUrl"];
foreach (var setting in retiredSettings)
{
    if (builder.Configuration[$"{AppOptions.SectionName}:{setting}"] is not null)
    {
        app.Logger.LogRetiredSetting(setting);
    }
}
```

- [ ] **Step 5: Fix the capacity messages**

In `Endpoints/SessionEndpoints.cs`, the `RunCommand` constant told visitors to run a command that
exited at startup validation. With no required setting left, the bare command works:

```csharp
    /// <summary>The command the capacity messages point at. The app has no required setting, so a
    /// bare run is now advice that actually works.</summary>
    private const string RunCommand =
        "docker run -p 127.0.0.1:8080:8080 ghcr.io/handys11/rustplusapi-credentials";
```

- [ ] **Step 5b: Strip the retired setting from every test that constructs AppOptions**

Eight test files set `PublicBaseUrl` in an object initialiser and stop compiling the moment the
property is gone. In each, `PublicBaseUrl` is the last member of its initialiser, so deleting the
line is safe. Two of those files also depend on `CreatedTtl` being five minutes, so they get it
explicitly rather than inheriting a default that just changed underneath them.

Run this from the repository root, **after** Step 1 has rewritten `AppOptionsValidatorTests.cs`
(the pattern is anchored to initialiser lines, so the `options.PublicBaseUrl = ...` statements in
that file are untouched either way):

```bash
cd tests/RustPlusApi.CredentialsWeb.UnitTests

# These two assert on sweeping and advance the clock six minutes, which only expires a Created
# session while CreatedTtl is five. Pin it here so the test states its own assumption.
sed -i -E 's/^( *)PublicBaseUrl = "[^"]*"$/\1CreatedTtl = TimeSpan.FromMinutes(5)/' \
    SessionStoreTests.cs SessionSweeperTests.cs

# The rest never read it.
sed -i -E '/^ *PublicBaseUrl = "[^"]*"$/d' \
    SessionStoreCapsTests.cs CredentialFlowTests.cs CredentialFlowPairingTests.cs

grep -rn "PublicBaseUrl" *.cs || echo "no references left"
```

The `grep` must print `no references left`, apart from the comment in `CallbackEndpointTests.cs`
that Step 6 rewrites. If a `new AppOptions` initialiser is left empty by the deletion, that is valid
C# and the formatter collapses it in Step 8.

- [ ] **Step 5c: Correct the comments that name the old lifetime**

Three comments state the old value and become wrong:

- `SessionStoreTests.cs` in `SweepExpired_UsesTheStateSpecificTtl`: change
  `// Authenticated sessions get SessionTtl (15 min), not CreatedTtl (5 min).` to
  `// Authenticated sessions get SessionTtl (15 min), not this store's CreatedTtl (5 min).`
- `SessionSweeperTests.cs` in `ExecuteAsync_LeavesLiveSessionsAlone`: the two comments reading
  `expires at 5 minutes` and `expires at 1+5=6 minutes` stay correct only because Step 5b pinned
  `CreatedTtl` in that file. Append `(pinned above)` to the first of them.

- [ ] **Step 6: Update the test host**

In `CredentialsWebFactory.cs`, delete the `BaseUrl` constant and the line that sets
`CredentialsWeb__PublicBaseUrl`. Keep the `settings` loop, which the warning test uses.

In `CallbackEndpointTests.cs`, the two `TryParseSteamLogin` unit tests referenced
`CredentialsWebFactory.BaseUrl`. Replace that reference with the literal
`"https://creds.example.org"` in both, and in the second test's comment replace "PublicBaseUrl is
validated absolute at startup" with "the base URL is built from a request Kestrel has already
accepted".

- [ ] **Step 7: Run the full suite to verify it passes**

```bash
dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests/RustPlusApi.CredentialsWeb.UnitTests.csproj
```

Expected: all pass. If any test still names `PublicBaseUrl`, it was missed in Step 6.

- [ ] **Step 8: Format and commit**

```bash
dotnet jb cleanupcode RustPlusApi.sln --profile="ReformatAndReorder"
git add apps/RustPlusApi.CredentialsWeb tests/RustPlusApi.CredentialsWeb.UnitTests
git commit -m "feat(web)!: drop PublicBaseUrl and AllowInsecureBaseUrl"
```

---

## Task 7: Accept a pasted callback

**Files:**
- Modify: `apps/RustPlusApi.CredentialsWeb/Endpoints/CallbackEndpoints.cs`
- Test: `tests/RustPlusApi.CredentialsWeb.UnitTests/PasteCallbackEndpointTests.cs`

**Interfaces:**
- Consumes: `CallbackParsing.TryParsePastedCallback` (Task 4), `SessionStore.TryConsumeReturnToken`,
  `CredentialFlow.CompleteRegistrationAsync`.
- Produces: `POST /api/callback`, taking
  `internal sealed record PasteCallbackRequest(string? Url)` and returning
  `internal sealed record PasteCallbackResponse(string SessionId)` with status 202.

- [ ] **Step 1: Write the failing test**

Create `tests/RustPlusApi.CredentialsWeb.UnitTests/PasteCallbackEndpointTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RustPlusApi.CredentialsWeb.Endpoints;
using RustPlusApi.CredentialsWeb.Sessions;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class PasteCallbackEndpointTests
{
    private static readonly Uri Route = new("/api/callback", UriKind.Relative);

    private static Session NewSession(CredentialsWebFactory factory)
    {
        var store = factory.Services.GetRequiredService<SessionStore>();
        store.TryCreate("203.0.113.7", isLocal: false, out var session, out _);
        return session!;
    }

    private static string Pasted(string returnToken) =>
        $"http://localhost:54321/callback/{returnToken}?steamId=76561198249527954&token=steam-token";

    [Fact]
    public async Task Paste_DrivesTheFlowToReady()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();
        var session = NewSession(factory);

        var response = await client.PostAsJsonAsync(Route, new PasteCallbackRequest(Pasted(session.ReturnToken)));
        await session.BackgroundWork;

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PasteCallbackResponse>();
        Assert.Equal(session.SessionId, body!.SessionId);
        Assert.Equal(SessionState.Ready, session.State);
        Assert.Equal("steam-token", factory.Steps.SteamTokenSeen);
        Assert.Null(session.SteamToken);
    }

    [Fact]
    public async Task Paste_Returns404_ForAnUnknownReturnToken()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            Route, new PasteCallbackRequest(Pasted("0123456789abcdef0123456789abcdef")));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Paste_Returns404_WhenReplayed()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();
        var session = NewSession(factory);

        var first = await client.PostAsJsonAsync(Route, new PasteCallbackRequest(Pasted(session.ReturnToken)));
        await session.BackgroundWork;
        var second = await client.PostAsJsonAsync(Route, new PasteCallbackRequest(Pasted(session.ReturnToken)));

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("https://companion-rust.facepunch.com/login")]
    public async Task Paste_Returns400_AndConsumesNothing_ForAnUnreadableAddress(string pasted)
    {
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();
        var session = NewSession(factory);

        var bad = await client.PostAsJsonAsync(Route, new PasteCallbackRequest(pasted));

        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(SessionState.Created, session.State);
        Assert.Empty(factory.Steps.Calls);

        // The whole point of parsing before consuming: the visitor gets to correct the paste.
        var good = await client.PostAsJsonAsync(Route, new PasteCallbackRequest(Pasted(session.ReturnToken)));
        await session.BackgroundWork;

        Assert.Equal(HttpStatusCode.Accepted, good.StatusCode);
        Assert.Equal(SessionState.Ready, session.State);
    }

    [Fact]
    public async Task Paste_Returns400_ForANullUrl()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(Route, new PasteCallbackRequest(null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests/RustPlusApi.CredentialsWeb.UnitTests.csproj --filter "FullyQualifiedName~PasteCallbackEndpointTests"
```

Expected: build failure, `PasteCallbackRequest` does not exist.

- [ ] **Step 3: Add the endpoint**

In `Endpoints/CallbackEndpoints.cs`, add the two records above the class:

```csharp
/// <summary>The address the visitor copied out of their browser's error page.</summary>
/// <param name="Url">The pasted address, exactly as they gave it.</param>
internal sealed record PasteCallbackRequest(string? Url);

/// <summary>Tells the browser which session the paste belonged to, so a tab that lost its handle can
/// pick the flow back up.</summary>
/// <param name="SessionId">The session the return token identified.</param>
internal sealed record PasteCallbackResponse(string SessionId);
```

Add the two messages as constants inside `CallbackEndpoints`:

```csharp
    private const string UnreadableMessage =
        "That doesn't look like the Rust+ callback address. Copy the whole address from the page "
        + "that failed to load, starting with http://, and try again.";

    private const string ConsumedMessage =
        "That address was already used, or the session expired. Start over — nothing was saved.";
```

Change `MapCallbackEndpoints` from an expression body to a block that maps both routes. Keep the
existing `GET` handler exactly as it is, and add:

```csharp
        app.MapPost("/api/callback", (
            PasteCallbackRequest request,
            SessionStore store,
            CredentialFlow flow) =>
        {
            // Parse before consuming, unlike the GET route. The visitor is looking at this response,
            // so a fumbled paste has to leave the session intact for them to correct.
            if (!CallbackParsing.TryParsePastedCallback(request.Url, out var returnToken, out var login))
            {
                return Results.Json(new ErrorPayload(UnreadableMessage), statusCode: 400);
            }

            // Single-use, exactly as for the redirect: an address pasted twice finds nothing, and an
            // unknown token is indistinguishable from a consumed one.
            if (!store.TryConsumeReturnToken(returnToken, out var session))
            {
                return Results.Json(new ErrorPayload(ConsumedMessage), statusCode: 404);
            }

            session.BackgroundWork = flow.CompleteRegistrationAsync(session, login, session.Lifetime.Token);
            return Results.Json(new PasteCallbackResponse(session.SessionId), statusCode: 202);
        });
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests/RustPlusApi.CredentialsWeb.UnitTests.csproj --filter "FullyQualifiedName~PasteCallbackEndpointTests"
```

Expected: all pass.

- [ ] **Step 5: Format and commit**

```bash
dotnet jb cleanupcode RustPlusApi.sln --profile="ReformatAndReorder"
git add apps/RustPlusApi.CredentialsWeb/Endpoints/CallbackEndpoints.cs \
        tests/RustPlusApi.CredentialsWeb.UnitTests/PasteCallbackEndpointTests.cs
git commit -m "feat(web): accept the Facepunch callback address by paste"
```

---

## Task 8: Keep the pairing wait local

**Files:**
- Modify: `apps/RustPlusApi.CredentialsWeb/Endpoints/SessionEndpoints.cs`
- Test: `tests/RustPlusApi.CredentialsWeb.UnitTests/PairingEndpointTests.cs`

**Interfaces:**
- Consumes: `Session.IsLocal` (Task 3), `AppOptions.AllowRemotePairing` (Task 6).
- Produces: `POST /api/sessions/{id}/pairing` returns 403 for a remote session unless
  `AllowRemotePairing` is set. `CreateSessionResponse.PairingAvailable` becomes
  `isLocal || options.AllowRemotePairing`.

- [ ] **Step 1: Write the failing test**

Add to `PairingEndpointTests.cs`. Note the existing `ReadySessionAsync` helper creates a local
session; add a remote variant beside it:

```csharp
    /// <summary>Runs a remote session to Ready through the paste route, the way a hosted visitor
    /// does.</summary>
    /// <param name="factory">The test host to create a session and client against.</param>
    private static async Task<Session> RemoteReadySessionAsync(CredentialsWebFactory factory)
    {
        using var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<SessionStore>();
        store.TryCreate("203.0.113.7", isLocal: false, out var session, out _);

        await client.PostAsJsonAsync(
            new Uri("/api/callback", UriKind.Relative),
            new PasteCallbackRequest(
                $"http://localhost:54321/callback/{session!.ReturnToken}"
                + "?steamId=76561198249527954&token=steam-token"));
        await session.BackgroundWork;

        factory.Steps.Calls.Clear();
        return session;
    }

    [Fact]
    public async Task Pairing_Returns403_ForARemoteSession()
    {
        await using var factory = new CredentialsWebFactory();
        var session = await RemoteReadySessionAsync(factory);
        using var client = factory.CreateClient();

        var response = await client.PostAsync(PairingUri(session.SessionId), null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(factory.Steps.Calls);
    }

    [Fact]
    public async Task Pairing_IsAllowedForARemoteSession_WhenTheOperatorOptsIn()
    {
        await using var factory = new CredentialsWebFactory(new Dictionary<string, string>
        {
            ["CredentialsWeb__AllowRemotePairing"] = "true"
        });
        factory.Steps.PairingWaitsForGate = true;
        var session = await RemoteReadySessionAsync(factory);
        using var client = factory.CreateClient();

        var response = await client.PostAsync(PairingUri(session.SessionId), null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }
```

Add `using System.Net.Http.Json;` and `using RustPlusApi.CredentialsWeb.Endpoints;` to that file.

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests/RustPlusApi.CredentialsWeb.UnitTests.csproj --filter "FullyQualifiedName~PairingEndpointTests"
```

Expected: the 403 test fails with `Accepted`.

- [ ] **Step 3: Refuse remote pairing**

In `Endpoints/SessionEndpoints.cs`, add the message constant:

```csharp
    private const string RemotePairingMessage =
        "Waiting for a pairing needs a socket held open to Google for as long as it takes you to "
        + "alt-tab into Rust, so this instance doesn't offer it. Your credentials above are the part "
        + "you need. To get the four pairing values, run the app yourself: " + RunCommand;
```

Add `AppOptions options` back to the pairing handler's parameters, and insert the check straight
after the `TryGet` miss:

```csharp
            // The pairing wait is the one step that holds a long-lived socket per visitor. A public
            // instance has no reason to hold one for a stranger, so it is local-only unless the
            // operator opts in — which someone self-hosting on a LAN address will want to.
            if (!session.IsLocal && !options.AllowRemotePairing)
            {
                return Results.Json(new ErrorPayload(RemotePairingMessage), statusCode: 403);
            }
```

- [ ] **Step 4: Report availability truthfully at session creation**

In the `POST /api/sessions` handler, re-add the `AppOptions options` parameter and change the last
constructor argument from `isLocal` to:

```csharp
                isLocal || options.AllowRemotePairing));
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests/RustPlusApi.CredentialsWeb.UnitTests.csproj
```

Expected: all pass.

- [ ] **Step 6: Format and commit**

```bash
dotnet jb cleanupcode RustPlusApi.sln --profile="ReformatAndReorder"
git add apps/RustPlusApi.CredentialsWeb tests/RustPlusApi.CredentialsWeb.UnitTests
git commit -m "feat(web): offer the pairing wait to local visitors only"
```

---

## Task 9: Prove the paste path leaks nothing

**Files:**
- Test: `tests/RustPlusApi.CredentialsWeb.UnitTests/SecretsAreNeverLoggedTests.cs`

**Interfaces:**
- Consumes: `POST /api/callback` (Task 7).
- Produces: nothing. Test-only task.

- [ ] **Step 1: Write the failing test**

Add to `SecretsAreNeverLoggedTests.cs` a second driver that reaches `Ready` by pasting, and the
assertion that the paste body never surfaces:

```csharp
    private static async Task<CredentialsWebFactory> RunPastedFlowAsync()
    {
        var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();

        var store = factory.Services.GetRequiredService<SessionStore>();
        store.TryCreate("203.0.113.7", isLocal: false, out var session, out _);

        await client.PostAsJsonAsync(
            new Uri("/api/callback", UriKind.Relative),
            new PasteCallbackRequest(
                $"http://localhost:54321/callback/{session!.ReturnToken}"
                + $"?steamId=76561198249527954&token={SteamTokenSentinel}"));
        await session.BackgroundWork;

        return factory;
    }

    [Fact]
    public async Task ThePastedAddressNeverReachesALogRecord()
    {
        await using var factory = await RunPastedFlowAsync();

        Assert.NotEmpty(factory.Logs.Records);
        Assert.DoesNotContain(
            factory.Logs.Records,
            record => record.Contains(SteamTokenSentinel, StringComparison.Ordinal));
        Assert.DoesNotContain(
            factory.Logs.Records,
            record => record.Contains("/api/callback", StringComparison.Ordinal));
    }
```

Add `using System.Net.Http.Json;` and `using RustPlusApi.CredentialsWeb.Endpoints;` to the file.

- [ ] **Step 2: Run the test**

```bash
dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests/RustPlusApi.CredentialsWeb.UnitTests.csproj --filter "FullyQualifiedName~SecretsAreNeverLoggedTests"
```

Expected: it should pass immediately, because `Program.cs` already silences the hosting diagnostics
logger and a request body is never logged. If it fails, that is a real leak: find the logger writing
it and silence it in `Program.cs` beside the existing filter, with a comment.

- [ ] **Step 3: Commit**

```bash
dotnet jb cleanupcode RustPlusApi.sln --profile="ReformatAndReorder"
git add tests/RustPlusApi.CredentialsWeb.UnitTests/SecretsAreNeverLoggedTests.cs
git commit -m "test(web): assert the pasted address never reaches a log"
```

---

## Task 10: The paste flow in the page

**Files:**
- Modify: `apps/RustPlusApi.CredentialsWeb/wwwroot/index.html`
- Modify: `apps/RustPlusApi.CredentialsWeb/wwwroot/app.js`
- Modify: `apps/RustPlusApi.CredentialsWeb/wwwroot/app.css`

**Interfaces:**
- Consumes: `CreateSessionResponse.CallbackMode` and `.PairingAvailable` (Tasks 5 and 8),
  `POST /api/callback` (Task 7).
- Produces: nothing consumed by later C# tasks.

There is no JavaScript test framework in this repo and `wwwroot` is outside the coverage gate, so
this task and the next are verified by hand. The steps below say exactly how.

- [ ] **Step 1: Rewrite the trust bullets**

In `index.html`, replace the first `<li>` inside `<details>` with two, and adjust the second so it
does not claim the token always arrives as a query parameter:

```html
            <ul>
                <li id="trust-local">This app is running on the machine you are browsing from. The
                    Steam login completes over loopback and nowhere else, so nothing here leaves your
                    computer.</li>
                <li id="trust-hosted" hidden>This server is not your machine. Your Steam auth token
                    reaches it when you paste the login address back, and nothing else about you
                    does.</li>
                <li>Your Steam auth token is dropped the moment your device is registered with Rust
                    Companion. It is never written to disk and never sent back to your browser.</li>
                <li>Nothing is written to disk or to a database. Everything lives in memory and is
                    discarded when your session ends or the process restarts.</li>
                <li>No credential is written to any log. The web server's request logging is turned
                    off precisely because it would otherwise record one.</li>
                <li>Your <code>playerToken</code> is full access to your Rust+ account. Treat it like
                    a password and don't paste it anywhere public.</li>
            </ul>
```

- [ ] **Step 2: Add the paste section and the stuck escape hatch**

In `index.html`, insert this section immediately after `</section>` of `#intro`:

```html
    <section id="paste" hidden>
        <h2>One more step</h2>
        <p id="paste-intro">
            <a id="login-link" href="" target="_blank" rel="noopener">Open the Steam login in a new
                tab</a>, sign in, and your browser will land on a page saying it can't connect to
            <code>localhost</code>. <strong>That is expected</strong> — nothing is listening there,
            and that is the point.
        </p>
        <p>Copy the whole address from that page and paste it below.</p>
        <label for="pasted-url">Callback address</label>
        <input id="pasted-url" type="url" inputmode="url" autocomplete="off" spellcheck="false"
               placeholder="http://localhost:54321/callback/…">
        <button id="submit-pasted" type="button">Continue</button>
        <p id="paste-error" class="error" role="alert"></p>
    </section>
```

Add the escape hatch to `#progress`, so a redirect that never landed is recoverable:

```html
    <section id="progress" hidden>
        <h2>Working…</h2>
        <p id="status">Registering a device with Google and Rust Companion.</p>
        <button id="paste-instead" type="button" class="secondary">Nothing happening? Paste the
            login address</button>
    </section>
```

And wrap the pairing offer in `#ready` so it can be hidden, adding the explanation beside it:

```html
        <div id="pair-offer">
            <h3>Optional: get your pairing values</h3>
            <p>
                To get <code>ip</code>, <code>port</code>, <code>playerId</code> and
                <code>playerToken</code>, this server has to hold a connection open to Google while
                you pair in game. Start it only when you're ready to alt-tab into Rust.
            </p>
            <button id="pair" type="button">Wait for my pairing</button>
            <p id="pair-note"></p>
        </div>
        <p id="pair-unavailable" hidden>
            The four pairing values need a connection held open to Google while you pair in game,
            which this instance doesn't do for visitors. Run the app on your own machine to get them,
            or use <code>PairingListener</code> from <code>RustPlusApi.Fcm.Registration</code> with
            the credentials above.
        </p>
```

- [ ] **Step 3: Add the styles**

Append to `app.css`:

```css
button.secondary {
    color: var(--fg);
    background: transparent;
    border: 1px solid var(--line);
}

label { color: var(--muted); font-size: 0.9rem; display: block; }

input[type="url"] {
    font: inherit;
    width: 100%;
    padding: 0.6rem 0.7rem;
    margin: 0.35rem 0 0.9rem;
    color: var(--fg);
    background: var(--bg);
    border: 1px solid var(--line);
    border-radius: 6px;
}

.error { color: var(--accent); min-height: 1.5rem; margin: 0.75rem 0 0; }
```

- [ ] **Step 4: Teach app.js about the two modes**

In `app.js`, add beside the existing module state:

```js
const PAIRING_KEY = "rustplus-credentials-pairing";

let callbackMode = "redirect";
let pairingAvailable = true;
```

Add `paste` to the `view` map, next to the other sections:

```js
    paste: document.getElementById("paste"),
```

Add these functions:

```js
function applyPairingAvailability() {
    document.getElementById("pair-offer").hidden = !pairingAvailable;
    document.getElementById("pair-unavailable").hidden = pairingAvailable;
}

function showPaste() {
    // Reached either straight after creating a session, when the login link is known, or from the
    // progress screen as a rescue, when it is not.
    document.getElementById("paste-intro").hidden = !document.getElementById("login-link").getAttribute("href");
    show("paste");
}

async function submitPaste() {
    const input = document.getElementById("pasted-url");
    const button = document.getElementById("submit-pasted");
    const error = document.getElementById("paste-error");
    const url = input.value.trim();

    if (!url) {
        error.textContent = "Paste the address from the page that failed to load.";
        return;
    }

    button.disabled = true;
    error.textContent = "";

    const response = await fetch("/api/callback", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ url })
    });

    if (response.ok) {
        // The address carries the Steam token, so it does not linger in the field.
        input.value = "";
        show("progress");
        return;
    }

    const body = await response.json().catch(() => ({
        message: "That address could not be read. Copy the whole address and try again."
    }));
    error.textContent = body.message;
    button.disabled = false;
}
```

Replace `start()` entirely:

```js
async function start() {
    const button = document.getElementById("start");
    button.disabled = true;

    const response = await fetch("/api/sessions", { method: "POST" });
    if (!response.ok) {
        const body = await response.json().catch(() => ({ message: "This instance is busy." }));
        fail(body.message);
        return;
    }

    const body = await response.json();
    sessionId = body.sessionId;
    callbackMode = body.callbackMode;
    pairingAvailable = body.pairingAvailable;
    sessionStorage.setItem(SESSION_KEY, body.sessionId);
    sessionStorage.setItem(PAIRING_KEY, String(body.pairingAvailable));
    applyPairingAvailability();

    if (callbackMode === "paste") {
        // Attach the stream before the visitor leaves for Steam: the flow starts the moment they
        // paste, and this tab is where they will be watching it.
        listen(sessionId);
        document.getElementById("login-link").href = body.loginUrl;
        showPaste();
        return;
    }

    location.href = body.loginUrl;
}
```

Add the listeners beside the existing ones:

```js
document.getElementById("submit-pasted").addEventListener("click", submitPaste);
document.getElementById("paste-instead").addEventListener("click", showPaste);
document.getElementById("pasted-url").addEventListener("paste", () => {
    // The field's value is not updated until after this event, so read it on the next tick.
    setTimeout(() => {
        if (document.getElementById("pasted-url").value.includes("/callback/")) {
            submitPaste();
        }
    }, 0);
});
```

And make the trust copy and the pairing offer reflect the mode on first paint, just above the final
`sessionId = readSessionId();`:

```js
// The server decides this, and says so in the create-session response. Until then the page guesses
// from its own address, which is right in every ordinary case and only cosmetic when it is not.
const looksLocal = ["localhost", "127.0.0.1", "[::1]"].includes(location.hostname)
    || location.hostname.endsWith(".localhost");
document.getElementById("trust-local").hidden = !looksLocal;
document.getElementById("trust-hosted").hidden = looksLocal;
pairingAvailable = sessionStorage.getItem(PAIRING_KEY) !== "false";
applyPairingAvailability();
```

Also clear the new key in the restart handler, beside the existing `removeItem`:

```js
    sessionStorage.removeItem(PAIRING_KEY);
```

- [ ] **Step 5: Verify the local mode by hand**

```bash
dotnet run --project apps/RustPlusApi.CredentialsWeb
```

Open the printed `http://localhost:<port>` address. Confirm: the local trust bullet is shown, the
pairing offer is present, and clicking *Sign in with Steam* navigates straight to Facepunch. You do
not need to complete the login.

- [ ] **Step 6: Verify the hosted mode by hand**

With the app still running, bind it to all interfaces and reach it from another machine, which is
the same setup as Task 1:

```bash
dotnet run --project apps/RustPlusApi.CredentialsWeb --urls http://0.0.0.0:5000
```

From the other machine, open `http://<lan-ip>:5000`. Confirm: the hosted trust bullet is shown, the
pairing offer is replaced by the explanation, clicking *Sign in with Steam* reveals the paste section
without navigating, and the login link's target carries a `returnUrl` pointing at
`http://localhost:<high port>`. Complete a real Steam login and paste the failed address back;
the page should move to *Working…* and then to *Credentials ready*.

- [ ] **Step 7: Commit**

```bash
git add apps/RustPlusApi.CredentialsWeb/wwwroot
git commit -m "feat(web): add the paste flow to the page"
```

---

## Task 11: Reveal and copy

**Files:**
- Modify: `apps/RustPlusApi.CredentialsWeb/wwwroot/index.html`
- Modify: `apps/RustPlusApi.CredentialsWeb/wwwroot/app.js`
- Modify: `apps/RustPlusApi.CredentialsWeb/wwwroot/app.css`

**Interfaces:**
- Consumes: the `credentials` and `paired` SSE payloads, unchanged.
- Produces: nothing consumed by other tasks.

- [ ] **Step 1: Add the controls to the ready section**

In `index.html`, replace the lone download button in `#ready` with:

```html
        <div class="actions">
            <button id="show-json" type="button" class="secondary" aria-expanded="false"
                    aria-controls="config-json">Show JSON</button>
            <button id="copy-json" type="button" class="secondary">Copy</button>
            <button id="download" type="button">Download rustplus.config.json</button>
        </div>
        <pre id="config-json" hidden></pre>
        <p id="copy-status" class="status" role="status" aria-live="polite"></p>
```

The JSON starts hidden deliberately: it is a credential, and it should not be sitting on screen
during an incidental screen share.

- [ ] **Step 2: Add copy buttons to the pairing values**

In `#paired`, replace the definition list and the buttons below it with:

```html
        <dl>
            <dt>Server</dt><dd><span id="server-name"></span></dd>
            <dt>IP</dt><dd><span id="pair-ip"></span><button class="copy" type="button"
                                                             data-copy="pair-ip">Copy</button></dd>
            <dt>Port</dt><dd><span id="pair-port"></span><button class="copy" type="button"
                                                                 data-copy="pair-port">Copy</button></dd>
            <dt>Player ID</dt><dd><span id="pair-player-id"></span><button class="copy" type="button"
                                                                           data-copy="pair-player-id">Copy</button></dd>
            <dt>Player token</dt><dd><span id="pair-player-token"></span><button class="copy" type="button"
                                                                                 data-copy="pair-player-token">Copy</button></dd>
        </dl>
        <pre id="snippet"></pre>
        <div class="actions">
            <button id="copy-snippet" type="button" class="secondary">Copy the snippet</button>
            <button id="download-paired" type="button">Download rustplus.config.json</button>
        </div>
```

- [ ] **Step 3: Add the styles**

Append to `app.css`:

```css
.actions { display: flex; flex-wrap: wrap; gap: 0.6rem; margin: 1.25rem 0 0.5rem; }

button.copy {
    font-size: 0.8rem;
    font-weight: 500;
    padding: 0.1rem 0.5rem;
    margin-left: 0.6rem;
    color: var(--fg);
    background: transparent;
    border: 1px solid var(--line);
}

.status { color: var(--muted); min-height: 1.5rem; margin: 0.5rem 0 0; }
```

- [ ] **Step 4: Wire it up in app.js**

Add these functions:

```js
function flash(button, text) {
    const original = button.textContent;
    button.textContent = text;
    setTimeout(() => { button.textContent = original; }, 2000);
}

function toggleJson(force) {
    const pre = document.getElementById("config-json");
    const toggle = document.getElementById("show-json");
    const shown = force === undefined ? pre.hidden : force;
    pre.hidden = !shown;
    toggle.setAttribute("aria-expanded", String(shown));
    toggle.textContent = shown ? "Hide JSON" : "Show JSON";
}

function selectElement(element) {
    const range = document.createRange();
    range.selectNodeContents(element);
    const selection = getSelection();
    selection.removeAllRanges();
    selection.addRange(range);
}

async function copyText(text, button, fallback) {
    const status = document.getElementById("copy-status");
    try {
        await navigator.clipboard.writeText(text);
        flash(button, "Copied");
        status.textContent = "Copied to the clipboard.";
    } catch {
        // Clipboard access can be refused even in a secure context, for instance when the document
        // does not have focus. A selection lets the visitor finish with their own shortcut.
        status.textContent = "Copying was blocked. The text is selected — press Ctrl+C or Cmd+C.";
        if (fallback) {
            if (fallback.hidden) { toggleJson(true); }
            selectElement(fallback);
        }
    }
}
```

In `onCredentials`, fill the block as well as the download buffer:

```js
function onCredentials(payload) {
    configJson = payload.configJson;
    document.getElementById("config-json").textContent = configJson;
    document.getElementById("steam-id").textContent = payload.steamId;
    show("ready");
}
```

Add the listeners:

```js
document.getElementById("show-json").addEventListener("click", () => toggleJson());
document.getElementById("copy-json").addEventListener("click", event =>
    copyText(configJson, event.currentTarget, document.getElementById("config-json")));
document.getElementById("copy-snippet").addEventListener("click", event =>
    copyText(document.getElementById("snippet").textContent, event.currentTarget,
             document.getElementById("snippet")));

// Delegated, because the pairing values are filled in after this script runs. The content policy
// forbids inline handlers, so the target is named by a data attribute instead.
document.addEventListener("click", event => {
    const button = event.target.closest("button.copy");
    if (button) {
        const value = document.getElementById(button.dataset.copy);
        copyText(value.textContent, button, value);
    }
});
```

- [ ] **Step 5: Verify by hand**

Run the app locally and complete a real registration, or drive the page against a session you push
to `Ready`. Confirm all of these:

1. *Show JSON* expands the block, the label becomes *Hide JSON*, and `aria-expanded` flips.
2. *Copy* puts the exact `rustplus.config.json` text on the clipboard and the button flashes
   *Copied*.
3. *Download* still produces the same file.
4. After pairing, each value's *Copy* copies just that value, and *Copy the snippet* copies the
   constructor line.
5. With the browser's clipboard permission denied, *Copy* selects the text and the status line says
   to press the shortcut.
6. The page still works with a keyboard alone, and the status line is announced.

- [ ] **Step 6: Commit**

```bash
git add apps/RustPlusApi.CredentialsWeb/wwwroot
git commit -m "feat(web): reveal and copy the credentials, not just download"
```

---

## Task 12: Documentation

**Files:**
- Modify: `apps/RustPlusApi.CredentialsWeb/README.md`
- Create: `apps/RustPlusApi.CredentialsWeb/Caddyfile.example`
- Modify: `apps/RustPlusApi.CredentialsWeb/docker-compose.yml`
- Modify: `docs/articles/credentials.md`, `docs/articles/getting-started.md`,
  `docs/articles/troubleshooting.md`, `README.md`, `CLAUDE.md`
- Modify: `docs/superpowers/specs/2026-09-03-credential-acquisition-website-design.md`,
  `docs/superpowers/specs/2026-09-02-browser-agnostic-steam-login-design.md`

**Interfaces:** none.

- [ ] **Step 1: Rewrite the app README**

Replace the *Run it*, *Why this is loopback-only* and *Configuration* sections. It must now say:

- The public instance's URL is the headline route, with the paste step described as two sentences.
- Self-hosting is `docker run -p 127.0.0.1:8080:8080 ghcr.io/handys11/rustplusapi-credentials`, with
  no environment variables, and browsing to it from that same machine gives the automatic redirect.
- Why the paste exists: Facepunch only redirects to a loopback address, decided from the address's
  shape rather than its reachability, so a hosted instance hands over an address nothing listens on
  and the visitor brings the result back.
- The configuration table drops `PublicBaseUrl` and `AllowInsecureBaseUrl` and gains
  `AllowRemotePairing`, described as the LAN self-host switch. `CreatedTtl` becomes 10 minutes.
- The pairing wait is local-only, and why.
- Keep the *What it does with credentials* section, amended: on a hosted instance the Steam token
  arrives in a request body rather than a request line, so it is no longer in any access log. Add
  the two new caveats: the clipboard, and the fact that a copied site could ask for the same paste,
  so the visitor should check the address they are on.

- [ ] **Step 2: Restore the reverse-proxy example**

Recreate `Caddyfile.example`. It must set `X-Forwarded-For` by overwriting rather than appending, so
per-IP caps cannot be spoofed, pair with `CredentialsWeb__KnownProxies__0`, and redact the session
handle from the access log path. The Steam token no longer appears in any URL, so the log guidance
is now only about `/api/sessions/{id}/events`.

- [ ] **Step 3: Simplify the compose file**

`docker-compose.yml` loses its `environment` block for the local case. Add a commented hosted
variant showing `CredentialsWeb__KnownProxies__0` and a port published to the proxy rather than to
loopback. Keep `read_only`, `tmpfs`, `no-new-privileges` and `cap_drop`.

- [ ] **Step 4: Update the user-facing docs**

- `docs/articles/credentials.md`: the website is the recommended route again, with the public URL
  and the paste step. Keep the local route section. Amend *How the Steam login works* to describe
  both callback shapes.
- `docs/articles/getting-started.md` and the root `README.md`: point at the public URL first.
- `docs/articles/troubleshooting.md`: keep the *"Failed to send login message"* entry, since that is
  still what a non-loopback return URL produces, and note it should no longer be reachable through
  the website. Add: the paste says the address could not be read, the paste says it was already
  used, and the pairing button is missing on the public instance.
- `CLAUDE.md`: the `apps/` paragraph drops "loopback-only" and gains one sentence on the two modes.

- [ ] **Step 5: Amend the two specs**

Both are dated records; append rather than rewrite.

- `2026-09-03-credential-acquisition-website-design.md`: under the disproven assumption, note that
  the app is hosted again as of 2026-09-06 by a different mechanism, and link the new spec.
- `2026-09-02-browser-agnostic-steam-login-design.md`: note that the loopback-only answer still
  stands and is now worked around rather than accepted.

- [ ] **Step 6: Verify the docs build**

```bash
docfx docs/docfx.json
```

Expected: no warnings about broken links. Fix any that name a section this task removed.

- [ ] **Step 7: Run everything and commit**

```bash
dotnet build
dotnet test RustPlusApi.sln
tools/coverage/report.sh
dotnet jb cleanupcode RustPlusApi.sln --profile="ReformatAndReorder"
git status --short
git add -A
git commit -m "docs(web): document the hosted instance and the paste flow"
```

Expected: the build is clean, every test passes, both coverage gates pass at line 95 / branch 90,
and `git status` after the formatter shows nothing unexpected.

---

## Self-review

**Spec coverage.** Every section of the spec maps to a task: the verification gate to Task 1; the
mode predicate to Task 2; the session flag to Task 3; pasted-address parsing to Task 4; the return
URL and `callbackMode` to Task 5; the settings removal, `AllowRemotePairing` and the `CreatedTtl`
change to Task 6; `POST /api/callback` to Task 7; the local-only pairing wait to Task 8; the trust
assertion to Task 9; the page's paste section and mode-aware copy to Task 10; reveal and copy to
Task 11; every documentation row to Task 12.

**Known gaps, accepted deliberately.** The spec's "no new rate limiter" decision needs no task. The
browser extension under *Later* is explicitly a separate spec. `wwwroot` has no automated tests
because the repo has no JavaScript test infrastructure; Tasks 10 and 11 carry explicit manual
verification instead.

**Type consistency.** `RequestMode.IsLocal`, `Session.IsLocal`, the `isLocal` parameter of
`SessionStore.TryCreate`, `CreateSessionResponse.CallbackMode` and `.PairingAvailable`,
`AppOptions.AllowRemotePairing`, `CallbackParsing.TryParsePastedCallback`, `PasteCallbackRequest.Url`
and `PasteCallbackResponse.SessionId` are each spelled the same way in every task that touches them.
The `"redirect"` and `"paste"` literals appear in Task 5 (produced) and Task 10 (consumed) only.
