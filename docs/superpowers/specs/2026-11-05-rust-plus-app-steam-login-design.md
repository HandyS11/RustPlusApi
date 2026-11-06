# Steam login through Facepunch's Rust+ app redirect

**Date:** 2026-11-05
**Status:** Approved by the owner 2026-11-06. Spike passed on Linux (Flatpak Chrome) and Windows (Edge).
**Scope:** `RustPlusApi.Fcm.Registration` (the Steam login step) and the
`RustPlus.Register.ConsoleApp` sample. The credentials website and a browser extension are a
**separate, later** project.

## Why

Facepunch rebuilt `companion-rust.facepunch.com/login` in early October 2026. Since then the Steam
login built by `SteamLoginService` never comes back. The browser ends on
`https://rust.facepunch.com/companion` and the terminal waits forever. This breaks
`FcmRegistration.RegisterWithRustPlusAsync`, which is public API, for every consumer of the NuGet
package, not just the sample.

PR #118 (2026-09-03, design `2026-09-02-browser-agnostic-steam-login-design.md`) deliberately
removed browser control. It relied on Facepunch redirecting to a `http://localhost` return address,
and that redirect is exactly what Facepunch removed. The owner has accepted reintroducing browser
control "since they changed the rules".

## What we know (verified 2026-11-05 by the owner, Firefox on Linux, real Steam login)

1. Our absolute `returnUrl` (`http://localhost:8080/callback/<nonce>`) survives the whole chain:
   Steam → `302 /signin-steam?state=…` → `302 /app?returnUrl=http://localhost:8080/callback/…`.
   `/app` then answers **200 with an HTML page** instead of redirecting. Logged out, the same
   `/app` URL renders "Rust+ - Login Failed".
2. The official app's return address works:
   `https://companion-rust.facepunch.com/login?returnUrl=%2Fapp%3FreturnUrl%3Drust-plus%3A%2F%2Frust-auth-session`
   gives Steam → `/signin-steam` → `302 /app?returnUrl=/app?returnUrl=rust-plus…` →
   `302 /app?returnUrl=rust-plus://rust-auth-session` → **`302 Location:
   rust-plus://rust-auth-session?steamId=<steam64>&token=<url-encoded token>`**. Firefox then fails
   with `NS_ERROR` because nothing handles `rust-plus://`.
