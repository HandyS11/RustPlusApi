# RustPlusApi.Fcm

Listens to Firebase Cloud Messaging for Rust+ push notifications — server/entity **pairing** and
**alarm** triggers.

Targets **.NET Standard 2.0** and **.NET 10**.

## Install

```bash
dotnet add package RustPlusApi.Fcm
```

## Usage

```csharp
using RustPlusApi.Fcm;

var listener = new RustPlusFcm(credentials);

listener.OnServerPairing += (_, e) => Console.WriteLine($"Pair: {e.Data?.Ip}:{e.Data?.Port}");
listener.OnAlarmTriggered += (_, alarm) => Console.WriteLine($"Alarm: {alarm?.Title}");

await listener.ConnectAsync();
// …
listener.Disconnect();
```

## Credentials

Get `credentials` natively with the
[`RustPlusApi.Fcm.Registration`](https://www.nuget.org/packages/RustPlusApi.Fcm.Registration)
package (no Node.js required) — or the legacy `npx @liamcottle/rustplus.js fcm-register` CLI, whose
`rustplus.config.json` is also supported.

## Documentation

- [FCM notifications guide](https://handys11.github.io/RustPlusApi/articles/fcm-notifications.html) ·
  [Credentials](https://handys11.github.io/RustPlusApi/articles/credentials.html)
- [API reference](https://handys11.github.io/RustPlusApi/) ·
  [source & samples](https://github.com/HandyS11/RustPlusApi)
