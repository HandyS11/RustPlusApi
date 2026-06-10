# Recipes

Common integration patterns — each snippet is self-contained and copy-pasteable.

## 1. React to an alarm by flipping a switch

Listen for a paired alarm via `RustPlusFcm`, then turn on a siren switch via `RustPlus` when it
fires. The FCM listener and the companion client run in parallel: the FCM socket stays open
indefinitely while the `RustPlus` connection is opened on demand for the flip.

```csharp
using RustPlusApi;
using RustPlusApi.Fcm;
using RustPlusApi.Fcm.Data.Events;
using RustPlusApi.Fcm.Registration;

// ── constants ────────────────────────────────────────────────────────────────
const string ConfigPath   = "rustplus.config.json";
const string ServerIp     = "192.0.2.1";
const int    ServerPort   = 28082;         // companion port from pairing, not game port
const ulong  PlayerId     = 76561198000000000UL;
const int    PlayerToken  = -123456789;
const ulong  SirenSwitchId = 1234567UL;   // entity ID of your siren smart switch

// ── FCM listener ─────────────────────────────────────────────────────────────
var credentials = CredentialsStore.Load(ConfigPath);
using var listener = new RustPlusFcm(credentials);

listener.OnAlarmTriggered += async (_, alarm) =>
{
    Console.WriteLine($"Alarm fired: {alarm?.Title} — turning on siren switch");

    using var rustPlus = new RustPlus(new RustPlusConnection(ServerIp, ServerPort, PlayerId, PlayerToken));
    await rustPlus.ConnectAsync();

    var result = await rustPlus.SetSmartSwitchValueAsync(SirenSwitchId, true);
    if (!result.IsSuccess)
        Console.Error.WriteLine($"Switch failed: {result.Error?.Message}");
};

await listener.ConnectAsync();
Console.WriteLine("Listening for alarms — press Ctrl+C to exit.");
await Task.Delay(Timeout.Infinite);
```

> [!NOTE]
> `OnAlarmTriggered` only fires for alarms that have been paired in game via the Rust+ app.
> See [FCM Notifications](fcm-notifications.md) for the full event surface and reconnect strategy.

## 2. Save the server map to disk

`GetMapAsync` returns a `ServerMap` whose `JpgImage` property contains the raw JPEG bytes. Cache
this response — the map is large and rarely changes.

```csharp
using RustPlusApi;

const string ServerIp   = "192.0.2.1";
const int    ServerPort = 28082;
const ulong  PlayerId   = 76561198000000000UL;
const int    PlayerToken = -123456789;

using var rustPlus = new RustPlus(new RustPlusConnection(ServerIp, ServerPort, PlayerId, PlayerToken));
await rustPlus.ConnectAsync();

var response = await rustPlus.GetMapAsync();
if (!response.IsSuccess)
{
    Console.Error.WriteLine($"GetMapAsync failed: {response.Error?.Message}");
    return;
}

var map = response.Data!;
if (map.JpgImage is { Length: > 0 } jpg)
{
    await File.WriteAllBytesAsync("map.jpg", jpg);
    Console.WriteLine($"Saved map.jpg ({jpg.Length:N0} bytes, {map.Width}×{map.Height} game units)");
}
else
{
    Console.Error.WriteLine("Server returned no map image.");
}
```

> [!NOTE]
> `ServerMap.JpgImage` is `byte[]?` — check for null/empty before writing.
> See [RustPlus Client](rustplus-client.md) for the full `GetMapAsync` response shape.

## 3. Minimal team-chat echo bot

Subscribe to `OnTeamChatReceived` and echo back every message that is not your own. The guard
compares `TeamMessage.SteamId` against your own `playerId` so the bot does not echo itself into an
infinite loop.

```csharp
using RustPlusApi;
using RustPlusApi.Data;

const string ServerIp    = "192.0.2.1";
const int    ServerPort  = 28082;
const ulong  PlayerId    = 76561198000000000UL;  // your SteamID64 — used as the echo guard
const int    PlayerToken = -123456789;

using var rustPlus = new RustPlus(new RustPlusConnection(ServerIp, ServerPort, PlayerId, PlayerToken));

rustPlus.OnTeamChatReceived += async (_, msg) =>
{
    // Ignore our own messages to avoid an infinite echo loop.
    if (msg.SteamId == PlayerId)
        return;

    var echo = $"[bot] {msg.Name}: {msg.Message}";
    Console.WriteLine($"Echo → {echo}");

    var result = await rustPlus.SendTeamMessageAsync(echo);
    if (!result.IsSuccess)
        Console.Error.WriteLine($"SendTeamMessageAsync failed: {result.Error?.Message}");
};

await rustPlus.ConnectAsync();
Console.WriteLine("Echo bot running — press Ctrl+C to exit.");
await Task.Delay(Timeout.Infinite);
```

