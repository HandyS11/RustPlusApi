<div align="center">

# RustPlusApi

**A C# library for the [Rust+](https://rust.facepunch.com/companion) companion API.**
Query and control your server, render security cameras, listen for FCM notifications, and acquire
all the required credentials natively — _no Node.js required_.

[![CI](https://github.com/HandyS11/RustPlusApi/actions/workflows/CI.yml/badge.svg)](https://github.com/HandyS11/RustPlusApi/actions/workflows/CI.yml)
[![CD](https://github.com/HandyS11/RustPlusApi/actions/workflows/CD.yml/badge.svg)](https://github.com/HandyS11/RustPlusApi/actions/workflows/CD.yml)
[![Docs](https://github.com/HandyS11/RustPlusApi/actions/workflows/Documentation.yml/badge.svg)](https://handys11.github.io/RustPlusApi/)

![.NET](https://img.shields.io/badge/.NET-Standard%202.0%20%7C%2010-512BD4)
[![NuGet Version](https://img.shields.io/nuget/v/RustPlusApi.svg)](https://www.nuget.org/packages/RustPlusApi)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![codecov](https://codecov.io/gh/HandyS11/RustPlusApi/graph/badge.svg?token=UZCM1A6ERM)](https://codecov.io/gh/HandyS11/RustPlusApi)
[![Mutation Score](https://img.shields.io/endpoint?style=flat&url=https%3A%2F%2Fbadge-api.stryker-mutator.io%2Fgithub.com%2FHandyS11%2FRustPlusApi%2Fdevelop)](https://dashboard.stryker-mutator.io/reports/github.com/HandyS11/RustPlusApi/develop)

[Getting Started](https://handys11.github.io/RustPlusApi/articles/getting-started.html) ·
[Documentation](https://handys11.github.io/RustPlusApi/) ·
[Samples](samples/README.md)

</div>

## Packages

| Package | Downloads | Description |
| --- | --- | --- |
| [`RustPlusApi`](src/RustPlusApi/README.md) | [![Downloads](https://img.shields.io/nuget/dt/RustPlusApi.svg)](https://www.nuget.org/packages/RustPlusApi) | Core client. Typed `Response<T>` API for info/time/map/markers, team & **clan** chat, **nexus** auth, entities (smart switch / alarm / storage monitor), and the **camera** protocol. |
| [`RustPlusApi.Fcm`](src/RustPlusApi.Fcm/README.md) | [![Downloads](https://img.shields.io/nuget/dt/RustPlusApi.Fcm.svg)](https://www.nuget.org/packages/RustPlusApi.Fcm) | FCM listener — receives server/entity **pairing** and **alarm** notifications. |
| [`RustPlusApi.Fcm.Registration`](src/RustPlusApi.Fcm.Registration/README.md) | [![Downloads](https://img.shields.io/nuget/dt/RustPlusApi.Fcm.Registration.svg)](https://www.nuget.org/packages/RustPlusApi.Fcm.Registration) | **Native credential acquisition** (GCM/Firebase/FCM/Expo + Steam login + Rust Companion). Replaces the `rustplus.js` Node CLI. |
| [`RustPlusApi.Camera`](src/RustPlusApi.Camera/README.md) | [![Downloads](https://img.shields.io/nuget/dt/RustPlusApi.Camera.svg)](https://www.nuget.org/packages/RustPlusApi.Camera) | Camera sessions (`CameraController`: keep-alive, turret/PTZ helpers, device-kind checks) and frame rendering via ImageSharp. |
| [`RustPlusApi.Extensions.DependencyInjection`](src/RustPlusApi.Extensions.DependencyInjection/README.md) | [![Downloads](https://img.shields.io/nuget/dt/RustPlusApi.Extensions.DependencyInjection.svg)](https://www.nuget.org/packages/RustPlusApi.Extensions.DependencyInjection) | DI registration (`AddRustPlus`, `IRustPlusFactory`) for the core client. |
| [`RustPlusApi.Fcm.Extensions.DependencyInjection`](src/RustPlusApi.Fcm.Extensions.DependencyInjection/README.md) | [![Downloads](https://img.shields.io/nuget/dt/RustPlusApi.Fcm.Extensions.DependencyInjection.svg)](https://www.nuget.org/packages/RustPlusApi.Fcm.Extensions.DependencyInjection) | DI registration (`AddRustPlusFcm`, `IRustPlusFcmFactory`) for the FCM listener. |

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

Prefer a browser to a terminal? The [Rust+ credentials website](apps/RustPlusApi.CredentialsWeb/README.md)
is the same registration flow behind a single-page app — no .NET SDK needed. You run it yourself and
browse to it on the same machine; Facepunch's Steam login only completes over loopback, so there is
no public instance. See its README for the full command.

**2. Talk to the server:**

```csharp
using var rustPlus = new RustPlus(new RustPlusConnection(server, port, playerId, playerToken));
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
(built with DocFX). Start with [Getting Started](https://handys11.github.io/RustPlusApi/articles/getting-started.html),
browse the [Recipes](https://handys11.github.io/RustPlusApi/articles/recipes.html) for common patterns, or check the
[Troubleshooting](https://handys11.github.io/RustPlusApi/articles/troubleshooting.html) page if something isn't working.
Runnable examples are in [`samples/`](samples/README.md).

## Credits

_This project is grandly inspired by [liamcottle/rustplus.js](https://github.com/liamcottle/rustplus.js)._

Special thanks to [**Versette**](https://github.com/Versette) and [**Devedse**](https://github.com/devedse) for their work on the `RustPlusApi.Fcm` socket.

- Author: [**HandyS11**](https://github.com/HandyS11)
