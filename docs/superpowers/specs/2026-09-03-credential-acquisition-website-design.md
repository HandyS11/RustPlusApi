# Credential-acquisition website

**Date:** 2026-09-03
**Status:** Implemented, and **partly invalidated on 2026-09-06** — see the box below. **The app is
hosted again as of the same day**, by a different mechanism than this document designed — see
`2026-09-06-out-of-band-steam-callback-design.md`.
**Scope:** A new `apps/RustPlusApi.CredentialsWeb` project — a single-page web app that acquires
Rust+ credentials. Two audiences: a self-host starter anyone can deploy, and a public instance.
Consumes `RustPlusApi.Fcm.Registration` as-is; **no library change is required**.

> **The public-instance half of this spec does not work and cannot be made to work.** Facepunch's
> `/signin-steam` only honours the `returnUrl` redirect for loopback addresses; anything else is
> handed to a `ReactNativeWebView` bridge that exists only inside the Rust+ app. The app is
> loopback-only. This document is kept as the dated record of what was designed and why, including
> the reasoning that turned out to be wrong — see *Verification log* and *Assumptions to re-verify*.
> Issue #126 has the reproduction.

## Problem

Acquiring Rust+ credentials today means cloning the repo, installing the .NET SDK and running
`samples/RustPlus.Register.ConsoleApp`. That is a large amount of friction for a user whose actual
goal is four values: `ip`, `port`, `playerId`, `playerToken`.

PR #118 removed the Chrome requirement and, deliberately, exposed the seam a website consumes:

```csharp
public static string BuildLoginUrl(string returnUrl);              // SteamLoginService
public static SteamLoginResult ParseCallback(Uri callbackUri);     // SteamLoginService
```

## Why this cannot be a static page

Of the six steps in the flow, only one runs in the browser:

| Step | Where it must run | Why |
|---|---|---|
| 1–3. GCM check-in → Firebase → FCM register → Expo token | Backend | protobuf POSTs to `android.clients.google.com`; no CORS |
| 4. Steam login | Browser redirect | Same mechanism local and hosted |
| 5. Rust Companion register | Backend | No CORS |
| 6. Wait for the in-game pairing push | Backend | Raw TLS socket to `mtalk.google.com:5228` |

Step 6 in particular means the backend holds a **live MCS connection per waiting visitor** while
they alt-tab into Rust and press "Pair with Server". That requires a real server→client push
channel, and on a public instance it is the scarce resource the whole abuse model is built around.

## Verification log (2026-09-03)

The hosted design depends on Facepunch honouring a `returnUrl` that is not `http://localhost:3000`.
The 2026-09-02 spec left this open. Three probes closed it:

1. **External `https` host reflected.** `GET https://companion-rust.facepunch.com/login?returnUrl=
   https%3A%2F%2Frustplus-creds.example.org%2Fcallback%2Fabc123XYZ` → `200`, with the value echoed
   verbatim into the hidden `returnUrl` input, host, scheme and path segment intact.
2. **External `https` host accepted at the OpenID kickoff.** `POST /login` with that `returnUrl` and
   the antiforgery token → `302` to `steamcommunity.com/openid/login`, with
   `openid.realm=https://companion-rust.facepunch.com` and
   `return_to=…/signin-steam?state=<encrypted>`. No validation error at either gate. The `returnUrl`
   travels inside the encrypted `state` blob exactly as it does for loopback.
3. **`https` scheme and a non-3000 port honoured end to end.** A throwaway Kestrel listener on
   `https://localhost:7443/callback/<128-bit nonce>` received the post-Steam redirect with
   `steamId` and a 193-character `token`, path nonce matching. Real Steam login, real credentials.

**Conclusion:** `https`, arbitrary ports and path nonces are proven. An external non-loopback
hostname is not conclusively proven end to end, but probes 1 and 2 show there is no gate left that
could plausibly enforce one — `/signin-steam` would have to deliberately re-validate a value it
receives encrypted. Re-confirm against the first staging deployment; see *Assumptions to re-verify*.

