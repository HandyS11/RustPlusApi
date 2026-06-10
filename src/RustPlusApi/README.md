# RustPlusApi

The core [Rust+](https://rust.facepunch.com/companion) companion client. Connect to a server and
use a typed `Response<T>` API for server info, time, map and markers, team & **clan** chat,
**nexus** auth, entities (smart switch / alarm / storage monitor) and the **camera** protocol.

**Part of [RustPlusApi](https://github.com/HandyS11/RustPlusApi)** · [Documentation](https://handys11.github.io/RustPlusApi/) · [Samples](https://github.com/HandyS11/RustPlusApi/tree/develop/samples)

Targets **.NET Standard 2.0** and **.NET 10**.

## Install

```bash
dotnet add package RustPlusApi
```

## Usage

```csharp
using RustPlusApi;

using var rustPlus = new RustPlus(server, port, playerId, playerToken);
await rustPlus.ConnectAsync();

var info = await rustPlus.GetInfoAsync();          // Response<ServerInfo?>
if (info.IsSuccess)
    Console.WriteLine($"{info.Data!.Name} — {info.Data.PlayerCount} players");

rustPlus.OnSmartSwitchTriggered += (_, e) => { /* react to a smart switch */ };
```

Every request returns a `Response<T>` (`IsSuccess` / `Error` / `Data`). Need credentials? Use the
[`RustPlusApi.Fcm.Registration`](https://www.nuget.org/packages/RustPlusApi.Fcm.Registration)
package.

## Documentation

- [RustPlus client guide](https://handys11.github.io/RustPlusApi/articles/rustplus-client.html)
- [Clan & Nexus](https://handys11.github.io/RustPlusApi/articles/clan-and-nexus.html) ·
  [Cameras](https://handys11.github.io/RustPlusApi/articles/cameras.html)
- [Troubleshooting](https://handys11.github.io/RustPlusApi/articles/troubleshooting.html)
- [API reference](https://handys11.github.io/RustPlusApi/) ·
  [source & samples](https://github.com/HandyS11/RustPlusApi)
