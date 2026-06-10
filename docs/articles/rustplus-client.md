# RustPlus Client

`RustPlus` is the core client. It opens a WebSocket to the server's companion port and exposes a
typed, task-based API.

## Construct and connect

```csharp
using var rustPlus = new RustPlus(server, port, playerId, playerToken, useFacepunchProxy: false);
await rustPlus.ConnectAsync();
```

| Parameter | Meaning |
| --- | --- |
| `server` | The server's IP address. |
| `port` | The Rust+ companion port (not the in-game connect port). |
| `playerId` | Your Steam ID. |
| `playerToken` | The player token from pairing. |
| `useFacepunchProxy` | Route through the Facepunch proxy instead of connecting directly. |

## The `Response<T>` contract

Every request returns a `Response<T>`:

```csharp
public sealed record Response<T>
{
    public bool IsSuccess { get; init; }
    public ErrorMessage? Error { get; init; }
    public T? Data { get; init; }
}
```

Always check `IsSuccess` before reading `Data`:

```csharp
var time = await rustPlus.GetTimeAsync();
if (time.IsSuccess)
    Console.WriteLine(time.Data!.Time);
else
    Console.WriteLine(time.Error!.Message);
```

## Server & world

| Method | Returns |
| --- | --- |
| `GetInfoAsync()` | `ServerInfo` (name, players, map size, wipe time, …) |
| `GetTimeAsync()` | `TimeInfo` (in-game time, day length, sunrise/sunset) |
| `GetMapAsync()` | `ServerMap` (image bytes, monuments) |
| `GetMapMarkersAsync()` | `MapMarkers` (players, vending machines, events, …) |

## Entities (smart devices)

| Method | Purpose |
| --- | --- |
| `GetSmartSwitchInfoAsync(id)` / `GetAlarmInfoAsync(id)` / `GetStorageMonitorInfoAsync(id)` | Read entity state |
| `SetSmartSwitchValueAsync(id, value)` | Turn a smart switch on/off |
| `ToggleSmartSwitchAsync(id)` / `StrobeSmartSwitchAsync(id, …)` | Convenience helpers |
| `CheckSubscriptionAsync(id)` / `SetSubscriptionAsync(id, on)` | Manage push subscriptions |

## Team

`GetTeamInfoAsync()`, `GetTeamChatAsync()`, `SendTeamMessageAsync(text)`,
`PromoteToLeaderAsync(steamId)`.

See [Clan & Nexus](clan-and-nexus.md) and [Cameras](cameras.md) for those families.

## Events

`RustPlus` raises socket-lifecycle events (`Connecting`, `Connected`, `MessageReceived`,
`NotificationReceived`, `ResponseReceived`, `Disconnecting`, `Disconnected`, `ErrorOccurred`) and
higher-level broadcast events:

```csharp
rustPlus.OnSmartSwitchTriggered += (_, e) => { /* … */ };
rustPlus.OnStorageMonitorTriggered += (_, e) => { /* … */ };
rustPlus.OnTeamChatReceived += (_, msg) => { /* … */ };
rustPlus.OnClanChatReceived += (_, msg) => { /* … */ };
rustPlus.OnClanChanged += (_, clan) => { /* … */ };
rustPlus.OnCameraRaysReceived += (_, frame) => { /* … */ };
```

To receive an entity event you must first make a request on that entity (e.g. call
`GetSmartSwitchInfoAsync(id)`), which subscribes you to its broadcasts.

## Disposal

`RustPlus` is `IDisposable`; dispose it (or `await DisconnectAsync()`) when done.

```csharp
await rustPlus.DisconnectAsync();
```

## Low-level access

`SendRequestAsync(AppRequest)` returns the raw `AppMessage` for advanced/custom requests not
covered by the typed methods. The contract types are protobuf-net classes generated from
`RustPlusContracts.proto`.
