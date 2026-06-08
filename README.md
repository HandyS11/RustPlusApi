<div align="center">

# RustPlusApi

**A C# library for the [Rust+](https://rust.facepunch.com/companion) companion API.**
Query and control your server, render security cameras, listen for FCM notifications, and acquire
all the required credentials natively — _no Node.js required_.

[![CI](https://github.com/HandyS11/RustPlusApi/actions/workflows/CI.yml/badge.svg)](https://github.com/HandyS11/RustPlusApi/actions/workflows/CI.yml)
[![CD](https://github.com/HandyS11/RustPlusApi/actions/workflows/CD.yml/badge.svg)](https://github.com/HandyS11/RustPlusApi/actions/workflows/CD.yml)
[![Docs](https://github.com/HandyS11/RustPlusApi/actions/workflows/Documentation.yml/badge.svg)](https://handys11.github.io/RustPlusApi/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
![.NET](https://img.shields.io/badge/.NET-Standard%202.0%20%7C%2010-512BD4?logo=dotnet)

[**Documentation**](https://handys11.github.io/RustPlusApi/) ·
[Getting Started](https://handys11.github.io/RustPlusApi/articles/getting-started.html) ·
[Samples](samples/README.md) ·

</div>

## Packages

| Package | Version | Downloads | Description |
| --- | --- | --- | --- |
| [`RustPlusApi`](src/RustPlusApi/README.md) | [![NuGet](https://img.shields.io/nuget/v/RustPlusApi.svg)](https://www.nuget.org/packages/RustPlusApi) | [![Downloads](https://img.shields.io/nuget/dt/RustPlusApi.svg)](https://www.nuget.org/packages/RustPlusApi) | Core client. Typed `Response<T>` API for info/time/map/markers, team & **clan** chat, **nexus** auth, entities (smart switch / alarm / storage monitor), and the **camera** protocol. |
| [`RustPlusApi.Fcm`](src/RustPlusApi.Fcm/README.md) | [![NuGet](https://img.shields.io/nuget/v/RustPlusApi.Fcm.svg)](https://www.nuget.org/packages/RustPlusApi.Fcm) | [![Downloads](https://img.shields.io/nuget/dt/RustPlusApi.Fcm.svg)](https://www.nuget.org/packages/RustPlusApi.Fcm) | FCM listener — receives server/entity **pairing** and **alarm** notifications. |
| [`RustPlusApi.Fcm.Registration`](src/RustPlusApi.Fcm.Registration/README.md) | [![NuGet](https://img.shields.io/nuget/v/RustPlusApi.Fcm.Registration.svg)](https://www.nuget.org/packages/RustPlusApi.Fcm.Registration) | [![Downloads](https://img.shields.io/nuget/dt/RustPlusApi.Fcm.Registration.svg)](https://www.nuget.org/packages/RustPlusApi.Fcm.Registration) | **Native credential acquisition** (GCM/Firebase/FCM/Expo + Steam login + Rust Companion). Replaces the `rustplus.js` Node CLI. |
| [`RustPlusApi.Camera`](src/RustPlusApi.Camera/README.md) | [![NuGet](https://img.shields.io/nuget/v/RustPlusApi.Camera.svg)](https://www.nuget.org/packages/RustPlusApi.Camera) | [![Downloads](https://img.shields.io/nuget/dt/RustPlusApi.Camera.svg)](https://www.nuget.org/packages/RustPlusApi.Camera) | Renders camera frames (`AppCameraRays`) into images via ImageSharp. |

## Versions

![skills](https://skillicons.dev/icons?i=cs,dotnet)

Targets **.NET Standard 2.0** and **.NET 10** — usable from .NET Framework 4.6.2+, .NET 6/7/8/9/10,
Mono, and Unity.

## Install

```bash
dotnet add package RustPlusApi
dotnet add package RustPlusApi.Fcm
dotnet add package RustPlusApi.Fcm.Registration   # native credentials (optional)
dotnet add package RustPlusApi.Camera             # camera rendering (optional)
```

## Quickstart

**1. Get your credentials** (once). Run the registration sample — it logs you into Steam, writes
`rustplus.config.json`, and prints the `RustPlus(...)` arguments after you pair in game:

```bash
dotnet run --project samples/RustPlus.Register.ConsoleApp
```

See [`samples/`](samples/README.md) for the full walkthrough. (You can still use
`npx @liamcottle/rustplus.js fcm-register` as a fallback.)

**2. Talk to the server:**

```csharp
using var rustPlus = new RustPlus(server, port, playerId, playerToken);
await rustPlus.ConnectAsync();

var info = await rustPlus.GetInfoAsync();
Console.WriteLine(info.Data?.Name);
```

**3. Listen for notifications:**

```csharp
var listener = new RustPlusFcm(credentials);
listener.OnServerPairing += (_, e) => Console.WriteLine($"Paired: {e.Data?.Ip}");
await listener.ConnectAsync();
```

## Documentation

Full guides and the API reference live on the **[documentation site](https://handys11.github.io/RustPlusApi/)**
(built with DocFX). Start with [Getting Started](https://handys11.github.io/RustPlusApi/articles/getting-started.html).
Runnable examples are in [`samples/`](samples/README.md).

## Credits

_This project is grandly inspired by [liamcottle/rustplus.js](https://github.com/liamcottle/rustplus.js)._

Special thanks to [**Versette**](https://github.com/Versette) and [**Devedse**](https://github.com/devedse) for their work on the `RustPlusApi.Fcm` socket.

- Author: [**HandyS11**](https://github.com/HandyS11)
