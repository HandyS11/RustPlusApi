# Browser-agnostic Steam login

**Date:** 2026-09-02
**Status:** Approved, not yet implemented
**Scope:** `RustPlusApi.Fcm.Registration` — replace the Chrome DevTools Protocol Steam login with
a plain redirect flow. The credential-acquisition website is a **separate, later** spec.

## Problem

Acquiring Rust+ credentials today requires Google Chrome or Chromium. `SteamLoginService` launches
Chrome with the DevTools protocol enabled, injects a `window.ReactNativeWebView.postMessage` shim
via `Page.addScriptToEvaluateOnNewDocument`, and captures the auth token the Facepunch login page
hands to that shim. Firefox and Safari do not work. This is the single ugliest requirement in the
library: it is documented as a hard prerequisite in nine places, it fails outright in containers and
on headless hosts, and it makes the Steam step untestable — the whole class is
`[ExcludeFromCodeCoverage]`.

## Finding that makes this possible

`https://companion-rust.facepunch.com/login` accepts a `returnUrl` parameter and honours an
arbitrary external URL. Verified 2026-09-02:

1. `GET /login?returnUrl=<url>` reflects the value into the form's hidden `returnUrl` input.
2. `POST /login` (with the antiforgery token) 302s to Steam OpenID with
   `openid.return_to=https://companion-rust.facepunch.com/signin-steam?state=<encrypted>`; the
   `returnUrl` travels inside that encrypted ASP.NET `state` blob.
3. After a real Steam login, the browser lands on the supplied `returnUrl` with the credentials
   appended as query parameters:

   ```
   http://localhost:3000/callback?steamId=765611982…&token=eyJzdGVhbUlkIjoi…
   ```

   Confirmed in **Firefox**, with no extension, no Chrome and no CDP. The `token` value is the same
   Rust+ auth token the `ReactNativeWebView` shim receives (the JWT payload decodes to
   `{"steamId":"…`), so it is a drop-in replacement for what `SteamLoginService` captures today.

The path of `returnUrl` is preserved and the query is appended to it.

### Assumptions to re-verify during implementation

- **A path segment survives the round-trip.** The nonce design below puts it in the path
  (`/callback/<nonce>`); the verification run used a bare `/callback`. If Facepunch strips or
  normalises the path, fall back to carrying the nonce in the `returnUrl` query string and accept
  whatever separator Facepunch uses when appending its own parameters.

  **Outcome (verified 2026-09-03):** Facepunch preserves the path segment and appends its own
  query to it. A full registration completed against the live endpoint with
  `/callback/<nonce>`. The query-string fallback described above was **not** needed and was
  never implemented.
- **Non-3000 ports and `https://` return URLs are accepted.** Only `http://localhost:3000/callback`
  was verified. Nothing suggests an allowlist — the value passes through opaquely — but the
  website spec depends on `https://` working and should confirm it.

  **Outcome (verified 2026-09-03).** Both halves confirmed, and the host question largely closed:

  - `https://localhost:7443/callback/<nonce>` completed a real Steam login end to end and received
    `steamId` plus a 193-character `token` — so the `https` scheme and a non-3000 port are proven.
  - An external host (`https://rustplus-creds.example.org/callback/abc123XYZ`) was reflected
    verbatim by `GET /login` and accepted by `POST /login`, which 302'd to Steam OpenID with no
    validation error. There is no allowlist at either gate; the value rides inside the encrypted
    `state` blob exactly as loopback does.

  Not conclusively proven: the final `/signin-steam` hop to an external hostname, which would
  require `/signin-steam` to deliberately re-validate a value it receives encrypted. Tracked in
  `2026-09-03-credential-acquisition-website-design.md` for re-confirmation against the first
  staging deployment.

## Design

### API shape

`SteamLoginService` splits into a pure half and an interactive half.

```csharp
/// The credentials the Facepunch callback returns.
public sealed record SteamLoginResult
{
    public ulong SteamId { get; init; }
    public string Token { get; init; } = null!;
}

// Pure — no network, no browser, no I/O. Fully unit-testable.
// Public, not internal: the later website is a separate application and cannot use
// InternalsVisibleTo, and these two are exactly what any consumer needs to build its own flow.
public static string BuildLoginUrl(string returnUrl);
public static SteamLoginResult ParseCallback(Uri callbackUri);

// Interactive — binds the loopback listener, reports and opens the URL, awaits the redirect.
public Task<SteamLoginResult> LoginAsync(
    Action<string>? onLoginUrl = null,
    CancellationToken cancellationToken = default);
```

