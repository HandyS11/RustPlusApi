# Introduction

RustPlusApi is a C# library for the [Rust+](https://rust.facepunch.com/companion) companion API —
the same API the official Rust+ mobile app uses to talk to a Rust game server. With it you can
read server state, control smart devices, watch security cameras, chat with your team/clan, and
receive push notifications, all from .NET.

There is no official public schema for the Rust+ protocol; it is reverse-engineered by the
community. RustPlusApi is grandly inspired by
[liamcottle/rustplus.js](https://github.com/liamcottle/rustplus.js).

## The packages

The library is split so you only take the dependencies you need.

| Package | What it does | Depends on |
| --- | --- | --- |
| **RustPlusApi** | Core WebSocket client with a typed `Response<T>` API: info, time, map, markers, team & clan chat, nexus auth, entities (smart switch / alarm / storage monitor) and the camera protocol. | protobuf-net |
| **RustPlusApi.Fcm** | Listens to Firebase Cloud Messaging for server/entity **pairing** and **alarm** notifications. | protobuf-net, System.Text.Json |
| **RustPlusApi.Fcm.Registration** | Acquires all the credentials natively (GCM check-in → Firebase → FCM → Expo → Steam → Rust Companion). Replaces the `rustplus.js` Node CLI. | RustPlusApi.Fcm |
| **RustPlusApi.Camera** | Renders camera frames (`AppCameraRays`) into images. | RustPlusApi, SixLabors.ImageSharp |

## Target frameworks

Everything targets **.NET Standard 2.0** and **.NET 10**, so the libraries run on .NET Framework
4.6.2+, .NET 6–10, Mono and Unity. The `net10.0` build uses modern BCL fast-paths; the
`netstandard2.0` build keeps the reach.

## How it fits together

1. **Get credentials once** with `RustPlusApi.Fcm.Registration` (or the rustplus.js CLI). This
   yields the FCM credentials and, after you pair in game, the four values a `RustPlus` client
   needs: server IP, port, player id, player token.
2. **Talk to the server** with `RustPlusApi` (`RustPlus`).
3. **Listen for notifications** (pairing, alarms) with `RustPlusApi.Fcm` (`RustPlusFcm`).
4. **Render cameras** (optional) with `RustPlusApi.Camera`.

Start with [Getting Started](getting-started.md).
