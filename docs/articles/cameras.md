# Cameras

Rust's CCTV cameras, drones and auto-turrets stream depth/entity data over the companion API.
RustPlusApi splits this into two layers:

- **Protocol layer** (in `RustPlusApi`) — subscribe, send input, and receive typed `CameraFrame`s.
- **Rendering layer** (in `RustPlusApi.Camera`) — turn frames into images. Optional, so the core
  stays image-free.

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

- `CameraInfo` carries `Width`, `Height`, `NearPlane`, `FarPlane` and a `ControlFlags`
  (`CameraControlFlags`) bitmask of supported inputs.
- `CameraButtons` is a `[Flags]` enum (`Forward`, `Backward`, `Left`, `Right`, `Jump`, `Duck`,
  `Sprint`, `Use`, `FirePrimary`, `FireSecondary`, `Reload`, `FireThird`).
- Each `CameraFrame` (a broadcast) contains the run-length-encoded `RayData` plus the in-view
  `CameraEntity` list (id, type, position/rotation/size, name).

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

> **Experimental.** The decode, sample shuffle and colouring are ported faithfully from
> rustplus.js but not yet validated against a captured real frame. Treat image fidelity as
> experimental until that validation lands.
