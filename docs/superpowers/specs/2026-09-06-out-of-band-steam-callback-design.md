# Out-of-band Steam callback

**Date:** 2026-09-06
**Status:** Approved. **The gate in *Verify this first* passed on 2026-09-06 — see the outcome recorded there.** Implementation in progress.
**Scope:** `apps/RustPlusApi.CredentialsWeb` and its documentation. Restores the hosted instance that
issue [#126](https://github.com/HandyS11/RustPlusApi/issues/126) invalidated, by taking the Facepunch
redirect out of band. Also adds reveal-and-copy to the credential hand-off. **No change to any
shipped package.**

## Problem

`2026-09-03-credential-acquisition-website-design.md` designed a public instance. Issue #126 proved
it cannot work: Facepunch's `/signin-steam` only honours the `returnUrl` redirect when that URL is
loopback, and hands anything else to a `ReactNativeWebView` bridge that exists only inside the Rust+
app. PR #127 documented the app as loopback-only in response.

That leaves a non-technical user with `docker run` as the only route to credentials, which is itself
a technical ask. The goal is a URL someone can visit.

## The finding this rests on

From #126, in the repo's own words: *"Facepunch's servers cannot reach a visitor's `localhost`
either, yet loopback works. The decision is made from the URL's shape, not its reachability."*

A hosted instance can therefore hand Facepunch a **loopback URL that nothing is listening on**. The
redirect still fires, in the visitor's own browser, and their browser shows a connection error with
the full callback URL — `steamId` and `token` included — sitting in the address bar. The visitor
copies that address and pastes it back into the page they started from. The server never needed to
receive the redirect; it only needed the URL.

This is the out-of-band pattern that pre-loopback OAuth clients used, and that `rclone authorize`
still uses.

### Why a backend cannot simply "be" localhost

Rejected, and worth recording because it is the obvious first idea. Facepunch answers the final
Steam hop with a `302`, and the **visitor's browser** follows it. `localhost`, `*.localhost`,
`127.0.0.0/8` and `::1` are resolved inside that browser without DNS, so no hostname exists that
Facepunch accepts as loopback and the browser sends to us. The only way a server receives that
redirect is to be the browser that logged in, which means driving a headless browser with the
visitor's Steam password or QR approval. That is the shape of a phishing flow and leaves the server
holding a Steam web session. Not built.

## Verify this first

**Implementation must not start until this passes.** Everything below assumes Facepunch takes the
legacy redirect branch for a loopback `returnUrl` **supplied from a non-loopback origin**. #126
proved the branch keys on the URL's shape, but every successful run so far also had a loopback
*origin*, so a `Referer`-based or origin-based gate has not been ruled out.

The probe, on the LAN setup from row 2 of #126 (plain HTTP, no proxy, no TLS):

1. Serve one static page from the LAN address with a link to
   `https://companion-rust.facepunch.com/login?returnUrl=http%3A%2F%2Flocalhost%3A54321%2Fcallback%2F<32 hex>`.
2. Click it, complete a real Steam login.
3. **Pass:** the browser lands on a connection error for `http://localhost:54321/callback/<nonce>`
   with `steamId` and `token` in the address bar.
   **Fail:** the visitor sees *"Failed to send login message to the Rust+ app."*

Repeat from a `file://` page or a typed URL to separate "no `Referer`" from "LAN `Referer`". If it
fails, this design dies and the browser extension sketched under *Later* is the remaining route.

### Outcome: PASS, 2026-09-06

Run from a static page served over plain HTTP at a LAN address, with a `returnUrl` of
`http://localhost:<dynamic port>/callback/<32 hex>`. A real Steam login redirected the browser to
exactly that address, with `steamId` and a `token` appended, landing on the browser's own
"unable to connect" page with the full address in the address bar.

So Facepunch reads the shape of the `returnUrl` and nothing else: not the origin the login was
started from, not the `Referer`. A hosted instance can hand over a loopback address it does not
own and have the visitor bring the result back. The design holds.

The second probe, from a typed URL with no referrer, was not needed: the LAN-origin run already
carried a non-loopback `Referer` and succeeded, which answers the stronger question.

## Decisions

**Mode is derived per request, not configured.** A request is **local** when both of these hold:

- the connection's remote address is loopback (`IPAddress.IsLoopback`, after `MapToIPv4()` for an
  IPv4-mapped IPv6 address such as `::ffff:127.0.0.1`, which `IsLoopback` otherwise rejects);
- the `Host` header names a loopback host: exactly `localhost`, any `*.localhost` (reserved by
  RFC 6761 and resolved to loopback by browsers), or an IP literal that is loopback once the IPv6
  brackets are trimmed.