`LoginAsync` becomes: bind `HttpListener` → build `returnUrl` → invoke `onLoginUrl(url)` →
best-effort browser open → await one matching GET → `ParseCallback`.

The existing `HttpListener` lifetime and the cancel-via-`listener.Stop()` registration survive
unchanged, including the `#pragma warning disable RCS1261` for netstandard2.0.

### Decisions

**`onLoginUrl` is always invoked, and the browser open is best-effort.** Rejected: throwing when no
browser can be opened (strands Docker/SSH/WSL users with no path forward) and never auto-opening
(pushes an `xdg-open`/`start`/`open` shim into every consumer). Reporting the URL unconditionally
is also better UX when the browser *does* open — Chrome frequently opens in the wrong profile, and
a visible URL makes that recoverable.

**`LoginAsync` returns `SteamLoginResult`, not `string`.** The callback carries `steamId` for free,
and it is the same Steam64 that later surfaces as `ServerPairing.PlayerId`. Discarding it to
preserve `Task<string>` is not worth it in a major version.
`FcmRegistration.RegisterWithRustPlusAsync` returns the same record for consistency. This is a
**breaking change**, acceptable for v2.

**`steamLoginPort: 0` picks a free port.** The existing `GetFreePort` helper is repurposed rather
than deleted. Default stays `3000` for continuity. Port 3000 collides constantly on dev machines
and the `returnUrl` is now built at runtime, so an auto-port costs three lines.

### Callback nonce

The listener accepts a plain GET at a fixed loopback URL, so any page the user happens to be
browsing could hit `http://localhost:3000/callback?token=<attacker's>` and cause registration
against the attacker's Rust+ account.

This is **not a regression** — the current shim POSTs `text/plain`, a CORS-simple request that any
origin can forge — but it is cheap to close now:

- Generate a cryptographically random per-run nonce.
- Use `returnUrl = http://localhost:{port}/callback/{nonce}`.
- Respond 404 to any other path and keep listening.

### Token in the URL

Facepunch places the token in the query string, so it enters browser history — something the
current shim deliberately avoided by POSTing. Mitigation: the "Done, you can close this window"
response page runs `history.replaceState` to scrub the query. Local-only flow, so there is no
server-side log exposure here; that concern belongs to the website spec.

### Removed

The CDP WebSocket (`ConnectAndInjectAsync`, `GetPageWebSocketUrlAsync`, `SendAsync`, `DrainAsync`),
the `postMessage` shim, `LaunchChrome`, `ResolveChromeLaunch`, `FindChrome`,
`IsFlatpakAppInstalled`, `ResolveOnPath`, `QuoteIfNeeded`, `TryKill`, `TryDeleteDirectory`,
`TryDisposeSocket`, `FlatpakAppIds`, the temp profile directory, and the `CHROME_PATH` contract.
Roughly 350 of ~400 lines.

### Added

Cross-platform default-browser open: `UseShellExecute = true` on Windows, `open` on macOS,
`xdg-open` on Linux. `UseShellExecute` is set explicitly because its default differs between
.NET Framework and .NET Core.

## Error handling

The existing `while (!cancellationToken.IsCancellationRequested)` loop stays — it re-listens rather
than failing on a bad callback, which matters now that a stale browser tab can fire one.

| Case | Behaviour |
|---|---|
| Browser open fails (no `xdg-open`, headless, WSL) | Swallow; keep waiting. The URL already went to `onLoginUrl`, so the user opens it anywhere — including on another machine, because the redirect target is their own browser's localhost. |
| Callback with missing or blank `token` | Error page; keep listening |
| Callback path does not match the nonce | 404; keep listening |
| Port already bound | Wrap the `HttpListenerException` from `Start()` with a message naming the port and suggesting `steamLoginPort: 0` |
| Facepunch changes the callback contract | `ParseCallback` throws `InvalidOperationException` pointing at `RegistrationConstants`, matching the existing "upstream-fragile, re-check the constants" convention |
| Cancellation | Unchanged: `listener.Stop()` under the registration, rethrown as `OperationCanceledException` |

The first row is the point of the whole change: because the loopback listener lives in the user's
own browser's world, "open this URL yourself" is a complete fallback rather than a degraded one.
Under CDP, a missing Chrome is fatal.

