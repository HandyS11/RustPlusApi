<div align="center">

# RustPlusApi

**A C# library for the [Rust+](https://rust.facepunch.com/companion) companion API.**

Query and control your server, render security cameras, listen for FCM notifications, and acquire
all the required credentials natively — _no Node.js required_.

[Get Started](articles/getting-started.md) ·
[Articles](articles/introduction.md) ·
[API Reference](xref:RustPlusApi) ·
[GitHub](https://github.com/HandyS11/RustPlusApi)

![.NET](https://img.shields.io/badge/.NET-Standard%202.0%20%7C%2010-512BD4?logo=dotnet)
![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)

</div>

---

## What you can do

- 🖥️ **Control your server** — info, time, map & markers, smart switches, alarms, storage monitors.
- 💬 **Team & clan** — read and send chat, manage the clan MOTD, react to broadcasts.
- 📷 **Cameras** — subscribe to server CCTV/drones, drive them, and render the frames to images.
- 🔔 **Notifications** — receive pairing and alarm pushes over FCM.
- 🔑 **Native credentials** — acquire FCM + Rust+ credentials end to end, replacing the Node CLI.

## Packages

| Package | Description |
| --- | --- |
| [`RustPlusApi`](xref:RustPlusApi) | Core client — typed `Response<T>` API, entities, team/clan/nexus, camera protocol. |
| [`RustPlusApi.Fcm`](xref:RustPlusApi.Fcm) | FCM listener for pairing & alarm notifications. |
| [`RustPlusApi.Fcm.Registration`](xref:RustPlusApi.Fcm.Registration) | Native credential acquisition (no Node.js). |
| [`RustPlusApi.Camera`](xref:RustPlusApi.Camera) | Renders camera frames into images (ImageSharp). |

All packages target **.NET Standard 2.0** and **.NET 10** (usable from .NET Framework 4.6.2+,
.NET 6–10, Mono and Unity).

## Quickstart

```csharp
using RustPlusApi;

using var rustPlus = new RustPlus(server, port, playerId, playerToken);
await rustPlus.ConnectAsync();

var info = await rustPlus.GetInfoAsync();
if (info.IsSuccess)
    Console.WriteLine($"{info.Data!.Name} — {info.Data.PlayerCount}/{info.Data.MaxPlayerCount}");
```

Don't have credentials yet? The [Getting Started](articles/getting-started.md) guide walks you
through acquiring them natively in a couple of minutes.

## Where to next

- **[Getting Started](articles/getting-started.md)** — install, get credentials, first connection.
- **[Credentials](articles/credentials.md)** — how the native registration flow works.
- **[RustPlus Client](articles/rustplus-client.md)** · **[Cameras](articles/cameras.md)** ·
  **[FCM Notifications](articles/fcm-notifications.md)**
- **[API Reference](xref:RustPlusApi)** — every public type.
