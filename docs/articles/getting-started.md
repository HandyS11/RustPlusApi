# Getting Started

## Prerequisites

- **.NET SDK** — any target the library supports: .NET Framework 4.6.2 or later, .NET 6–10, Mono,
  or Unity. The `netstandard2.0` build covers everything; the `net10.0` build uses modern BCL
  fast-paths on .NET 10.
- **Google Chrome or Chromium** — required for the one-time credential registration step (Step 1).
  A native install, a Flatpak, or any path you point to via `CHROME_PATH` works. Firefox and Safari
  will not work — see [Credentials](credentials.md#steam-login-requires-chromechromium).
- **A Rust server with Rust+ enabled** — you need access to the in-game *Rust+* menu so you can
  choose *Pair with Server* to complete the pairing flow.

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

### What the four values are

All four arrive together in the server-pairing push notification.

| Value | Meaning | Source |
| --- | --- | --- |
| `server` | Server IP address | Pairing notification (`ServerPairing.Ip`) |
| `port` | Rust+ companion port — **not** the game join port | Pairing notification (`ServerPairing.Port`) |
| `playerId` | Your SteamID64 | Pairing notification (`ServerPairing.PlayerId`) |
| `playerToken` | Per-server auth token issued at pairing time | Pairing notification (`ServerPairing.PlayerToken`) |

## 2. Connect and query the server

```csharp
using RustPlusApi;

using var rustPlus = new RustPlus(new RustPlusConnection(server, port, playerId, playerToken));
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

## If something doesn't work

See [Troubleshooting](troubleshooting.md).