> **Superseded, 2026-09-06.** That conclusion was wrong, and the hedge in it was the load-bearing
> part: `/signin-steam` *does* branch on the `returnUrl`, and an external hostname does not complete
> the round trip. Probe 3 succeeded because `https://localhost:7443` is loopback, not because the
> scheme and port were the only variables left. See *Assumptions to re-verify* below and issue #126.

The 2026-09-02 spec's "Assumptions to re-verify" section is amended with these outcomes.

## Decisions

**In this repo, as a new top-level `apps/` project.** Not `samples/` — "sample" undersells something
hosted publicly and deployed by strangers, and `samples/` is outside the coverage and CD story. Not
a separate repo — colocation makes the app a canary that breaks loudly in CI when an upstream
Google/Facepunch step regresses, keeps one clone for self-hosters, and keeps the app version-locked
to the packages it demonstrates. Cost: the app is net10.0-only, so it sits outside the
netstandard2.0 multi-TFM parity story that governs `src/`.

**Minimal API + a hand-written static page + SSE.** The entire client is one readable file, which
matters disproportionately for a page that handles full Rust+ account access: a security-conscious
user can audit it. SSE is one-directional — exactly the shape of the problem, where the only
server→client event is "your pairing arrived" — survives proxies and reconnects for free.
Rejected: Blazor Server (heavy dependency for one page; circuit reconnection semantics complicate
the bounding story rather than simplify it; an opaque client defeats the audit argument) and
WebSockets (bidirectional capability that buys nothing here, at the cost of manual reconnect logic).

**Steps 1–5 always; step 6 as an opt-in continuation.** The default path ends at "you have
credentials and your device is registered", which touches no long-lived socket. The page then offers
"wait for my pairing" as a clearly-labelled next step. Framing it as a continuation rather than an
upfront fork matters: at the point of choosing, the user has already seen what they got and can
judge whether they need more.

**In-memory sessions, reconnectable, hard TTL.** Nothing touches disk or a database, so "never
persisted" is literally true and a process restart wipes everything by construction. A session id in
`sessionStorage` lets an SSE drop reconnect and resume the same wait — which matters because the
drop happens precisely when the user alt-tabs into fullscreen Rust. Rejected: dying with the SSE
stream (a blip costs a fresh Steam login) and a Redis/SQLite store (directly contradicts the promise
the page makes).

**Docker as the documented default, `dotnet run` for contributors.** Headline install is one
`docker run` against a multi-arch image published to `ghcr.io`.

**Steam-first ordering plus tiered caps; no captcha.** See below.

## Flow ordering

Step 4 has no dependency on steps 1–3; step 5 needs both the Steam token and the Expo token.
So `4 → 1,2,3 → 5` is a valid order, and the app uses it.

This is the load-bearing abuse decision. Under the console app's `1,2,3 → 4 → 5` ordering, an
anonymous visitor triggers real Google device registrations before proving they are anyone — the
cheapest thing for an attacker to burn would be the resource the project least controls. Under
Steam-first, an unauthenticated visitor costs one dictionary entry with a five-minute TTL and
nothing else.

The reordering lives in the app's own `CredentialFlow`. `FcmRegistration.RegisterWithRustPlusAsync`,
which bundles the interactive login with the Companion registration, is untouched and remains the
local/console path. The app calls the public step classes directly:
`SteamLoginService.BuildLoginUrl`/`ParseCallback`, `AndroidFcmRegister`, `ExpoPushClient`,
`RustCompanionClient`, `PairingListener`.

## Architecture

```
apps/RustPlusApi.CredentialsWeb/
    Program.cs                 minimal API wiring
    Sessions/                  Session, SessionStore, SessionOptions, sweeper
    Flow/                      CredentialFlow — orchestrates 4→1,2,3→5 and optional 6
    Upstream/                  adapter interface over the four live-network classes
    wwwroot/                   index.html, app.js, app.css — no build step, no npm
    Dockerfile
tests/RustPlusApi.CredentialsWeb.UnitTests/
```

