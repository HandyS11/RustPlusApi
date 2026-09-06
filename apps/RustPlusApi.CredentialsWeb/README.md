# Rust+ credentials website

A single-page web app that walks you from nothing to working Rust+ credentials.

**It runs on your own machine, and only there.** The browser you log in with must be on the same
machine as the container. There is no public instance, and one cannot be built — see
[Why this is loopback-only](#why-this-is-loopback-only).

## Run it

```bash
docker run -p 127.0.0.1:8080:8080 \
  -e CredentialsWeb__PublicBaseUrl=http://localhost:8080 \
  -e CredentialsWeb__AllowInsecureBaseUrl=true \
  ghcr.io/handys11/rustplusapi-credentials
```

Then open <http://localhost:8080> in a browser **on that same machine**.

`PublicBaseUrl` is required and should be the loopback origin you will actually open, with no
trailing slash — it is the URL Facepunch redirects the browser back to, and it must match the port
you published. `AllowInsecureBaseUrl=true` is needed because startup otherwise insists on `https`,
which made sense when this app was meant to be hosted; on loopback, plain `http` is the normal
case.

Startup checks that `PublicBaseUrl` is present, absolute, free of a trailing slash and — unless
`AllowInsecureBaseUrl` is set — `https`, printing `Configuration error: ...` to stderr and exiting
with code 1 rather than starting misconfigured. It does **not** check that the origin is loopback:
a routable value starts cleanly and then fails at the Steam step, which is what
[#126](https://github.com/HandyS11/RustPlusApi/issues/126) reports. Failing fast on that is a
sensible follow-up; today it is on you to get it right.

For local development without a container:

```bash
dotnet run --project apps/RustPlusApi.CredentialsWeb \
  -- --CredentialsWeb:PublicBaseUrl=http://localhost:5000 --CredentialsWeb:AllowInsecureBaseUrl=true
```

## Why this is loopback-only

Facepunch has moved the Rust+ login off the redirect. `/signin-steam` appears to branch on the
shape of the `returnUrl` it was given:

- a **loopback** `returnUrl` gets the legacy `302` redirect, with `steamId` and `token` appended;
- **anything else** gets a `window.ReactNativeWebView.postMessage(...)` call instead — the bridge
  that exists because the Rust+ companion app loads this page in a React Native WebView. In an
  ordinary browser that object is `undefined`, and the visitor sees *"Failed to send login message
  to the Rust+ app."* The callback is never requested, so the app never learns anything happened.

A LAN address with no TLS and no proxy in the path fails identically to a public HTTPS host, which
rules out the transport chain as the cause. Reachability is not the trigger either: Facepunch's
servers cannot reach your `localhost` any more than they can reach an unroutable LAN IP, yet
loopback works and the LAN IP does not.

This means a hosted instance cannot work. Neither branch is reachable from one: the redirect branch
needs a loopback `returnUrl` that a hosted site cannot own, and the postMessage branch needs script
injection into Facepunch's own window, which only something that *launches* the browser can arrange.

Consequently there is no reverse-proxy setup for this app. A proxy can serve the page, but the Steam
step will fail regardless of how the proxy is configured, so none is documented.

> **Not proven.** Facepunch's source has not been read. The branch is inferred from the behaviour
> above, from the error string matching the missing bridge exactly, and from `liamcottle/rustplus.js`
> documenting the same change. `/signin-steam` returns a bodyless 500 to a probe without valid
> OpenID parameters, so its own error page cannot be read. See
> [#126](https://github.com/HandyS11/RustPlusApi/issues/126) for the full reproduction.
>
> **This is a legacy path.** The loopback redirect is the branch Facepunch no longer uses for its
> own app. If it is retired, this app and `SteamLoginService` — and so the console sample — stop
> working together, and injection becomes the only remaining route.

## Configuration

All settings live under the `CredentialsWeb` section (`CredentialsWeb__Name` as an environment
variable, `--CredentialsWeb:Name` on the command line).

| Setting | Default | What it does |
|---|---|---|
| `PublicBaseUrl` | *(required)* | The loopback origin you open in the browser, no trailing slash |
| `AllowInsecureBaseUrl` | `false` | Permits a non-https base URL. Needed for the usual `http://localhost` setup |
| `KnownProxies__0`, `__1`, ... | *(empty)* | Reverse proxy addresses whose `X-Forwarded-For` is trusted |
| `MaxConcurrentSessions` | `200` | Global cap on live sessions, in any state |
| `MaxConcurrentPairings` | `50` | Global cap on live MCS sockets held open waiting for a pairing |
| `MaxCompletionsPerIpPerHour` | `5` | Rolling per-IP cap on completed flows |
| `CreatedTtl` | `00:05:00` | Lifetime of a session before the Steam login completes |
| `SessionTtl` | `00:15:00` | Lifetime of a session once the Steam login has completed |
| `PairingTtl` | `00:10:00` | Maximum time an MCS socket is held waiting for a pairing push |

The caps and `KnownProxies` are inherited from the abandoned hosted design and are still enforced,
but on a single-user loopback instance they will not be reached in normal use. They are documented
because they exist and can still refuse a request, not because they need tuning.

## What it does with credentials

The server here is your own machine, but the guarantees are worth stating because the Steam auth
token really does pass through the process:

- **In memory only.** No database, no session cache, no disk. A restart wipes everything.
- **The Steam auth token is dropped** the moment your device is registered with Rust Companion.
- **No credential is logged.** Session ids, step names and exception text do appear in the app's
  own logs — but never a Steam token, FCM/GCM/Expo credential or `playerToken`. Asserted by
  `SecretsAreNeverLoggedTests`, not just intended.
- **The callback responds 302**, so the token-bearing URL never becomes a browser history entry,
  and the session handle travels in a URL fragment, which browsers never send to a server.
- **The token does appear in the request line** on the way in. Facepunch decides the callback shape
  and nothing can change that; the design minimises its lifetime rather than pretending otherwise.
  If you put anything in front of this app that keeps an access log, that log will record the token
  and the session id.

## Flow

The steps run in the order `4 → 1,2,3 → 5`, not the console app's `1,2,3 → 4 → 5`. Putting the
Steam login first means a page load cannot trigger Google device registrations: they cost one
dictionary entry with a five-minute TTL and nothing else. Step 6, the pairing wait, is an opt-in
continuation because it is the only step that holds a socket open.
