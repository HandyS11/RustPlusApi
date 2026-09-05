# Rust+ credentials website

A single-page web app that walks a visitor from nothing to working Rust+ credentials. It is both a
self-host starter and the code behind the public instance.

## Run it

```bash
docker run -p 8080:8080 \
  -e CredentialsWeb__PublicBaseUrl=https://creds.example.org \
  ghcr.io/handys11/rustplusapi-credentials
```

`PublicBaseUrl` is **required** and must be the externally reachable origin, with no trailing
slash. It cannot be inferred: it is the URL Facepunch redirects the browser back to, and behind a
reverse proxy that is not what Kestrel sees. Startup validates it before the host is built —
if it is missing, not an absolute URL, has a trailing slash, or is not `https`, the process prints
`Configuration error: ...` to stderr and exits with code 1 rather than starting. The one exception
is scheme: set `CredentialsWeb__AllowInsecureBaseUrl=true` to permit a non-`https` base URL, for
local development only.

For local development without a container:

```bash
dotnet run --project apps/RustPlusApi.CredentialsWeb \
  -- --CredentialsWeb:PublicBaseUrl=http://localhost:5000 --CredentialsWeb:AllowInsecureBaseUrl=true
```

## Three things that will bite you behind a reverse proxy

**Your access log will record Steam auth tokens.** Facepunch appends the token to the callback URL
as a query parameter (`?steamId=...&token=...`). The app itself never logs it — the ASP.NET Core
hosting request logger is switched off outright for exactly this reason, and
`SecretsAreNeverLoggedTests` asserts it, not just intends it — but your reverse proxy's default
access-log format records the full request line regardless. Filter the query string before it
reaches disk; `Caddyfile.example` in this directory shows how (it drops `steamId` and `token` from
the logged URI).

**Your access log will also record the session handle.** The post-login redirect carries the
session id in a URL fragment (`/#session=...`), which keeps it out of that one callback log line
and out of `Referer` headers — but the browser immediately opens
`GET /api/sessions/{sessionId}/events` to attach the event stream, and *that* request puts the
handle straight in the path, where a default access log records it like any other URL. The handle
is the sole authenticator for a stream that replays the full credentials payload once they're
acquired, so treat it as sensitive too. `Caddyfile.example` redacts the session path alongside
`steamId` and `token`.

**Your per-IP limits will silently do nothing.** Without the proxy's own address listed in
`CredentialsWeb__KnownProxies__0` (and `__1`, `__2`, ... for more than one), every visitor presents
as the proxy and shares one bucket — the caps in the table below stop meaning anything per visitor.
Configured too loosely — trusting an address you don't control — the same header lets a caller
spoof `X-Forwarded-For` and step past the limits entirely. With `KnownProxies` left empty (the
default), forwarded headers are ignored outright, which is the right behavior when the app is
reached directly. See `docker-compose.yml` for a worked example naming the proxy's address on the
container network.

> **Test coverage caveat.** The test suite proves that *configuring* `KnownProxies` turns
> forwarding on (a request bearing `X-Forwarded-For` is honored once a proxy is named) and that
> leaving it empty keeps forwarding off. It cannot exercise ASP.NET Core's own proxy-address
> matching — the check that a forwarded header is only trusted when it actually arrived from one of
> the named addresses — because the in-process `TestServer` used by those tests leaves
> `HttpContext.Connection.RemoteIpAddress` null, so that check never runs in the test. Whether a
> real deployment correctly rejects a forwarded header from an untrusted peer relies on Kestrel's
> and `ForwardedHeadersMiddleware`'s own behavior with a real remote address, not on anything this
> repository's tests confirm.

## Configuration

All settings live under the `CredentialsWeb` section (`CredentialsWeb__Name` as an environment
variable, `--CredentialsWeb:Name` on the command line).

| Setting | Default | What it does |
|---|---|---|
| `PublicBaseUrl` | *(required)* | Externally reachable origin, no trailing slash |
| `AllowInsecureBaseUrl` | `false` | Permits a non-https base URL. Development only |
| `KnownProxies__0`, `__1`, ... | *(empty)* | Reverse proxy addresses whose `X-Forwarded-For` is trusted |
| `MaxConcurrentSessions` | `200` | Global cap on live sessions, in any state |
| `MaxConcurrentPairings` | `50` | Global cap on live MCS sockets held open waiting for a pairing |
| `MaxCompletionsPerIpPerHour` | `5` | Rolling per-IP cap on completed flows |
| `CreatedTtl` | `00:05:00` | Lifetime of a session before the Steam login completes |
| `SessionTtl` | `00:15:00` | Lifetime of a session once the Steam login has completed |
| `PairingTtl` | `00:10:00` | Maximum time an MCS socket is held waiting for a pairing push |

## What it does with credentials

- **In memory only.** No database, no session cache, no disk. A restart wipes everything.
- **The Steam auth token is dropped** the moment your device is registered with Rust Companion.
- **No credential is logged.** Session ids, step names and exception text do appear in the app's
  own logs — but never a Steam token, FCM/GCM/Expo credential or `playerToken`. Asserted by
  `SecretsAreNeverLoggedTests`, not just intended.
- **The callback responds 302**, so the token-bearing URL never becomes a browser history entry,
  and the session handle travels in a URL fragment, which browsers never send to a server.
- **The server does see the token** in the request line. Facepunch decides the callback shape and
  nothing can change that; the design minimises its lifetime rather than pretending otherwise.

## Flow

The steps run in the order `4 → 1,2,3 → 5`, not the console app's `1,2,3 → 4 → 5`. Putting the
Steam login first means an anonymous visitor cannot trigger Google device registrations: they cost
one dictionary entry with a five-minute TTL and nothing else. Step 6, the pairing wait, is an
opt-in continuation because it is the only step that holds a socket open.
