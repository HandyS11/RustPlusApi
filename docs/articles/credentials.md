# Credentials

To use Rust+ you need credentials. There are two kinds:

- **FCM credentials** — identify your "device" to Google Firebase Cloud Messaging so you can
  receive Rust+ push notifications (used by `RustPlusFcm`).
- **Server pairing values** — `ip`, `port`, `playerId`, `playerToken` for a specific server, sent
  to you as a push notification when you choose *Pair with Server* in game (used by `RustPlus`).

The `RustPlusApi.Fcm.Registration` package acquires both natively, replacing the
`rustplus.js` Node CLI.

## The flow

```mermaid
sequenceDiagram
    participant App as Your app
    participant G as Google (GCM/Firebase/FCM)
    participant E as Expo
    participant St as Steam (your browser)
    participant FP as Facepunch (Rust Companion)
    participant Game as Rust (in game)

    App->>G: 1. GCM check-in
    G-->>App: androidId + securityToken
    App->>G: 2-3. Firebase install + FCM register
    G-->>App: FCM token
    App->>E: 4. Expo push token
    E-->>App: ExponentPushToken[...]
    App->>St: 5. Interactive Steam login (browser redirect)
    St-->>App: Steam auth token + Steam64 id
    App->>FP: 6. Register device with Rust Companion
    FP-->>App: subscribed to pairing pushes
    Note over App: 7. CredentialsStore.Save("rustplus.config.json")
    Game->>FP: 8. "Pair with Server" in game
    FP->>G: push notification
    G-->>App: ServerPairing (ip/port/playerId/playerToken)
```

`FcmRegistration` orchestrates the whole chain:

```csharp
using RustPlusApi.Fcm.Registration;

var registration = new FcmRegistration();

// Steps 1–4: GCM check-in → Firebase install → FCM register → Expo token.
var credentials = await registration.AcquireCredentialsAsync();

// Steps 5–6: interactive Steam login (opens your browser) + Rust Companion device registration.
var steamLogin = await registration.RegisterWithRustPlusAsync(
    credentials,
    onLoginUrl: url => Console.WriteLine($"Open this URL to log in: {url}"));

// Step 7: persist for later runs.
CredentialsStore.Save("rustplus.config.json", credentials);

// Step 8: pair in game; one await yields the RustPlus constructor args.
using var listener = new PairingListener(credentials);
ServerPairing pairing = await listener.WaitForServerPairingAsync();
// new RustPlus(new RustPlusConnection(pairing.Ip, pairing.Port, pairing.PlayerId, pairing.PlayerToken))
```

| Step | Component | Result |
| --- | --- | --- |
| 1 | `AndroidFcmRegister.CheckInAsync` | Android id + security token |
| 2 | `AndroidFcmRegister.InstallAsync` | Firebase installation token |
| 3 | `AndroidFcmRegister.RegisterFcmAsync` | FCM token |
| 4 | `ExpoPushClient.GetTokenAsync` | Expo push token |
| 5 | `SteamLoginService.LoginAsync` | Steam auth token + Steam64 id |
| 6 | `RustCompanionClient.RegisterAsync` | device subscribed to pairing pushes |
| 7 | `CredentialsStore.Save` | `rustplus.config.json` |
| 8 | `PairingListener` | `ServerPairing` (ip/port/playerId/playerToken) |

Steps 1–7 run once. Step 8 happens every time you pair a new server in game.

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

## Upstream fragility

Every network step depends on live Google, Expo and Facepunch services, whose endpoints and
constants drift when those apps change. The flow is ported from rustplus.js /
`@liamcottle/push-receiver`; if registration breaks, re-check `RegistrationConstants` against
those upstream sources. The offline test suite covers the deterministic parts; the live flow is
validated by running the `RustPlus.Register.ConsoleApp` sample end to end.

## Loading credentials back

```csharp
var credentials = CredentialsStore.Load("rustplus.config.json");
var listener = new RustPlusFcm(credentials);
```

The legacy rustplus.js `rustplus.config.json` format is also accepted by the FCM sample's loader.

> [!WARNING]
> `rustplus.config.json` contains long-lived push credentials (the GCM security token and
> FCM/Expo tokens) in plain JSON — anyone who can read the file can receive your pairing
> notifications. Treat it like a password file: keep it out of version control and shared
> directories. On .NET 10+ on Linux/macOS, `CredentialsStore.Save` restricts it to owner
> read/write; on other targets, restrict permissions yourself.