`net10.0` only, `IsPackable=false`, `ProjectReference` to `RustPlusApi.Fcm.Registration`.

### Two ids per session

Both 128-bit, from `RandomNumberGenerator`.

- **`returnToken`** — appears only in the `returnUrl` path. Identifies which session an inbound
  Facepunch callback belongs to. **Single-use**: consumed and invalidated on first callback, so a
  callback URL replayed from browser history hits a dead route.
- **`sessionId`** — the handle the browser uses for the SSE stream and subsequent calls. Never
  appears in any `returnUrl`.

Separating them means the value that necessarily travels through Facepunch and lands in logs is not
the value that grants access to the session's events.

### Endpoints

| Route | Behaviour |
|---|---|
| `GET /` | The single page. Static. |
| `POST /api/sessions` | Creates an empty session. Returns `{ sessionId, loginUrl }` where `loginUrl = SteamLoginService.BuildLoginUrl($"{PublicBaseUrl}/callback/{returnToken}")`. Touches nothing upstream. |
| `GET /callback/{returnToken}` | Facepunch's redirect target. `SteamLoginService.ParseCallback` → stash in session → start `1,2,3` then `5` in the background → **302** to `/#session={sessionId}`. |
| `GET /api/sessions/{id}/events` | SSE. Emits `step`, `credentials`, `paired`, `error`, `expired`. Opened by the client immediately after `POST /api/sessions`, before the user leaves for Steam, so progress streams the moment the callback lands. |
| `POST /api/sessions/{id}/pairing` | Opt-in step 6. Opens the `PairingListener`. |

**The callback responds 302, not 200.** A redirect leaves no back-button history entry, so the
token-bearing URL never becomes one. This is strictly better than the library's local flow, which
lands on a 200 and scrubs the query afterwards with `history.replaceState`.

**The redirect target carries the session in a fragment (`/#session=…`).** Fragments are never sent
to the server, so the session handle stays out of the *callback* log line and out of `Referer`
headers. It does **not** stay out of access logs entirely: the client immediately opens
`GET /api/sessions/{sessionId}/events` to attach the SSE stream, which puts the handle in the
request path of every such request, and a default access-log format records that. The handle is
the sole authenticator for a stream that replays the full credentials payload, so anything keeping
an access log in front of this app must redact the session path too, not just `steamId` and `token`.
(`Caddyfile.example` showed how; it was removed on 2026-09-06 along with the rest of the
reverse-proxy guidance, since no proxy can front the Steam login.) It also makes the flow work when
the Steam login is completed in a
*different* browser from the one holding the SSE stream — a phone, say — mirroring the library's
headless story at no cost.

**No endpoint returns the credentials.** They arrive once over SSE; the page assembles
`rustplus.config.json` client-side as a `Blob` for download. One less route serving secrets.

### State machine

`Created` → `Authenticated` (Steam callback) → `Registering` (steps 1,2,3) → `Ready` (step 5 done;
credentials shown, download offered) → optionally `AwaitingPairing` → `Paired`. `Failed` is
reachable from anywhere.

There is deliberately **no terminal `Expired` state**. A pairing wait that times out returns the
session to `Ready` and emits an `expired` event — which is what makes the retry promised in the
error-handling table actually possible, since the pairing endpoint only accepts a `Ready` session.
A session that outlives its own TTL is removed by the sweeper rather than parked in a state nobody
can observe.

### What the page hands back

- On `Ready`: `rustplus.config.json` as a download, plus the values rendered on-page.
- On `Paired`: the four values (`ip`, `port`, `playerId`, `playerToken`) and a copyable
  `new RustPlus(new RustPlusConnection(...))` line, matching what the console sample prints.

## Trust and secret handling

The page transits the user's Steam auth token, their FCM credentials, and ultimately their
`playerToken` — which is full Rust+ account access. Facepunch's own login page carries an HTML
comment warning not to share it.

