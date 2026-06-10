# RustPlusApi.Fcm.Registration

Native C# acquisition of the FCM + Rust+ credentials — **no Node.js required**. Runs the full chain
(GCM check-in → Firebase → FCM → Expo → Steam login → Rust Companion) so you log into Steam once,
pair in game, and get everything `RustPlus(...)` and `RustPlusFcm(...)` need.

Targets **.NET Standard 2.0** and **.NET 10**.

## Install

```bash
dotnet add package RustPlusApi.Fcm.Registration
```

## Usage

```csharp
using RustPlusApi.Fcm.Registration;

var registration = new FcmRegistration();

var credentials = await registration.AcquireCredentialsAsync();   // GCM/Firebase/FCM/Expo
await registration.RegisterWithRustPlusAsync(credentials);        // Steam login + Rust Companion
CredentialsStore.Save("rustplus.config.json", credentials);

using var listener = new PairingListener(credentials);
ServerPairing pairing = await listener.WaitForServerPairingAsync(); // pair in game
var rustPlus = new RustPlus(pairing.Ip, pairing.Port, pairing.PlayerId, pairing.PlayerToken);
```

## Requirements & caveats

- **Steam login requires Chrome/Chromium.** The Facepunch login delivers the token via
  `ReactNativeWebView.postMessage`, which is intercepted by driving Chrome through the DevTools
  protocol. Native and Flatpak installs are auto-detected; set `CHROME_PATH` to override.
  **Firefox/Safari will not work.**
- **Upstream-fragile.** Every step depends on live Google/Expo/Facepunch services and drifts when
  those apps change. Ported from rustplus.js / `@liamcottle/push-receiver`; if registration breaks,
  re-check `RegistrationConstants` against those sources.

## Documentation

- [Credentials guide](https://handys11.github.io/RustPlusApi/articles/credentials.html)
- [API reference](https://handys11.github.io/RustPlusApi/) ·
  [source & samples](https://github.com/HandyS11/RustPlusApi)
