# Cameras

Rust's CCTV cameras, drones and auto-turrets stream depth/entity data over the companion API.
RustPlusApi splits this into two layers:

- **Protocol layer** (in `RustPlusApi`) — subscribe, send input, and receive typed `CameraFrame`s.
- **Session & rendering layer** (in `RustPlusApi.Camera`) — manage a camera session
  (`CameraController`: keep-alive, turret/PTZ helpers) and turn frames into images. Optional,
  so the core stays image-free.

> [!NOTE]
> Render fidelity is validated against real captured frames — golden tests in
> `RustPlusApi.Camera.UnitTests` pin the decode output to live-captured frame sequences from
> every device type: static CCTV, PTZ camera, auto-turret and drone (2026-06-12).

## Data flow

```mermaid
flowchart LR
    A[SubscribeToCameraAsync id] --> B[CameraInfo<br/>width / height / flags]
    S[(Server)] -- broadcasts --> C[OnCameraRaysReceived<br/>CameraFrame]
    C --> D[CameraRenderer.AddRays]
    D --> E["Render() → PNG bytes"]
    F[SendCameraInputAsync<br/>buttons + mouse deltas] --> S
```

## Identifiers

Camera identifiers are the in-game string codes configured on the camera entity via a computer
station (for example `CAM01`, `DOOR1`, `TURRET_N`). The exact string is case-sensitive and must
match what is set on the in-game computer station.

- **CCTV cameras** use the code typed into the computer station.
- **Auto-turrets** and **drones** exposed through the Rust+ API also accept string identifiers,
  but those are assigned per entity rather than user-configured; the source does not encode further
  naming conventions for them.

## Protocol layer

```csharp
var info = await rustPlus.SubscribeToCameraAsync("CAM01");   // Response<CameraInfo?>
if (!info.IsSuccess) return;

rustPlus.OnCameraRaysReceived += (_, frame) =>
{
    // frame.VerticalFov, frame.Distance, frame.RayData (RLE depth+material), frame.Entities
};

await rustPlus.SendCameraInputAsync(CameraButtons.Forward | CameraButtons.FirePrimary,
                                    mouseDeltaX: 0.1f, mouseDeltaY: 0f);

await rustPlus.UnsubscribeFromCameraAsync();
```

### `CameraInfo` properties

`SubscribeToCameraAsync` returns `CameraInfo` describing the camera:

| Property | Type | Description |
| --- | --- | --- |
| `Width` | `int` | Render width in pixels. |
| `Height` | `int` | Render height in pixels. |
| `NearPlane` | `float` | Near clip-plane distance. |
| `FarPlane` | `float` | Far clip-plane distance (maximum ray-cast range). |
| `ControlFlags` | `CameraControlFlags` | Bitmask of inputs the camera accepts. |

### `CameraControlFlags` values

| Member | Value | Meaning |
| --- | --- | --- |
| `None` | `0` | No controls available. |
| `Movement` | `1` | WASD movement is supported. |
| `Mouse` | `2` | Mouse look is supported. |
| `SprintAndDuck` | `4` | Sprint and duck inputs are supported (drones: vertical movement). |
| `Fire` | `8` | Fire inputs are supported. |
| `Reload` | `16` | Reload input is supported. |
| `Crosshair` | `32` | The camera renders a crosshair overlay. |

### `CameraButtons` enum

`CameraButtons` is a `[Flags]` enum used with `SendCameraInputAsync`:

| Member | Value | Meaning |
| --- | --- | --- |
| `None` | `0` | No button pressed. |
| `Forward` | `2` | Move forward. |
| `Backward` | `4` | Move backward. |
| `Left` | `8` | Strafe left. |
| `Right` | `16` | Strafe right. |
| `Jump` | `32` | Jump. |
| `Duck` | `64` | Crouch / duck. |
| `Sprint` | `128` | Sprint. |
| `Use` | `256` | Use / interact. |
| `FirePrimary` | `1024` | Fire primary weapon. |
| `FireSecondary` | `2048` | Fire secondary (ADS / alt-fire). |
| `Reload` | `8192` | Reload. |
| `FireThird` | `134217728` | Fire tertiary (underbarrel / melee). |

### `CameraFrame` fields

Each `CameraFrame` (delivered via `OnCameraRaysReceived`) contains:

- `VerticalFov` — vertical field-of-view in degrees.
- `SampleOffset` — index into the shuffled sample-position buffer.
- `RayData` — run-length-encoded depth/material bytes.
- `Distance` — maximum ray-cast distance used when encoding the frame.
- `Entities` — list of `CameraEntity` objects visible in the frame (id, type, position/rotation/size, name).
- `TimeOfDay` — in-game time of day when captured (`null` if not reported).
- `CameraPosition` / `CameraRotation` — world-space position and rotation (`null` if not reported).
- `SampleRotation` — world-space rotation the frame's rays were sampled with (`null` if not reported).

## CameraController

`SubscribeToCameraAsync` alone is not enough for long sessions: the server stops streaming
rays for subscriptions that are not renewed. `CameraController` (in the `RustPlusApi.Camera`
package) wraps the full session — it re-subscribes every 10 seconds (configurable), forwards
frames, and exposes the press-and-release gestures PTZ cameras and auto-turrets expect:

