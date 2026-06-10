# Cameras

Rust's CCTV cameras, drones and auto-turrets stream depth/entity data over the companion API.
RustPlusApi splits this into two layers:

- **Protocol layer** (in `RustPlusApi`) — subscribe, send input, and receive typed `CameraFrame`s.
- **Rendering layer** (in `RustPlusApi.Camera`) — turn frames into images. Optional, so the core
  stays image-free.

> [!WARNING]
> The decode, sample shuffle and colouring are ported faithfully from rustplus.js but not yet
> validated against a captured real frame. Treat image fidelity as experimental until that
> validation lands.

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
| `SprintAndDuck` | `4` | Sprint and duck inputs are supported. |
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
