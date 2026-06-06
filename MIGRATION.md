# Migrating from 1.x to 2.0

2.0 is a breaking release. This guide covers what changed and how to update.

## Target frameworks

1.x targeted a single framework; **2.0 multi-targets `netstandard2.0` and `net10.0`**, so it now
works on .NET Framework 4.6.2+, .NET 6–10, Mono and Unity. No action needed unless you pinned a
specific assembly.

## `RustPlusLegacy` removed

The `[Obsolete]` `RustPlusLegacy` class and all `*LegacyAsync` methods (which returned raw
`AppMessage`) are gone. Use `RustPlus`, which returns typed `Response<T>`:

| 1.x (`RustPlusLegacy`) | 2.0 (`RustPlus`) |
| --- | --- |
| `GetInfoLegacyAsync()` | `GetInfoAsync()` → `Response<ServerInfo?>` |
| `GetTimeLegacyAsync()` | `GetTimeAsync()` |
| `GetMapLegacyAsync()` | `GetMapAsync()` |
| `GetMapMarkersLegacyAsync()` | `GetMapMarkersAsync()` |
| `GetTeamInfoLegacyAsync()` / `GetTeamChatLegacyAsync()` | `GetTeamInfoAsync()` / `GetTeamChatAsync()` |
| `SendTeamMessageLegacyAsync(msg)` | `SendTeamMessageAsync(msg)` |
| `GetEntityInfoLegacyAsync(id)` | `GetSmartSwitchInfoAsync(id)` / `GetAlarmInfoAsync(id)` / `GetStorageMonitorInfoAsync(id)` |
| `SetEntityValueLegacyAsync(id, v)` | `SetSmartSwitchValueAsync(id, v)` |
| `CheckSubscriptionLegacyAsync(id)` / `SetSubscriptionLegacyAsync(id)` | `CheckSubscriptionAsync(id)` / `SetSubscriptionAsync(id)` |
| `PromoteToLeaderLegacyAsync(id)` | `PromoteToLeaderAsync(id)` |
| `GetClanChatLegacyAsync()` / `SetClanMotdLegacyAsync(msg)` | `GetClanChatAsync()` / `SetClanMotdAsync(msg)` |
| `GetNexusAuthLegacyAsync(key)` | `GetNexusAuthAsync(key)` → `Response<NexusAuth?>` |
| `StrobeEntityLegacyAsync(...)` / `ToggleEntityValueLegacyAsync(id)` | `StrobeSmartSwitchAsync(...)` / `ToggleSmartSwitchAsync(id)` |

`SendRequestAsync(AppRequest)` is kept as the low-level escape hatch (see the serializer note below).

## Single Protobuf dependency: `Google.Protobuf` → `protobuf-net`

The core now uses **protobuf-net** (matching `RustPlusApi.Fcm`); `Google.Protobuf` is gone. The
typed `Response<T>` API is unchanged, but if you used the raw `AppMessage`/`AppRequest` contract
types (e.g. via `SendRequestAsync`), they are now **protobuf-net** types:

- Field **numbers** (the wire format) are unchanged — still compatible with Rust+.
- C# property names are unchanged (PascalCase). Proto field *names* are now snake_case internally
  (cosmetic only).
- Presence: optional scalars use protobuf-net's `ShouldSerialize*()` instead of `Has*`.
- `bytes` fields are `byte[]` instead of `ByteString`.

The contract is now generated from `RustPlusContracts.proto` at build time (no committed `.cs`).

## Bug fix: success responses

`IsError` previously treated a bare `AppSuccess` as an error, so `PromoteToLeaderAsync` and
`SetSubscriptionAsync` always reported failure. They now correctly return `IsSuccess = true`.

## New packages

- **`RustPlusApi.Fcm.Registration`** — acquire credentials natively (no `rustplus.js` Node CLI).
- **`RustPlusApi.Camera`** — render camera frames to images (ImageSharp).

## New features in `RustPlus`

Clan (`GetClanInfoAsync`, `SetClanMotdAsync`, `GetClanChatAsync`, `SendClanMessageAsync` +
`OnClanChatReceived`/`OnClanChanged`), Nexus (`GetNexusAuthAsync`), and the Camera protocol
(`SubscribeToCameraAsync`, `SendCameraInputAsync`, `UnsubscribeFromCameraAsync` +
`OnCameraRaysReceived`).

## FCM credentials

`Credentials` gained optional `Fcm` (token) and `ExpoPushToken` fields (additive). The existing
`rustplus.config.json` from `rustplus.js` still loads. The internal string↔number JSON converters
were removed in favour of `System.Text.Json`'s native number handling — no effect on consumers.