```csharp
var response = await CameraController.SubscribeAsync(rustPlus, "TURRET01");
if (!response.IsSuccess) return;

await using var turret = response.Data!;
turret.OnFrameReceived += (_, frame) => renderer.AddRays(frame);

if (turret.IsAutoTurret)        // ControlFlags has Reload (turret-only input)
{
    await turret.ShootAsync();  // FirePrimary press + release
    await turret.ReloadAsync(); // Reload press + release
}
else
{
    // PTZ cameras only. Cycles the zoom levels and wraps.
    await turret.ZoomAsync();
}

await turret.LookAsync(10f, 0f);                  // mouse look (Mouse flag)
await turret.MoveAsync(CameraButtons.Forward);    // drone nudge (Movement flag)
```

The action helpers are **capability-gated**: an action the device does not advertise is refused
client-side with `RustPlusErrorCode.NotSupported` and **nothing is sent**. This matters because
the live server acknowledges unsupported inputs with success while silently ignoring them — and
because zoom shares the `FirePrimary` button with turret fire, an ungated zoom sent to a turret
would actually shoot it. `ZoomAsync` requires `IsPtzCamera`; `ShootAsync`/`ReloadAsync` require
`IsAutoTurret`; `LookAsync` requires the `Mouse` flag; `MoveAsync` requires `Movement` (WASD)
and/or `SprintAndDuck` (jump/duck/sprint). The raw `SendInputAsync`/`PressAsync` remain ungated
escape hatches.

> [!IMPORTANT]
> An accepted input is not necessarily an **actuated** one — the server acks everything with
> success. Live-tested (2026-06, player disconnected):
>
> - **PTZ look and zoom act** — the frame's `CameraRotation` pans and `VerticalFov` cycles
>   through the four zoom levels (65 → 43.33 → 26 → 16.25, wrapping).
> - **Drones fly, but only under a continuous input stream** — `MoveAsync` streams frames for
>   its hold duration for exactly this reason; a single press-and-release is acked and ignored.
>   Vertical controls are `Sprint` (ascend) and `Duck` (descend) — that is what the
>   `SprintAndDuck` control flag refers to; `Jump` did nothing on a live drone. A parked drone
>   responds to `Sprint` (take-off) only: planar movement and mouse look are ignored until it
>   is airborne. Looking mid-flight changes the heading `Forward`/`Backward` move along — land
>   before looking around if you need to return to the starting spot.
> - **Drones take damage** — hitting the ground or structures and incoming fire all cost the
>   drone HP, and a destroyed drone fails subsequent subscribes with
>   `RustPlusErrorCode.NoPlayer` (the entity is gone). Fly gently and land with short `Duck`
>   bursts. A player can attach a package for the drone to carry; a `FirePrimary` click drops
>   it — use the raw `PressAsync(CameraButtons.FirePrimary)` for that (the gated
>   `ShootAsync`/`ZoomAsync` deliberately refuse on drones).
> - **Turret inputs proved nothing on the test server** (the turret was deactivated there) —
>   shoot/reload/look acks are transport-proof only; verify in game.
>
> When you need proof an input acted, watch the frame's
> `CameraPosition`/`CameraRotation`/`VerticalFov` — never the ack.

One device-kind check exists per camera type, derived from live-observed control flags:

| Check | Rule | Live-observed flags |
| --- | --- | --- |
| `IsAutoTurret` | has `Reload` (turret-only input — a drone may render a crosshair, so `Crosshair` is not a reliable marker) | `Mouse, Fire, Reload, Crosshair` |
| `IsDrone` | has `SprintAndDuck` (the drone's vertical movement controls) | `Movement, Mouse, SprintAndDuck` |
| `IsPtzCamera` | has `Mouse` but is neither turret nor drone (`Fire` drives the zoom) | `Mouse, Fire` |
| `IsStaticCamera` | `ControlFlags` is `None` | `None` |

The four checks are mutually exclusive for every flag combination observed in game.

A failed renewal raises `OnKeepAliveFailed` with the server's `ErrorMessage` (for example
`NoPlayer` after the camera was destroyed in game; a disconnected client reports `Unknown`
with the exception message). The controller keeps retrying, so a later reconnect recovers on
its own — but frames going quiet after this event means the subscription is dead:

```csharp
turret.OnKeepAliveFailed += (_, error) =>
    Console.WriteLine($"Keep-alive failed: {error.Code} {error.Message}");
```

Disposing the controller stops the keep-alive and unsubscribes. Create at most one live
controller per client — the server tracks a single camera subscription per connection.

> [!NOTE]
> Cameras are accessed while the paired player is **disconnected** from the server. If the
> camera entity itself has been destroyed in game, subscribing fails with
> `RustPlusErrorCode.NoPlayer` (`no_player`) — despite the name, the error is about the
> missing camera entity, not the player.

## Rendering layer (RustPlusApi.Camera)

`CameraRenderer` decodes the ray stream and produces a PNG. Create one per camera, sized from the
subscription, feed frames, and render:

```csharp
using RustPlusApi.Camera;

var info = (await rustPlus.SubscribeToCameraAsync("CAM01")).Data!;
var renderer = new CameraRenderer(info.Width, info.Height);

rustPlus.OnCameraRaysReceived += (_, frame) =>
{
    renderer.AddRays(frame);
    byte[] png = renderer.Render();   // save or display
};
```

Frames accumulate: each `AddRays` fills in more samples, so the image sharpens over successive
frames.
