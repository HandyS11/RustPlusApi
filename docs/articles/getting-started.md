# Getting Started

## Install

```bash
dotnet add package RustPlusApi
dotnet add package RustPlusApi.Fcm
dotnet add package RustPlusApi.Fcm.Registration   # native credentials (optional)
dotnet add package RustPlusApi.Camera             # camera rendering (optional)
```

## 1. Get your credentials

You need credentials to connect. The recommended way is the native
[RustPlusApi.Fcm.Registration](credentials.md) package, which logs you into Steam and links your
account with Rust+ — no Node.js required. The quickest path is the `RustPlus.Register.ConsoleApp`
sample:

```bash
dotnet run --project samples/RustPlus.Register.ConsoleApp
```

It writes `rustplus.config.json` (the FCM credentials) and, after you choose **Pair with Server**
in game, prints the four values for the `RustPlus` constructor.

## 2. Connect and query the server

```csharp
using RustPlusApi;

using var rustPlus = new RustPlus(server, port, playerId, playerToken);
await rustPlus.ConnectAsync();

var info = await rustPlus.GetInfoAsync();
if (info.IsSuccess)
    Console.WriteLine($"{info.Data!.Name} — {info.Data.PlayerCount}/{info.Data.MaxPlayerCount} players");
```

Every request returns a [`Response<T>`](rustplus-client.md) — see the [RustPlus Client](rustplus-client.md)
guide for the full surface.

## 3. Listen for notifications

```csharp
using RustPlusApi.Fcm;
using RustPlusApi.Fcm.Registration;

var credentials = CredentialsStore.Load("rustplus.config.json");
var listener = new RustPlusFcm(credentials);
listener.OnAlarmTriggered += (_, alarm) => Console.WriteLine("Alarm!");
await listener.ConnectAsync();
```

See [FCM Notifications](fcm-notifications.md) for the full event list.

## Next steps

- [Credentials](credentials.md) — how the native registration flow works end to end.
- [RustPlus Client](rustplus-client.md) — info/time/map, entities, team, events.
- [Cameras](cameras.md) — subscribe to and render server cameras.