3. **Windows block, confirmed with a control run.** The same flow with a Windows Firefox
   User-Agent (`Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:159.0) …`) makes the final
   `/app?returnUrl=rust-plus://…` hop answer **200** (Facepunch's "Login Failed") instead of the
   302. The very next run with the normal Linux UA got the 302. Community report
   (Trophy198/rustplusplus-credentials-extension PR #6) says the trigger is the substring
   "windows" in the UA, case-insensitive, and that client hints are ignored.
4. Pressing Steam's "Sign in" again on an already-used Steam page ends at
   `/login-failed?returnUrl=&reason=cancelled`.
5. The token is `base64(JSON{steamId, version:0, iss, exp}).base64(signature)` and lives
   **14 days** (exp − iss = 1,209,600 s). It is the same kind of token the old flow returned.
6. Facepunch's `/login` page is now an interstitial ("You are signing in to a third-party app…")
   carrying this notice to developers: *third-party apps must sign players in through pages which
   display this warning; the warning must be shown as-is; do not hide, alter, cover or restyle it,
   and do not press Continue on the player's behalf.*

Community prior art for the new chain: Trophy198/rustplusplus-credentials-extension PRs #5 and #6,
and Trophy198/rustplusplus-credential-page PR #82.

## Goals

- `RegisterWithRustPlusAsync` works again on **Windows** (Edge, which every Windows machine has),
  macOS and Linux, with the player signing in once in a browser window the library opens.
- The player sees Facepunch's and Steam's pages untouched and presses every button themselves.
- No new NuGet dependency. Both TFMs (netstandard2.0 + net10.0) behave the same.
- A complete fallback for machines without a usable browser (Firefox-only, containers, SSH,
  headless): the developer pastes the `rust-plus://` address.
- Existing consumer code keeps compiling.

## Non-goals

- The credentials website (`apps/RustPlusApi.CredentialsWeb`) and a browser extension. Both are the
  next project. The website stays broken until then. Its `BuildLoginUrl`/`ParseCallback` usage
  must keep compiling.
- Driving Firefox (WebDriver BiDi). Firefox users get the paste fallback.
- Token refresh or the 14-day expiry. That concerns the companion-socket work on
  `feat/companion-socket`, not this fix.
- Registering an OS handler for `rust-plus://` links. It cannot work on Windows (the browser never
  gets the redirect) and would fight the official Rust+ desktop app for the scheme.

## Anti-goals (failure even if it "works")

- Anything that hides, alters, covers, restyles or auto-presses Facepunch's warning page or Steam's
  login page, or injects script into either.
- The token or Facepunch/Steam session leaving the machine, or appearing in logs or `Debug` output.
- The temporary browser profile, which holds the player's Steam and Facepunch sessions, left on
  disk after the login ends, including after failure or cancellation.
- Triggering the official Rust+ desktop app's `rust-plus://` handler.
- Going back to "the whole class is excluded from coverage".

## Design

### Flow

1. Build the login URL: `SteamLoginService.BuildLoginUrl(RustPlusAppReturnUrl)`, where a new
   constant in `RegistrationConstants` is `RustPlusAppReturnUrl =
   "/app?returnUrl=rust-plus://rust-auth-session"`. The existing escaping yields exactly the URL
   verified above. Always pass it to `onLoginUrl`, as today.
2. Find a Chromium-family browser (next section). If none is found, or launching it fails, go to the
   paste fallback.
3. Launch it with a **fresh temporary profile** and the DevTools remote-debugging port on loopback.
   Connect over the Chrome DevTools Protocol (CDP).
4. Enable `Fetch` interception **only** for `https://companion-rust.facepunch.com/app*`, at both the
   Request and Response stages, then navigate the page to the login URL. Nothing else is
   intercepted or modified, except for the whole-tab fallback in step 5.
5. **Request stage** (`/app`): if the request's `User-Agent` contains "windows"
   (case-insensitive), continue it with that header replaced by an equivalent non-Windows UA. If a
   `Sec-CH-UA-Platform` header is present, rewrite it to match. (My call: the claim that Facepunch
   ignores client hints rests on one community report, and keeping the two consistent costs
   nothing.) `Fetch.continueRequest` header overrides do not carry over to redirect hops, so each
   `/app` hop is handled on its own. Otherwise continue unchanged.
   **Fallback, my call:** if the spike shows the per-hop override does not get past Facepunch (for
   example because the UA is tied to the session at `/signin-steam`), override the UA for the whole
   tab instead, with `Emulation.setUserAgentOverride` including `userAgentMetadata` with a
   non-Windows platform, applied only when the browser's own UA contains "windows". Steam and the
   warning page then see the non-Windows UA too. That is acceptable: it changes no page content and
   presses nothing. It is the same thing the owner already accepted, just applied more widely.
6. **Response stage** (`/app`):
   - A 3xx whose `Location` starts with `rust-plus://rust-auth-session`: parse it with the existing
     `SteamLoginService.ParseCallback(new Uri(location))`, then `Fetch.fulfillRequest` with a small
     200 "Done, you can close this window and return to your terminal" page. The browser never
     attempts the `rust-plus://` link, so the official desktop app is never triggered.
   - A 3xx to a relative `/app?...` (the middle hop): continue.
   - A 200 (Facepunch's "Login Failed"): let the page show, then fail with an
     `InvalidOperationException` saying Facepunch refused the login at its final step.
7. Close the browser, wait for it to exit, delete the temporary profile (retrying on locked files),
   and return the `SteamLoginResult`.

### Finding the browser

- Search order (builder's call): an explicit path the caller supplied, then known install locations
  per OS, then `PATH`, then Flatpak on Linux. It must find **Edge** on Windows, and **Flatpak
  Chrome** (`com.google.Chrome`) on Linux, since that is the owner's only Chromium-family browser.
- Use the pre-#118 code as **reference**, not as-is: `git show
  0c66a38^:src/RustPlusApi.Fcm.Registration/Steps/SteamLoginService.cs`. It has `FindChrome`,
  `IsFlatpakAppInstalled`, `FlatpakAppIds`, `ResolveOnPath`, `LaunchChrome`/`ResolveChromeLaunch`,
  `QuoteIfNeeded`, `TryKill` and `TryDeleteDirectory`. It had **no tests**, because the whole class
  was excluded from coverage, and it read the real environment and file system directly. Rewrite
  the lookup behind injected file-system, environment and OS-check seams, and write its tests from
  scratch. CI runs only on Ubuntu, so the Windows and macOS lookup branches are covered through
  those fakes. Extend the lookup with Edge, Brave and Chromium.
- **Snap (my call): detect and skip.** A browser resolved under `/snap/` (for example Ubuntu's
  `/snap/bin/chromium`) is treated as unusable, and the login goes to the paste fallback with a
  message saying why. Snap's private `/tmp` and its home-directory rules would leave a profile the
  library cannot delete, and nobody here can verify a Snap-specific path. Snap support can be added
  later by someone who can test it.
- **Flatpak sandbox:** the sandbox has its own `/tmp`. Use a probed free port passed explicitly
  (`--remote-debugging-port=<port>`) instead of `0` plus the `DevToolsActivePort` file. Put the
  profile somewhere both the sandbox and the host can reach, for example
  `~/.var/app/<app-id>/cache/<random>`. Verify this on the owner's machine.
- Launch flags (builder adjusts): `--remote-debugging-port=<port>`, `--user-data-dir=<temp>`,
  `--no-first-run`, `--no-default-browser-check`, and a blank start page. A non-default
  `--user-data-dir` is required: Chrome 136+ refuses remote debugging on the default profile. So
  the player signs in to Steam in that window even if signed in elsewhere.

### Cleanup and lifetime (my calls)

- Every profile lives under a library-owned root: `<temp>/RustPlusApi.SteamLogin/<random>`, or for
  Flatpak `~/.var/app/<app-id>/cache/RustPlusApi.SteamLogin/<random>`.
- At the start of each login, sweep that root for leftovers from earlier runs. It must never delete
  a profile an in-progress login (another process) is still using. The mechanism is the builder's
  call; for example, each live login holds an exclusively opened lock file in its profile, and the
  sweep skips any profile whose lock it cannot take.
- **Deletion still failing after the retries** (Edge background processes can hold files briefly):
  return the token anyway. The player did everything right, and a valid login must not be thrown
  away. Write a `Debug` line naming the directory (a path, not a secret) and leave it to the next
  sweep. Document this.
- **The host process is killed hard** (debugger stop, `kill`, crash): `finally` does not run. The
  browser window may stay open; the player closes it, and the next login sweeps the profile. No
  Windows Job Object / P/Invoke in this change. Document this.
- **The sample wires Ctrl+C** (`Console.CancelKeyPress`: cancel the token, set `e.Cancel = true`) so
  the ordinary Ctrl+C path closes the browser and deletes the profile.

### Paste fallback

When no browser is found or launching it fails:

- The library reports the login URL through `onLoginUrl`. It then asks the caller for the address
  through a new optional callback (shape is the builder's call, for example
  `Func<CancellationToken, Task<string>>`), parses it with `ParseCallback`, and accepts both the
  raw `rust-plus://…` address and one pasted with surrounding whitespace.
- If the caller supplied no paste callback, throw an `InvalidOperationException` that explains how
  to get the address: open the URL, sign in, then in developer tools → Network find the `app?…`
  request's `Location` header. On Windows browsers, first set a non-Windows user agent in
  developer tools.
- The sample supplies a `Console.ReadLine` paste callback and prints those instructions.

### Public API (my calls, constrained by "existing code keeps compiling")

- Keep `FcmRegistration.RegisterWithRustPlusAsync(Credentials, Action<string>?, CancellationToken)`
  and `SteamLoginService.LoginAsync(Action<string>?, CancellationToken)`, with the same signatures
  and new behaviour.
- Add an overload or options type for the new inputs (paste callback, explicit browser path) with
  no ambiguous overload resolution for existing calls. Shape is the builder's call. Document it.
- `FcmRegistration(…, int steamLoginPort = 3000)` and `SteamLoginService(int port = 3000)`: keep the
  parameters, document them as no longer used, and do **not** mark them `[Obsolete]`. That would
  turn into build errors for consumers with warnings-as-errors. An unread primary-constructor
  parameter raises CS9113, which fails this repo's build, so each needs a targeted suppression with
  a comment saying why it is kept.
- Keep `BuildLoginUrl` and `ParseCallback` public and unchanged. The website uses them.
  `ParseCallback` must accept the `rust-plus://` URI (add tests).
- The loopback `HttpListener` flow is removed.

### Errors

| Case | Behaviour |
| --- | --- |
| No browser found, or launch fails | Paste fallback; without a paste callback, `InvalidOperationException` with instructions |
| Player closes the browser window before finishing | `InvalidOperationException` ("browser closed before the login finished"); profile still deleted |
| `/app` answers 200 (Facepunch refused) | `InvalidOperationException` naming Facepunch's refusal and, when the UA was overridden, saying so |
| `/login-failed?reason=…` reached (e.g. `cancelled`) | Builder's call whether to observe it. If observed, fail with the reason and suggest restarting the login |
| `Location` carries no usable token/steamId | Existing `ParseCallback` `InvalidOperationException` |
| Cancellation | `OperationCanceledException`; browser closed, profile deleted |

### Security

- Never write the token, the `Location` value or cookies to logs, exceptions or `Debug` output.
  Error messages may name the URL path, never the query.
- Bind the debugging port to loopback, which is Chrome's default; never pass
  `--remote-debugging-address`. Any local process could attach during the login window. This is
  accepted as a local risk and documented, as the old CDP flow did.
- Delete the profile on every exit path.

## Testing

Follow `docs/development/testing.md`: multi-TFM parity, exact assertions, 100/100 line/branch except
justified `[ExcludeFromCodeCoverage]` members, and Stryker for `RustPlusApi.Fcm.Registration`.

- Put the CDP orchestration (steps 4–7) behind a transport seam and test it against a fake CDP
  endpoint. Cover the success redirect, middle hop, UA override on and off, the 200 refusal, window
  closed, cancellation, unrelated Fetch events, and command errors.
- Unit-test browser finding against a fake file system, environment and `PATH`, including
  Flatpak; the UA rewrite (result never contains "windows", product tokens kept); the paste
  parsing; and `ParseCallback` with `rust-plus://`.
- Only launching a real process and opening the real WebSocket may be excluded. Each exclusion
  gets a per-member justification in `docs/development/testing.md`, which replaces the
  `HttpListener`/`TryOpenBrowser` entries.
- **Spike first, before building the rest (owner agreed 2026-11-05):** a throwaway console program,
  never committed, that runs steps 1–7 and prints each `/app` hop's status, whether the UA was
  changed, the Steam ID and the token's expiry, and whether the profile was deleted. It never prints
  the token, cookies or the `Location` query, and it registers nothing with Rust+.
  1. Run on the owner's Linux machine with Flatpak Chrome.
  2. Then published as a self-contained `win-x64` single-file exe, so no .NET SDK is needed on
     Windows, and run by the owner on Windows with Edge: a control run without the override, which
     should show Facepunch's refusal; then the per-request override; and only if that fails, the
     whole-tab fallback from step 5.

  It must confirm that `Fetch` Response-stage interception fires for the `/app` 302 and can fulfil
  it, and that the UA override gets past Facepunch on real Edge. If response-stage interception of
  redirects does not work, fall back to observing `Network.requestWillBeSent` for the
  `rust-plus://` URL (its `redirectResponse`), then navigating to a local done page. The spike's
  results decide the per-request versus whole-tab choice before the real build starts.

  **Linux result (2026-11-05, Fedora 44, Flatpak Chrome 157, Windows UA faked with
  `--user-agent`):** worked as designed. Both `/app` hops were seen at the Request stage and had
  their UA changed per request. The final hop's 302 to `rust-plus://rust-auth-session` was caught
  at the **Response stage** and fulfilled with the done page. The token was 193 characters and
  expired 14 days later. The Flatpak profile under `~/.var/app/com.google.Chrome/cache/…` was
  deleted. The `Network.requestWillBeSent` fallback was not needed.

  **Windows result (2026-11-06, the owner's Windows 11 10.0.26200 x64, real Microsoft Edge,
  self-contained `win-x64` exe):**
  - Control run (`none`): the middle hop answered 302, and the last hop answered **200**
    (Facepunch refused). The Windows block is confirmed on genuine Edge.
  - Per-request run (`hop`, UA plus `Sec-CH-UA-Platform` rewritten on each `/app` request): both
    hops passed, and the last hop's 302 to `rust-plus://rust-auth-session` was caught at the Response
    stage. The token was 193 characters and the profile was deleted.

  **Decision:** use the per-request override. The whole-tab fallback in step 5 was not needed and is
  **not implemented**. It stays documented as the next step if Facepunch tightens the check.
- **Manual acceptance:** a full `RustPlus.Register.ConsoleApp` run on the owner's Linux machine and,
  by the owner, on Windows with Edge. Both must pair a server.

## Docs to update

`docs/articles/credentials.md`, `src/RustPlusApi.Fcm.Registration/README.md`, `samples/README.md`,
`docs/articles/troubleshooting.md` (the Windows "Login Failed" and what the library does about it,
the Firefox-only and Snap paste fallback, `reason=cancelled`), `docs/development/testing.md`, and
every place that describes the **console** loopback redirect, including `CLAUDE.md`, `AGENTS.md`
and `.github/copilot-instructions.md` (grep for "loopback" and "redirect"). State plainly that the
Windows step tells Facepunch the browser is not on Windows, which Facepunch appears to block
deliberately, so it may break again.

**Website passages (my call): a notice, not a rewrite.** Passages that describe the credentials
*website* (for example the "credentials website (recommended)" and "How the Steam login works"
sections of `docs/articles/credentials.md`, `docs/articles/troubleshooting.md` around lines
120–139, and the app's README) get a short notice: the website has been broken since Facepunch's
October 2026 change, so use the console tool until it is fixed. Any recommendation of the website
over the console tool is reversed. Rewriting them belongs to the website project.

## Delivery

- New branch from `develop` (my call: `fix/steam-login-rust-plus-app`), not
  `feat/companion-socket`. Conventional commits; this is a `fix:` with no breaking public signature.
- Nothing is committed or pushed without the owner asking.

## Left to the builder

The exact search order and Edge/Brave/Chromium install paths; the options/overload shape; the
transport seam's shape; the UA replacement string; whether `/login-failed` is observed; the done
page's wording; and file and class layout inside `Steps/`.