### Lifetimes

| Secret | Lives | Dropped |
|---|---|---|
| Steam auth token | Session object, in memory | Immediately after step 5 succeeds. Reference nulled; never retained for display. |
| FCM / GCM / Expo credentials | Session, in memory | Session disposal (TTL or completion) |
| `playerToken` | Session, in memory, only across the `Ready`→`Paired` window | Session disposal |

### Never logged

Request logging is suppressed for `/callback/{returnToken}` specifically. ASP.NET Core logs the path
**and query** at Information by default, which would write the Steam token straight into stdout.
This gets an explicit test (below), not just a code comment.

Also: `Referrer-Policy: no-referrer`; a CSP admitting no third-party origins; `Cache-Control:
no-store` on every dynamic response.

### Never persisted

No database, no session cache, no disk. The container runs with a read-only root filesystem, which
turns the promise into something the runtime enforces rather than something the code intends.

### The honest admission

The server **does** see the Steam token in the request line, because Facepunch decides the callback
shape and nothing in this design can change that. The tempting fix — respond 200 and have JavaScript
POST the token as a request body — buys nothing, because the GET already carried it, and costs the
302's history benefit. So the design mitigates rather than eliminates: minimum lifetime, no logging,
no persistence, no history entry. This is stated plainly on the page, not only in this document.

### The gap outside the app's control

A default nginx or Caddy access log records the full request line including the query. A
self-hoster who does everything else right can still end up with Steam tokens in `/var/log`. This
gets a prominent README section with a worked log-format snippet, and the public instance is
configured accordingly.

### Startup refuses to run insecurely

`PublicBaseUrl` must be `https://` or the app exits, with a single explicit
`--allow-insecure-base-url` escape hatch for localhost development. `PublicBaseUrl` is required
rather than inferred: behind a reverse proxy the externally-reachable address is not what Kestrel
sees, and `returnUrl` must be the external one.

## Bounding

All counters in memory, all knobs configurable, defaults tuned for self-host; the values below are
the public instance's.

| Knob | Public default | Rationale |
|---|---|---|
| Global concurrent sessions | 200 | Memory backstop |
| Global concurrent pairings (MCS sockets) | 50 | The genuinely scarce resource |
| Active sessions per IP | 1 | One human, one flow (see eviction rule below) |
| Completed flows per IP per hour | 5 | Bounds Google device registrations |
| TTL — `Created` (pre-Steam) | 5 min | Cheapest state to spam, shortest leash |
| TTL — authenticated session | 15 min | |
| TTL — pairing wait | 10 min | Bounds socket hold time |

A `PeriodicTimer` background service sweeps expired sessions and disposes their sockets. Over-cap
requests get a 429 whose body says "try again shortly — or run your own instance", with the
`docker run` line.

**Eviction, not rejection, for abandoned or dead sessions.** A one-session-per-IP cap would otherwise
lock a user out for minutes over an attempt that cannot be resumed — the single most likely thing a
confused first-time visitor runs into. So `POST /api/sessions` from an IP that already holds a
session in `Created` **or** `Failed` state **evicts** the old one and issues a new one: `Created`
never touched upstream, and `Failed` is terminal, so neither has anything left to resume. An IP
holding a session in any other state — one where real, still-live upstream work has been done — gets
the 429 instead; that session is resumable via its `sessionId`, so there is nothing to start over.

Captcha was rejected: a third-party script tag would undermine the single-auditable-file property
that the trust story leans on, and it adds a Cloudflare dependency plus a knob self-hosters must be
able to switch off. Steam-first ordering already makes the expensive resources cost a real Steam
account. Revisit if distributed abuse actually materialises.

**Proxy trap.** Per-IP limits are worthless behind a reverse proxy unless `ForwardedHeaders` is
configured with `KnownProxies` — otherwise every visitor presents as the proxy and shares one
bucket. Configured the other way, anyone can spoof `X-Forwarded-For` to dodge the limits. This must
be correct in the shipped compose file and called out in the README.

