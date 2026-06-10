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
    participant St as Steam (via Chrome)
    participant FP as Facepunch (Rust Companion)
    participant Game as Rust (in game)

    App->>G: 1. GCM check-in
    G-->>App: androidId + securityToken
    App->>G: 2-3. Firebase install + FCM register
    G-->>App: FCM token
    App->>E: 4. Expo push token
    E-->>App: ExponentPushToken[...]
    App->>St: 5. Interactive Steam login (Chrome DevTools)
    St-->>App: Steam auth token
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

// Steps 5–6: interactive Steam login (launches Chrome) + Rust Companion device registration.
await registration.RegisterWithRustPlusAsync(credentials);

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
| 5 | `SteamLoginService.LoginAsync` | Steam auth token |
| 6 | `RustCompanionClient.RegisterAsync` | device subscribed to pairing pushes |
| 7 | `CredentialsStore.Save` | `rustplus.config.json` |
| 8 | `PairingListener` | `ServerPairing` (ip/port/playerId/playerToken) |

Steps 1–7 run once. Step 8 happens every time you pair a new server in game.

## Steam login requires Chrome/Chromium

The Facepunch login page hands the auth token to its host via `ReactNativeWebView.postMessage`,
which can only be intercepted in a Chromium browser driven through the Chrome DevTools Protocol
(CDP). `SteamLoginService` injects a shim via `Page.addScriptToEvaluateOnNewDocument` — the same
mechanism Puppeteer and Playwright use — so the shim runs before any page script and sidesteps the
cross-origin `WindowProxy` restrictions that blocked older techniques. **Firefox and Safari will
not work.**

### Browser discovery order

`SteamLoginService.FindChrome()` and `SteamLoginService.ResolveChromeLaunch()` locate the browser
in this exact order (source: `src/RustPlusApi.Fcm.Registration/Steps/SteamLoginService.cs`):

1. **`CHROME_PATH` env var** — if set and the path exists as a file, it is used immediately,
   bypassing all other discovery.
2. **Native binary — Windows** (checked via `RuntimeInformation.IsOSPlatform`): looks for the
   following paths in order:
   - `C:\Program Files\Google\Chrome\Application\chrome.exe`
   - `C:\Program Files (x86)\Google\Chrome\Application\chrome.exe`
   - `C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe`
3. **Native binary — macOS**: checks in order:
   - `/Applications/Google Chrome.app/Contents/MacOS/Google Chrome`
   - `/Applications/Chromium.app/Contents/MacOS/Chromium`
4. **Native binary — Linux (and other platforms)**: walks `PATH` for the first match among these
   names, in order: `google-chrome`, `google-chrome-stable`, `chromium`, `chromium-browser`,
   `microsoft-edge`.
5. **Flatpak** — if no native binary was found and `flatpak` is on `PATH`, checks for installed
   Flatpak apps in this order: `com.google.Chrome`, `org.chromium.Chromium`,
   `com.github.Eloston.UngoogledChromium`. Presence is determined by checking whether the app
   directory exists under `/var/lib/flatpak/app/<id>` (system-wide) or
   `~/.local/share/flatpak/app/<id>` (user install). When a Flatpak app is found, Chrome is
   launched as `flatpak run --filesystem=<workDir> <appId>` with a temporary profile directory
   passed in so Chrome can write its data.

If no browser is found at all, `LaunchChrome` throws `InvalidOperationException` with a message
that includes instructions to install Chrome/Chromium or set `CHROME_PATH`.

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