> [!NOTE]
> `SendTeamMessageAsync` returns `Response<TeamMessage?>` — the payload is the server's echo of
> your own message, not a confirmation. See [RustPlus Client](rustplus-client.md#team).

## 4. Camera snapshot loop

Subscribe to a camera, accumulate frames with `CameraRenderer.AddRays`, render a PNG, then
unsubscribe. Requires the `RustPlusApi.Camera` NuGet package (install with
`dotnet add package RustPlusApi.Camera`).

```csharp
using RustPlusApi;
using RustPlusApi.Camera;
using RustPlusApi.Data.Cameras;

const string ServerIp    = "192.0.2.1";
const int    ServerPort  = 28082;
const ulong  PlayerId    = 76561198000000000UL;
const int    PlayerToken = -123456789;
const string CameraId    = "CAM01";   // in-game identifier set on the computer station
const int    FrameTarget = 10;        // accumulate 10 frames before saving

using var rustPlus = new RustPlus(new RustPlusConnection(ServerIp, ServerPort, PlayerId, PlayerToken));
await rustPlus.ConnectAsync();

var sub = await rustPlus.SubscribeToCameraAsync(CameraId);
if (!sub.IsSuccess)
{
    Console.Error.WriteLine($"Subscribe failed: {sub.Error?.Message}");
    return;
}

var info = sub.Data!;
var renderer = new CameraRenderer(info.Width, info.Height);
var tcs = new TaskCompletionSource<bool>();
var frameCount = 0;

rustPlus.OnCameraRaysReceived += async (_, frame) =>
{
    renderer.AddRays(frame);

    if (++frameCount < FrameTarget)
        return;

    // Enough frames — render, save, unsubscribe.
    var png = renderer.Render();
    await File.WriteAllBytesAsync("snapshot.png", png);
    Console.WriteLine($"Saved snapshot.png ({png.Length:N0} bytes) after {frameCount} frames.");
    tcs.TrySetResult(true);
};

await tcs.Task;   // wait until the snapshot is saved
await rustPlus.UnsubscribeFromCameraAsync();
```

> [!WARNING]
> Camera image fidelity is experimental — the decode has not yet been validated against a captured
> real frame. See [Cameras](cameras.md) for the rendering layer details.

## 5. Persist and reload credentials

Run the registration once, save to disk, and reload on subsequent runs. The one-await
`PairingListener` pattern handles the "pair in game and get the constructor args" step.

```csharp
using RustPlusApi;
using RustPlusApi.Fcm.Registration;

const string ConfigPath = "rustplus.config.json";

// ── first run: acquire and persist FCM credentials ───────────────────────────
if (!File.Exists(ConfigPath))
{
    Console.WriteLine("No credentials found — running registration flow (Chrome will open).");
    var registration = new FcmRegistration();
    var credentials = await registration.AcquireCredentialsAsync();
    await registration.RegisterWithRustPlusAsync(credentials);
    CredentialsStore.Save(ConfigPath, credentials);
    Console.WriteLine($"Credentials saved to {ConfigPath}.");
}

// ── every run: load and listen for the next pairing ──────────────────────────
var creds = CredentialsStore.Load(ConfigPath);

Console.WriteLine("In game: open Rust+ → Pair with Server, then wait …");
using var pairingListener = new PairingListener(creds);
var pairing = await pairingListener.WaitForServerPairingAsync();

Console.WriteLine($"Paired: {pairing.Ip}:{pairing.Port} (player {pairing.PlayerId})");

// ── use the pairing values immediately ───────────────────────────────────────
using var rustPlus = new RustPlus(new RustPlusConnection(pairing.Ip, pairing.Port, pairing.PlayerId, pairing.PlayerToken));
await rustPlus.ConnectAsync();

var info = await rustPlus.GetInfoAsync();
if (info.IsSuccess)
    Console.WriteLine($"{info.Data!.Name} — {info.Data.PlayerCount}/{info.Data.MaxPlayerCount} players");
```

> [!NOTE]
> `CredentialsStore.Save` / `Load` write and read indented JSON (`rustplus.config.json`) in this
> library's own format. (The legacy `rustplus.js` config layout is different — the
> `RustPlus.Fcm.ConsoleApp` sample ships a loader that accepts both.)
> See [Credentials](credentials.md) for the full registration flow, including browser discovery
> order and upstream-fragility notes.