## Error handling

| Case | Behaviour |
|---|---|
| Callback `returnToken` unknown or already consumed | 404. No information about whether the token ever existed. |
| Callback missing or blank `token` | `ParseCallback` throws; session → `Failed`; the page shows "the Steam login didn't complete — start over". |
| Facepunch changes the callback contract | Same path as above; the `InvalidOperationException` message already points at `RegistrationConstants`. |
| Any of steps 1,2,3,5 fails upstream | Session → `Failed` with a step-named message over SSE. Credentials already acquired are dropped, not retried automatically. |
| Pairing push never arrives before TTL | Socket disposed, pairing slot released, session returns to `Ready` and an `expired` event is emitted. The page offers to retry the pairing step without redoing the Steam login. |
| SSE stream drops | Client reconnects with the same `sessionId` and resumes; server-side state and the MCS socket are untouched. |
| Global or hourly cap exceeded | 429 with the self-host pointer. |
| This address already holds a resumable session | 429 pointing back at that session instead — not the self-host pointer, since capacity was never the problem. |
| `PublicBaseUrl` missing or not `https` | Startup fails with a message naming the setting and the escape hatch. |

## Testing

**The coverage gate needs an adjustment.** `tools/coverage/report.sh` runs the whole solution and
gates the *merged* aggregate at line 95 / branch 90. Adding a web app would drag that number down
and quietly weaken the **library's** gate. Fix: filter the app assembly out of the merged report so
the library gate stays byte-for-byte what it is today, and add a second gate for the app in the same
script, held to the **same line 95 / branch 90**. The app can meet that bar because the two
untestable regions — `Program.cs` wiring and the upstream adapter — are `[ExcludeFromCodeCoverage]`
with justifications, leaving `SessionStore`, `CredentialFlow` and the endpoint handlers, all of
which are ordinary unit-testable code. This matches the repo's existing posture: everything is
expected at 100/100 minus a justified exclusion list.

**Testability seam.** The app defines a thin adapter interface over the four live-network classes
and `CredentialFlow` depends on that, so flow tests never touch Google or Facepunch. The adapter
implementation is `[ExcludeFromCodeCoverage]` with per-member justifications, following the
convention already documented in `docs/development/testing.md`. No interfaces are added to the
shipped library.

Covered:

- `SessionStore` — TTL expiry per state, sweeper disposal, single-use `returnToken`, replay
  rejection, every cap, per-IP accounting
- Callback — unknown token → 404, missing `token` → `Failed`, correct 302 target, replay → 404
- Startup validation — non-`https` `PublicBaseUrl` refuses to boot; the escape hatch works
- SSE — event sequence per state transition; reconnect resumes the same session
- `CredentialFlow` — the `4→1,2,3→5` ordering, and that a failure at each step lands in `Failed`

**The promise test.** Drive a full callback through `WebApplicationFactory` with a capturing
`ILoggerProvider`, and assert the Steam token and the `playerToken` appear in **no** log record.
This is the test that defends the trust section rather than merely documenting it.

The app is net10.0-only, so its test project targets `net10.0` alone — the multi-TFM parity
requirement applies to `src/` and does not extend here.

## Packaging and CD

- Multi-stage `Dockerfile`, non-root user, read-only root filesystem.
- `CI.yml` builds and tests the new project.
- `CD.yml` gains a job publishing a multi-arch (amd64/arm64) image to `ghcr.io` on release, tagged
  with the release tag and `latest`.
- A minimal `docker-compose.yml` and a worked Caddy snippet ship as **copyable documentation**, not
  as a supported second install path. Rationale: the two worst self-host footguns — a default access
  log that leaks Steam tokens, and `ForwardedHeaders` misconfiguration that silently voids per-IP
  limits — are both configuration mistakes that a paragraph of prose will not reliably prevent.

## Documentation fallout

