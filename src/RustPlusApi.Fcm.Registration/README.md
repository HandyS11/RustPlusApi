# RustPlusApi.Fcm.Registration

Native C# acquisition of the FCM + Rust+ credentials — **no Node CLI required**. It runs the
full chain so a user logs into Steam once, pairs in-game, and gets everything
`new RustPlus(...)` and `new RustPlusFcm(...)` need.

```csharp
var registration = new FcmRegistration();

// Steps 1-4: GCM check-in -> Firebase install -> FCM register -> Expo token.
var credentials = await registration.AcquireCredentialsAsync();

// Steps 5-6: interactive Steam login (launches Chrome/Chromium) + Rust Companion register.
await registration.RegisterWithRustPlusAsync(credentials);

// Step 7: persist the blob for later RustPlusFcm runs.
CredentialsStore.Save("rustplus.config.json", credentials);

// Step 8: pair in-game ("Pair with Server"); one await yields the RustPlus args.
using var listener = new PairingListener(credentials);
ServerPairing pairing = await listener.WaitForServerPairingAsync();
var rustPlus = new RustPlus(pairing.Ip, pairing.Port, pairing.PlayerId, pairing.PlayerToken);
```

## Steam login requires Chrome/Chromium

The Facepunch login page delivers the auth token via `ReactNativeWebView.postMessage`, which can
only be intercepted in a browser with the same-origin policy relaxed. So the Steam step launches
**Google Chrome or Chromium** (not your default browser) with `--disable-web-security` in an
isolated profile — exactly as rustplus.js does. **Firefox/Safari will not work.** Install Chrome
or Chromium, or set the `CHROME_PATH` environment variable to the executable. **Flatpak** Chrome
/ Chromium (`com.google.Chrome`, `org.chromium.Chromium`) is detected and launched via
`flatpak run` automatically.

> **⚠️ Upstream-fragile / experimental.** Every network step depends on live Google, Expo and
> Facepunch services, and the endpoints/constants drift when those apps change. This flow is
> ported from [liamcottle/rustplus.js](https://github.com/liamcottle/rustplus.js) and
> [`@liamcottle/push-receiver`](https://github.com/liamcottle/push-receiver). Steps 1–4 (GCM
> check-in → Firebase → FCM → Expo) are verified by the opt-in canary against the real endpoints;
> the interactive Steam + pairing steps are only validatable by a manual run. If registration
> breaks, re-check `RegistrationConstants` and the request shapes against those upstream sources.
