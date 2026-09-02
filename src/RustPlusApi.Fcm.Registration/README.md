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
    onLoginUrl: url => Console.WriteLine($"Open this URL to log in: {url}"));
CredentialsStore.Save("rustplus.config.json", credentials);

using var listener = new PairingListener(credentials);
ServerPairing pairing = await listener.WaitForServerPairingAsync(); // pair in game
var rustPlus = new RustPlus(new RustPlusConnection(pairing.Ip, pairing.Port, pairing.PlayerId, pairing.PlayerToken));
```

## Requirements & caveats

- **Steam login opens your default browser.** The flow is an ordinary redirect: the login page is
  opened with a `returnUrl` pointing at a loopback listener, and Facepunch redirects back with the
  Steam id and auth token. Any browser works. If no browser can be opened (containers, SSH), the
  URL is handed to the `onLoginUrl` callback so you can open it yourself — but it must be opened on
  the **same machine** running registration, since `returnUrl` points at that machine's own
  `localhost`. To finish registration from a different device, forward the port instead: an SSH
  tunnel (`ssh -L 3000:localhost:3000 host`) or a published container port both make the loopback
  callback reachable from wherever you open the link.
- **Upstream-fragile.** Every step depends on live Google/Expo/Facepunch services and drifts when
  those apps change. Ported from rustplus.js / `@liamcottle/push-receiver`; if registration breaks,
  re-check `RegistrationConstants` against those sources.

## Documentation

- [Credentials guide](https://handys11.github.io/RustPlusApi/articles/credentials.html)
- [Troubleshooting](https://handys11.github.io/RustPlusApi/articles/troubleshooting.html)
- [API reference](https://handys11.github.io/RustPlusApi/) ·
  [source & samples](https://github.com/HandyS11/RustPlusApi)
