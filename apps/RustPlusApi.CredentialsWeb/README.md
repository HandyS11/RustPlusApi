# Rust+ credentials website

A single-page web app that walks you from nothing to working Rust+ credentials — no .NET SDK
required.

**There is no public instance yet.** The app is built to be one — see
[Two modes, chosen automatically](#two-modes-chosen-automatically) — and when the maintainer stands
one up, its address will be filled in here. Until then, the route that works is running it yourself,
which is one command.

## Run it yourself

```bash
docker run -p 127.0.0.1:8080:8080 ghcr.io/handys11/rustplusapi-credentials
```

No environment variables needed — the app has no required setting. Open <http://localhost:8080> in
a browser **on that same machine**, and Steam signs you in with the ordinary automatic redirect: to
your browser and the container, you both look loopback to each other. That is the shortest version
of this flow, and the one to prefer if it is available to you.

For local development without a container:

```bash
dotnet run --project apps/RustPlusApi.CredentialsWeb
```

## Two modes, chosen automatically

The app decides how to handle each request as it arrives — nothing is configured. A request is
**local** when both hold: the connection came from a loopback address, and the `Host` header names
a loopback host (`localhost`, any `*.localhost`, or a loopback IP literal). That is exactly what
browsing to `http://localhost:8080` from the machine running the container looks like. Anything
else — a LAN address, a hostname behind a reverse proxy, a hosted instance — is **remote**.

- **Local** keeps today's redirect. Facepunch sends the browser straight back to this app; there is
  no extra step.
- **Remote** gets the paste flow, because of how Facepunch's `/signin-steam` decides where it is
  willing to send the browser back to: purely from the *shape* of the return address, not whether
  anything can actually reach it. A loopback address always qualifies — Facepunch's own servers
  cannot reach your `localhost` any more than they can reach an unroutable LAN address, yet loopback
  still works. So a remote visitor is handed a loopback address that nothing is listening on. Their
  browser fails to connect and shows that dead address — Steam token and all — right in the address
  bar. They copy it and paste it back into the page they started from, which picks up from there
  exactly as if the redirect had landed on its own. **This was verified against the live Facepunch
  endpoint on 2026-09-06, from a non-loopback origin, and it worked.**

## Self-hosting beyond loopback

Running this for more than yourself — on a LAN address, or behind a reverse proxy — puts every
visitor into remote/paste mode. Three things follow.

### The pairing wait is local-only by default

It is the one step that holds a socket open to Google per visitor — the one genuinely scarce
resource here — so it is offered to local visitors only unless you opt in. Self-hosting for yourself
on a LAN address? Set `CredentialsWeb__AllowRemotePairing=true`.

### Behind a reverse proxy, set `KnownProxies` — the caps do not work without it

`Caddyfile.example` in this directory is a worked configuration, and `docker-compose.yml` has the
matching compose wiring. The proxy must overwrite `X-Forwarded-For` rather than append to it, so a
visitor can't spoof their way past the per-IP caps, and it has to be paired with
`CredentialsWeb__KnownProxies__0` set to the proxy's own address on the app's network.

Leaving `KnownProxies` empty behind a proxy does **not** make the caps lax. It makes the instance
effectively single-user, because every visitor is then accounted as the proxy and they all share one
per-IP bucket:

- `MaxCompletionsPerIpPerHour` (5) becomes a **global** ceiling. After the fifth completed flow the
  instance stops serving anybody at all for an hour.
- The one-session-per-address rule becomes a **global** one-visitor-at-a-time lock, and the 429 it
  answers with tells a total stranger "You already have a session in progress. Reopen that tab",
  which is nonsense for them.
- Worst: sessions get **evicted**. A session still in `Created` is treated as abandoned and cleared
  out when the "same address" starts a new one — which is right per visitor and catastrophic when
  every visitor is the same address. Visitor B merely loading the page invalidates visitor A's
  return token while A is still on Steam's login page. A then pastes a perfectly good address and is
  told it was already used.

The app cannot detect this for you — a loopback-published local instance and a proxied hosted one
look identical from the inside — so it logs an informational line at startup naming the setting,
and the rest is the operator's call.

### Behind a reverse proxy, reject foreign `Host` values too

The local/remote decision is `(loopback connection) AND (loopback Host)`. With a proxy on the same
host as the app, the first half is true for *every* request, so the `Host` header is all that is
left — and the caller writes that header. A remote caller sending `Host: localhost` reads as local
and takes the local behaviour, including the pairing wait `AllowRemotePairing` is meant to gate. No
credential is disclosed; it is a control bypass, bounded by `MaxConcurrentPairings`.

Close it at the deployment, because the app cannot close it in code:

- A **named site block** at the proxy — `creds.example.org { ... }`, as in `Caddyfile.example` —
  routes a request only when its `Host` matches, and answers nothing otherwise. That is enough.
- A **catch-all** block that passes the client's `Host` straight through is not. If you use one,
  filter `Host` at the proxy yourself, or set the `AllowedHosts` environment variable on the app —
  ASP.NET Core's own host filtering, which sits at its `*` default here because the app ships no
  `appsettings.json`.

## Configuration

All settings live under the `CredentialsWeb` section (`CredentialsWeb__Name` as an environment
variable, `--CredentialsWeb:Name` on the command line).

| Setting | Default | What it does |
|---|---|---|
| `AllowRemotePairing` | `false` | The LAN self-host switch: offers the pairing wait to non-local visitors too. Turn it on when self-hosting on an address that is yours but not loopback, for visitors you trust. |
| `KnownProxies__0`, `__1`, ... | *(empty)* | Reverse proxy addresses whose `X-Forwarded-For` is trusted. Required behind a proxy — see above for what happens without it |
| `MaxConcurrentSessions` | `200` | Global cap on live sessions, in any state |
| `MaxConcurrentPairings` | `50` | Global cap on live MCS sockets held open waiting for a pairing |
| `MaxCompletionsPerIpPerHour` | `5` | Rolling per-IP cap on completed flows |
| `CreatedTtl` | `00:10:00` | Lifetime of a session before the Steam login completes. Ten minutes covers a real Steam login, two-factor included, plus a copy and a paste. |
| `SessionTtl` | `00:15:00` | Lifetime of a session once the Steam login has completed |
| `PairingTtl` | `00:10:00` | Maximum time an MCS socket is held waiting for a pairing push |

`PublicBaseUrl` and `AllowInsecureBaseUrl` are gone — the return URL now comes from the request
itself (or is a fixed loopback string on a remote request), so there is nothing left for either to
configure. An old deployment that still sets one logs a startup warning naming it and keeps running;
nothing breaks, but they can be removed.

## What it does with credentials

The Steam auth token really does pass through this server on its way to becoming your credentials,
so the guarantees below are worth stating plainly:

- **In memory only.** No database, no session cache, no disk. A restart wipes everything.
- **The Steam auth token is dropped** the moment your device is registered with Rust Companion.
- **No credential is logged.** Session ids, step names and exception text do appear in the app's
  own logs — but never a Steam token, FCM/GCM/Expo credential or `playerToken`. Asserted by
  `SecretsAreNeverLoggedTests`, not just intended.
- **The callback responds 302**, so on a *local* visit the token-bearing URL never becomes a browser
  history entry, and the session handle travels in a URL fragment, which browsers never send to a
  server.
- **On a remote visitor, the Steam token arrives in a request body, not a request line.** The paste
  is a `POST` with the address in a JSON body, so — unlike the hosted design this app originally
  carried in its own codebase, which was never actually deployed anywhere — the token never appears
  in any URL and so never reaches an access log. What is still worth redacting is the session handle
  in the path of `GET /api/sessions/{id}/events`; see `Caddyfile.example`.

Three things worth knowing if you're pasting an address into this page, or anyone else's:

- **Your clipboard manager may remember it.** The token is in whatever you copied, and a clipboard
  history tool keeps what you copy regardless of what this page does with it afterwards.
- **A paste box is a social-engineering surface.** Nothing stops another site from copying this
  page's flow and asking you to paste the same address into it. That's not a new capability — anyone
  can already clone and host this app — but "paste this address here" normalises the behaviour, so
  check you're actually on the site you meant to be on before you paste. The page names the origin
  you should be on, immediately above the box.
- **The paste flow puts the token in your browser history.** This is the one place the paste is
  *worse* than the redirect, and it is worth stating in the same breath as the `302` above. In paste
  mode your browser genuinely navigates to the token-bearing loopback URL, fails to connect, and
  leaves that URL in the failed tab's address bar and in its session history — and on a signed-in,
  syncing profile, potentially in your browser vendor's cloud. Nothing in this app can prevent it:
  the navigation happens in your browser, to an address no server here ever receives. The page tells
  you to close that tab once the paste has landed, which is the whole mitigation.

## Flow

The steps run in the order `4 → 1,2,3 → 5`, not the console app's `1,2,3 → 4 → 5`. Putting the
Steam login first means a page load cannot trigger Google device registrations: they cost one
dictionary entry with a ten-minute TTL and nothing else. Step 6, the pairing wait, is an opt-in
continuation because it is the only step that holds a socket open.