## Testing

The coverage picture improves materially. Today the **entire class** is `[ExcludeFromCodeCoverage]`,
justified in `docs/development/testing.md` with the claim *"No pure helpers were extractable."*
That claim becomes false.

**Newly covered by ordinary unit tests** (`BuildLoginUrl`, `ParseCallback`):

- `returnUrl` percent-encoding, including a port and a path segment
- nonce round-trip: URL built with a nonce, callback path matched against it
- `steamId` parsed as `ulong`; non-numeric and missing `steamId` rejected
- missing `token`, empty `token`, whitespace-only `token`
- unexpected extra query parameters ignored
- callback path mismatch rejected

Exact-assertion tests, per the repo convention for the core package, since Stryker cannot mutate
this assembly's siblings.

**Remaining exclusions** (justification narrows from the whole class to these two members): the
best-effort browser launch, and the `HttpListener` accept loop. The existing
`internal LoginAsync(startUrl, …)` seam stays so the loop can be driven against a fake.
`FcmRegistration.RegisterWithRustPlusAsync` stays excluded — it still drives a live login.

Both TFM hosts (net8.0 → netstandard2.0 build, net10.0) must agree, as with every other behavioural
fork in the repo.

## Documentation fallout

Every item below currently promises a Chrome requirement that will no longer exist.

| File | Change |
|---|---|
| `src/RustPlusApi.Fcm.Registration/README.md` L35-37 | Replace the "Steam login requires Chrome/Chromium" caveat with "opens your default browser" |
| `docs/articles/getting-started.md` L8-9 | Drop the Chrome prerequisite entirely |
| `docs/articles/credentials.md` | Mermaid participant `Steam (via Chrome)` → default browser; replace the ~35-line Chrome-discovery section (L75-111) with a short description of the redirect flow |
| `docs/articles/troubleshooting.md` L51-83 | "Chrome/Chromium not found" → "browser didn't open — open the URL yourself" and "port already in use" |
| `docs/articles/introduction.md` L48 | Reword the Steam-login step |
| `docs/articles/recipes.md` L198 | "Chrome will open" → "your browser will open" |
| `docs/articles/samples.md` L23, L30 | Drop the Chrome requirement |
| `samples/README.md` L21, L28-29 | Drop the Chrome requirement |
| `samples/RustPlus.Register.ConsoleApp/Program.cs` L23-24 | Print the login URL via `onLoginUrl` instead of announcing Chrome |
| `CLAUDE.md` L61 | "Steam login via Chrome DevTools Protocol" → redirect flow |
| `docs/development/testing.md` L183-196 | Rewrite the exclusion justification; narrow from the whole class to the two interactive members |

The `Task<string>` → `Task<SteamLoginResult>` break needs calling out in the release notes and PR
body — the repo has no CHANGELOG.

## Out of scope

- The credential-acquisition website. This spec ends at "the library no longer needs Chrome."
  `BuildLoginUrl` and `ParseCallback` are what that website will consume, but nothing web-facing is
  built here.
- Any change to `AndroidFcmRegister`, `ExpoPushClient`, `RustCompanionClient`, `PairingListener` or
  `CredentialsStore`.
- `RegistrationConstants.ChromeVersion` and the `ChromeBuildProto` check-in fields, which describe
  the *spoofed device identity* sent to Google and have nothing to do with a locally installed
  browser.

## Success criteria

1. `RustPlus.Register.ConsoleApp` completes a full registration on a machine with **no Chromium
   installed** and Firefox as the default browser.
2. The same run completes with the browser-open step sabotaged, by opening the reported URL by hand.
3. No occurrence of `CHROME_PATH`, `chromium` or `flatpak` remains in
   `RustPlusApi.Fcm.Registration` or in user-facing docs, and the only surviving `Chrome` matches
   are the check-in device-identity ones named under **Out of scope**
   (`RegistrationConstants.ChromeVersion`, `Protobuf/CheckinContracts.cs`,
   `AndroidFcmRegister` and their tests).
4. `dotnet test RustPlusApi.sln` passes on both TFM hosts; `tools/coverage/report.sh` passes the
   95/90 gate with the narrowed exclusion list.
5. `dotnet build` is clean under `TreatWarningsAsErrors`, and `dotnet jb cleanupcode` produces no
   diff.
