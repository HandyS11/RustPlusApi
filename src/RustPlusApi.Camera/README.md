# RustPlusApi.Camera

Renders Rust+ camera frames into images. It is a **separate package** so the core
`RustPlusApi` stays image-free — only take this dependency if you need rendered frames.

It depends on [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) (the 2.1.x
line, which supports `netstandard2.0`).

## Usage

```csharp
// 1. Subscribe with the core client and keep the returned CameraInfo (width/height).
var info = (await rustPlus.SubscribeToCameraAsync("CAM01")).Data!;

// 2. Create a renderer sized from the camera.
var renderer = new CameraRenderer(info.Width, info.Height);

// 3. Feed each frame, then render whenever you want the current image.
rustPlus.OnCameraRaysReceived += (_, frame) =>
{
    renderer.AddRays(frame);
    byte[] png = renderer.Render();
    // save / display the PNG bytes
};
```

Frames accumulate: each `AddRays` fills in more of the image, so it sharpens over
successive frames.

> **Experimental.** The ray-decode, sample shuffle and colouring are ported faithfully from
> [liamcottle/rustplus.js](https://github.com/liamcottle/rustplus.js) but have **not yet been
> validated against a real captured frame** (pending the v2 golden-payload capture). Treat
> image fidelity as experimental until that validation lands.