| File | Change |
|---|---|
| `apps/RustPlusApi.CredentialsWeb/README.md` | New. Self-host guide: `docker run` one-liner, `PublicBaseUrl`, the proxy log-format snippet, the `ForwardedHeaders` warning, every config knob. |
| `docs/articles/credentials.md` | Add the website as the recommended route; keep the console app as the local one. |
| `docs/articles/troubleshooting.md` | New section: callback 404s, 429s, the pairing push not arriving. |
| `docs/articles/getting-started.md` | Point at the website first. |
| `README.md` (root) | Mention the public instance and the self-host image. |
| `CLAUDE.md` | Add the `apps/` layer to the architecture description, and note it is outside the multi-TFM parity story. |
| `docs/development/testing.md` | Document the app's coverage gate and its exclusion justifications. |
| `docs/superpowers/specs/2026-09-02-browser-agnostic-steam-login-design.md` | Amend "Assumptions to re-verify" with the 2026-09-03 verification log above. |

## Out of scope

- Accounts, logins or any notion of a returning user.
- Any UI for managing or retrieving past credentials.
- Storing a credential server-side beyond the session TTL.
- Changes to `RustPlusApi.Fcm.Registration` or any other shipped package.
- The Rust+ client functionality itself — the site stops at the four pairing values.

## Assumptions to re-verify

- ~~**An external non-loopback `https` hostname completes the round trip.**~~ **Disproven,
  2026-09-06 (issue #126).** It does not. `/signin-steam` branches: a loopback `returnUrl` gets the
  legacy 302, anything else gets `window.ReactNativeWebView.postMessage`, which is `undefined` in an
  ordinary browser — the visitor sees "Failed to send login message to the Rust+ app" and the
  callback is never requested. A plain-HTTP LAN address with no proxy and no TLS fails identically
  to a public HTTPS host, which rules out the transport chain. The documented fallback is the
  outcome: the app is loopback-only, the public instance is the casualty, and the hosted framing
  has been removed from the README, `docker-compose.yml` and the user-facing docs.

  Probes 1 and 2 were correct and remain so — re-run on 2026-09-06, `/login` still reflects and
  accepts *any* `returnUrl`, including `javascript:`. The conclusion drawn from them ("there is no
  gate left that could plausibly enforce one") was the error: absence of a gate at `/login` did not
  imply absence of one at `/signin-steam`, which is unreadable from outside (bodyless 500 without
  valid OpenID parameters). Facepunch's source has still not been read; the branch is inferred.

  **Further update, same day.** The public instance is possible again, by a route this assumption
  never considered: `2026-09-06-out-of-band-steam-callback-design.md` doesn't need Facepunch to
  redirect anywhere externally reachable at all. It hands the browser a *loopback* `returnUrl` that
  simply has nothing listening on it, and has the visitor bring the resulting dead address back by
  hand. The hosted framing this document's fallout had removed from the README,
  `docker-compose.yml` and the user-facing docs has since been restored, describing that mechanism
  instead of the one this document designed.
- **`PairingListener` reliably surfaces the first pairing push after a fresh registration.** A prior
  investigation found the first push could be missed; four fixes landed. The website makes this
  user-visible in a way the console app did not — a missed first push reads as "the site is broken".
  Validate during implementation with a real pairing.

## Success criteria

1. A visitor with no .NET installed completes the flow in a browser and downloads a working
   `rustplus.config.json`.
2. The same visitor opts into the pairing step, pairs in game, and gets four values that
   successfully construct a working `RustPlus` connection.
3. The promise test passes: no Steam token and no `playerToken` appears in any log record.
4. `docker run -p 8080:8080 ghcr.io/handys11/rustplusapi-credentials` serves the app on a machine
   with no .NET SDK.
5. Startup refuses a non-`https` `PublicBaseUrl` without the escape hatch.
6. Every cap in the bounding table is enforced and covered by a test.
7. `dtk dotnet build` is clean under `TreatWarningsAsErrors`; `dotnet jb cleanupcode` produces no diff;
   `tools/coverage/report.sh` passes both the unchanged library gate and the new app gate.