Both halves are needed. The `Host` header alone is forgeable, but a forged value only redirects the
forger's own browser to their own machine, so nothing of ours leaks. The remote address alone is
wrong in the deployment that matters: a reverse proxy on the same host makes every visitor look
loopback, which would hand strangers the local behaviour. The `Host` check is what prevents that.

Rejected: a `Mode` setting. It reintroduces exactly the class of mistake #126 is about, where a
wrong value starts cleanly and fails later at the Steam step.

**Local keeps today's automatic redirect.** The return URL is built from the request itself
(`{scheme}://{host}/callback/{returnToken}`), so the redirect lands on the app that issued it, with
no configuration to get wrong. `GET /callback/{returnToken}` is unchanged in behaviour: only the
source of its base URL moves from configuration to the request.

**Hosted uses a dead loopback return URL plus a paste.**
`http://localhost:{port}/callback/{returnToken}`, where `port` is drawn per session from the dynamic
range with `RandomNumberGenerator.GetInt32(49152, 65536)`.

Rejected: a browser-blocked port (Chrome's `ERR_UNSAFE_PORT` list). It would guarantee no local
service ever receives the token, but a browser is free to abort the navigation rather than render an
error page, and then the URL never reaches the address bar. Connection-refused is the predictable
behaviour, and a random dynamic port makes a collision with a real local listener remote. If one
happens anyway, the single-use return token means the visitor's paste fails closed with "already
used" rather than the flow completing somewhere else.

**`PublicBaseUrl` and `AllowInsecureBaseUrl` are deleted, not deprecated.** Nothing needs the
external origin once the return URL comes from the request or from a fixed loopback string. Keeping
them as no-ops would preserve a setting whose only remaining function is to look load-bearing. Both
are ignored with a startup warning naming them, so an existing `docker run` keeps working instead of
failing validation.

This removes the app's only required setting, which fixes the minor complaint at the foot of #126:
the capacity messages tell the visitor to run
`docker run -p 8080:8080 ghcr.io/handys11/rustplusapi-credentials`, and that command now works.

**The pairing wait is local-only.** It is the one step that holds an MCS socket per visitor, and the
public instance's purpose is the FCM credentials, which are the mandatory half. A session records
whether it was created locally; the pairing endpoint refuses otherwise. `AllowRemotePairing`
(default `false`) re-enables it for someone self-hosting on a LAN address, who is in the same
position as a loopback user but fails the `Host` check.

Net configuration change: two settings removed, one added.

**The credentials are revealed and copied as well as downloaded.** Hidden behind an explicit toggle
rather than shown by default, so the JSON is not on screen during an incidental screen share.

## Architecture

```
apps/RustPlusApi.CredentialsWeb/
    AppOptions.cs              - PublicBaseUrl, - AllowInsecureBaseUrl, + AllowRemotePairing
    Program.cs                 + startup warning for the two removed settings
    Endpoints/
        RequestMode.cs         NEW. The loopback predicate. Pure, unit-testable.
        CallbackParsing.cs     NEW. Shared parse helpers for both callback shapes.
        CallbackEndpoints.cs   GET unchanged in behaviour; + POST /api/callback
        SessionEndpoints.cs    mode-aware returnUrl; pairing refusal when remote;
                               capacity messages point at the bare docker run
    Sessions/Session.cs        + IsLocal
    wwwroot/                   paste section, reveal/copy, mode-aware copy
```

### Endpoints

| Route | Change |
|---|---|
| `POST /api/sessions` | Returns `{ sessionId, loginUrl, callbackMode, pairingAvailable }`. `callbackMode` is `"redirect"` or `"paste"`. The return URL inside `loginUrl` is the request's own origin when local, and `http://localhost:{random}/callback/{returnToken}` when not. |
| `GET /callback/{returnToken}` | Unchanged, except the callback URI is rebuilt from the request rather than from `PublicBaseUrl`. Local mode only in practice; left mapped unconditionally, since a hosted instance simply never issues a URL that points at it. |
| `POST /api/callback` | **New.** Body `{ "url": "<pasted>" }`. Parses, then consumes, then starts the same background flow. |
| `POST /api/sessions/{id}/pairing` | `403` with an explanatory payload when the session is not local and `AllowRemotePairing` is off. |
| `GET /api/sessions/{id}/events` | Unchanged. |

### Parsing a pasted URL

Order matters, and it differs deliberately from the `GET` handler: **parse first, consume second**,
so a fumbled paste does not burn the session.

1. Trim. If it does not parse as an absolute URI, retry with `http://` prepended — Safari copies
   without the scheme.
2. Take the last path segment as the return token and require 32 lowercase hex characters.
3. Hand the whole URI to `SteamLoginService.ParseCallback`, which yields `steamId` and `token` or
   throws.
4. Only now `TryConsumeReturnToken`. On success, start `CompleteRegistrationAsync` exactly as the
   redirect does.

| Outcome | Response |
|---|---|
| Parsed and consumed | `202` with `{ sessionId }` |
| Not a URL, wrong path shape, or no usable `token`/`steamId` | `400`, "That doesn't look like the Rust+ callback address. Copy the whole address from the failed page, starting with `http://`." Nothing consumed, so the visitor can retry. |
| Return token unknown or already used | `404`, "That link was already used, or the session expired. Start over." |

No new rate limiter. The return token is 128 bits, so guessing is not a threat model, and the
existing per-IP completion cap already bounds what a successful guess could achieve.

### The page

**Mode-aware copy.** The static page picks its initial wording from `location.hostname`, then
corrects itself from the create-session response, which is authoritative. The current trust bullet
claiming nothing leaves your computer is true locally and false when hosted, so it forks:

- local, unchanged in substance;
- hosted, stating plainly that the Steam auth token reaches this server through the paste, lives in
  memory only, is dropped the moment the device is registered, and is never logged or persisted.

**Paste section.** Clicking *Sign in with Steam* in hosted mode creates the session but does not
navigate. It swaps in a section containing a link that opens the Steam login in a new tab, a description of the connection
error to expect, and the paste box. The link is a plain anchor rather than a scripted popup: opening
a window after an `await` risks the popup blocker. The box submits on paste when the value parses,
with a button as the fallback, and the original tab keeps its event stream open throughout.

The paste box is also offered as a rescue for a redirect that did not land, in both modes.

**Deviation, recorded in the final review, 2026-09-06.** The implementation offers that rescue from
the **progress** screen, not the failure screen this paragraph originally specified, and the
implementation is right. Every route into `Failed` runs *after* `TryConsumeReturnToken` — the `GET`
callback handler consumes before it can advance to `Failed`, and both `CredentialFlow` failure paths
are downstream of that — so by the time the failure screen is on display the session's return token
is gone and a paste there could only ever answer `404`. The progress screen is where a redirect that
never arrived actually strands the visitor, and their token is still live at that point. The design
text is what is corrected here; the code stays as it is.

**Reveal and copy.** On `Ready`, beside the existing download: a *Show JSON* toggle carrying
`aria-expanded`, revealing the config in a `<pre>`, and a *Copy* button using
`navigator.clipboard.writeText`. Clipboard access needs a secure context, which both `https` and
`http://localhost` satisfy. The fallback selects the text and tells the visitor to press their copy
shortcut. Confirmation is a transient label change plus an `aria-live="polite"` announcement.

On `Paired`, the same treatment per value for `ip`, `port`, `playerId` and `playerToken`, and for
the `RustPlus` constructor snippet. When pairing is unavailable, the section is replaced by a line
explaining that the four values need a listener, pointing at running the app locally or at
`PairingListener` in the library.

### TTL

`CreatedTtl` rises from five minutes to ten. A paste-mode visitor spends that window on a real Steam
login, two-factor included, plus a copy and a paste, and the existing code already notes that a
round trip can consume most of the old value.

## Trust

Unchanged from the 2026-09-03 design except where the paste improves or complicates it.

**Improved.** The Steam token no longer appears in any request line on a hosted instance, because it
arrives in a `POST` body. That retires the "honest admission" and the access-log gap the earlier
design had to accept. What remains loggable is the session handle in the event-stream path.

**New, and stated on the page as well as here.** The token passes through the visitor's clipboard,
where a clipboard manager may retain it. And a paste box is a social-engineering surface: a copied
site could ask for the same paste and harvest tokens. That is not a new capability, since anyone can
already clone and host this app, but the instruction "paste this URL here" normalises the behaviour,
so the page names the origin the visitor should be on.

**Unchanged.** In-memory only, no disk, no database. The token is dropped the moment the companion
registration succeeds, and on failure and cancellation alike. No credential is logged, asserted by
`SecretsAreNeverLoggedTests` rather than intended.

**Amendment — found in the final review, 2026-09-06.** The list above was incomplete: it named the
clipboard and missed the browser history. `GET /callback/{returnToken}` answers `302` specifically so
the token-bearing URL never becomes a history entry, and the README repeats that claim — but in paste
mode the visitor's browser *navigates to* the token-bearing loopback URL itself, fails to connect,
and leaves that URL in the failed tab's address bar and in its session history. On a signed-in,
syncing profile that history may reach the browser vendor's cloud. So the paste is not a pure
improvement over the redirect: it takes the token out of every request line and out of every access
log, and puts it into the visitor's own browser history instead.

Nothing in the code can avoid this — the navigation happens in the visitor's browser, to an address
no server of ours receives, and Facepunch has already committed the token to that URL by the time
anything of ours could intervene. The mitigation is therefore instructional and has to be honest
about being only that: the page tells the visitor to close the failed tab once the paste has landed,
and the app README's security section records the exposure alongside the `302` claim it qualifies.

## Bounding

The caps stop being vestigial. With a public instance they are load-bearing again, and
`KnownProxies` must be set for per-IP accounting to mean anything behind a proxy. The proxy must
overwrite `X-Forwarded-For` rather than append to a client-supplied value.

`MaxConcurrentPairings` stays and applies to local and `AllowRemotePairing` deployments; the public
instance holds no sockets at all.

## Testing

New, and all ordinary unit tests:

- `RequestMode` — loopback and non-loopback remote addresses, IPv4-mapped IPv6, `localhost`,
  `sub.localhost`, `127.0.0.1`, `[::1]`, a public `Host` with a loopback connection (the proxy case,
  must be remote), a loopback connection with a forged public `Host`, and the reverse.
- `CallbackParsing` — valid paste, missing scheme, surrounding whitespace, wrong path shape,
  non-hex token, absent `token`, absent `steamId`, the Facepunch login URL pasted by mistake.
- `POST /api/callback` — success starts the flow; a `400` consumes nothing and a retry with a good
  URL then succeeds; replay of a consumed token gives `404`.
- `POST /api/sessions` — the return URL is the request origin when local and a loopback URL with a
  dynamic-range port when not; `callbackMode` and `pairingAvailable` match.
- Pairing — `403` for a remote session, allowed when `AllowRemotePairing` is set.
- `SecretsAreNeverLoggedTests` extended: drive a paste and assert the token appears in no log record.
- `AppOptionsValidatorTests` — the two removed settings' cases go; the startup warning is asserted.

The two coverage gates keep their 95 line and 90 branch bar.

## Documentation fallout

| File | Change |
|---|---|
| `apps/RustPlusApi.CredentialsWeb/README.md` | Rewrite. Leads with the public URL, documents both modes, drops the removed settings, restores reverse-proxy guidance. |
| `apps/RustPlusApi.CredentialsWeb/Caddyfile.example` | Restore, with the log filter narrowed to the session-handle path. |
| `apps/RustPlusApi.CredentialsWeb/docker-compose.yml` | Local example loses the environment block; a hosted example is added. |
| `docs/articles/credentials.md` | The website becomes the recommended route again, with the public URL. |
| `docs/articles/getting-started.md`, root `README.md` | Same reversal. |
| `docs/articles/troubleshooting.md` | Keep the *"Failed to send login message"* entry, since it is what a wrong return URL still produces. Add paste-specific entries. |
| `CLAUDE.md` | The `apps/` paragraph loses "loopback-only". |
| `2026-09-03-credential-acquisition-website-design.md` | Amend the disproven assumption: the app is hosted again, by a different mechanism. Do not rewrite the record. |
| `2026-09-02-browser-agnostic-steam-login-design.md` | Note that the loopback-only answer stands and is now worked around rather than accepted. |

CD is unchanged; the image already ships.

## Out of scope

- Any change to `RustPlusApi.Fcm.Registration` or any other package. `BuildLoginUrl` and
  `ParseCallback` are already public and already do what this needs.
- Accounts, or retrieving a past credential.
- Operating the public instance: hosting, DNS and TLS live outside this repo.

## Later

A browser extension that defines `ReactNativeWebView.postMessage` on
`companion-rust.facepunch.com` and delivers `{ SteamId, Token }` to an allowlisted return URL would
remove the paste step and, more importantly, survive Facepunch retiring the loopback branch, because
it uses the bridge their own app depends on. Precedent exists: the rustplus.py Link Companion has
been listed since 2022, and `Endilis/websiterust` carries a readable Manifest V3 implementation.
That is a separate spec, on the same backend: the extension would post to the same
`POST /api/callback`.

## Assumptions to re-verify

- **The probe in *Verify this first*.** Load-bearing, and unproven today.
- **A browser leaves the failed URL in the address bar.** Universal for connection refused, but
  confirm on the mobile browsers the public instance will actually see, where a failed navigation
  can turn into a search.
- **The loopback branch survives at all.** Unchanged from #126: if Facepunch retires it, this design
  and the console app fail together, and the extension becomes the only route.

## Success criteria

1. A visitor with no .NET and no Docker completes the flow at a public URL and copies a working
   `rustplus.config.json`.
2. The same app run locally still completes with the automatic redirect and no configuration.
3. The pairing wait is offered locally, refused on the public instance, and re-enabled by
   `AllowRemotePairing`.
4. A malformed paste can be corrected and retried without repeating the Steam login.
5. The secrets test passes with the paste path exercised.
6. `dtk dotnet build` is clean under `TreatWarningsAsErrors`, `dotnet jb cleanupcode` produces no diff,
   and `tools/coverage/report.sh` passes both gates.
