# RustPlusApi.Fcm.Registration

Native C# acquisition of the FCM + Rust+ credentials — **no Node.js required**. Runs the full chain
(GCM check-in → Firebase → FCM → Expo → Steam login → Rust Companion) so you log into Steam once,
pair in game, and get everything `RustPlus(...)` and `RustPlusFcm(...)` need.

**Part of [RustPlusApi](https://github.com/HandyS11/RustPlusApi)** · [Documentation](https://handys11.github.io/RustPlusApi/) · [Samples](https://github.com/HandyS11/RustPlusApi/tree/develop/samples)

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
var steamLogin = await registration.RegisterWithRustPlusAsync(    // Steam login + Rust Companion
    credentials,
    onLoginUrl: url => Console.WriteLine($"Steam login address: {url}"));
CredentialsStore.Save("rustplus.config.json", credentials);

using var listener = new PairingListener(credentials);
ServerPairing pairing = await listener.WaitForServerPairingAsync(); // pair in game
var rustPlus = new RustPlus(new RustPlusConnection(pairing.Ip, pairing.Port, pairing.PlayerId, pairing.PlayerToken)
{
    // Optional: connect through Facepunch's companion socket rather than the server's app port.
    ServerId = pairing.ServerId,
    AuthToken = steamLogin.Token, // a credential — CredentialsStore does not save it
});
```

## Requirements & caveats

- **Steam login opens its own browser window.** The library starts Edge (Windows first), Chrome,
  Brave or Chromium (Flatpak included) with a fresh temporary profile, drives it over the Chrome
  DevTools Protocol, and captures Facepunch's `rust-plus://` redirect, which carries the Steam id
  and auth token. You sign in to Steam in that window. The `rust-plus:` redirect is never followed,
  so the official Rust+ desktop app is not launched. The profile is deleted afterwards (if that
  fails the login still succeeds and the next login sweeps it); a process killed hard can leave
  the window open for you to close. Snap browsers are detected and skipped, and
  `SteamLoginOptions.BrowserPath` (absolute path only) overrides the search.
- **Paste fallback.** With no usable browser (Firefox only, Snap only, no display), set
  `FcmRegistration.SteamLoginOptions.ReadPastedAddress`. It receives the reason the browser route
  was not used; show it with `SteamLoginService.PasteInstructions`, then return the
  `rust-plus://rust-auth-session?…` address the user copies by hand. Without the callback,
  `RegisterWithRustPlusAsync` throws `InvalidOperationException` with the reason, the instructions
  and the login address.
- **Windows.** The library reports a non-Windows browser to Facepunch on its `/app` steps only,
  because Facepunch appears to block Windows browsers deliberately (it answers "Login Failed").
  That may break again.
- **Debugging port.** While the login runs, the browser's DevTools port listens on `127.0.0.1`
  without authentication, so any local process could attach in that window. This is an accepted
  local risk. The browser picks the port itself as it starts, and the library reads it from the
  `DevToolsActivePort` file the browser writes into the private profile directory, so no other
  process can take the port before the browser binds it.
- `steamLoginPort` / `SteamLoginService(port)` are kept for source compatibility and unused. The
  Rust+ auth token lives 14 days.
- **Upstream-fragile.** Every step depends on live Google/Expo/Facepunch services and drifts when
  those apps change. Ported from rustplus.js / `@liamcottle/push-receiver`; if registration breaks,
  re-check `RegistrationConstants` against those sources.

## Documentation

- [Credentials guide](https://handys11.github.io/RustPlusApi/articles/credentials.html)
- [Troubleshooting](https://handys11.github.io/RustPlusApi/articles/troubleshooting.html)
- [API reference](https://handys11.github.io/RustPlusApi/) ·
  [source & samples](https://github.com/HandyS11/RustPlusApi)
