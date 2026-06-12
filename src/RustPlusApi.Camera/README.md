# RustPlusApi.Camera

Renders Rust+ camera frames (`AppCameraRays`) into images. A **separate package** so the core
[`RustPlusApi`](https://www.nuget.org/packages/RustPlusApi) stays image-free — take this dependency
only if you need rendered frames. Depends on
[SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) (the 2.1.x line, which supports
`netstandard2.0`).

**Part of [RustPlusApi](https://github.com/HandyS11/RustPlusApi)** · [Documentation](https://handys11.github.io/RustPlusApi/) · [Samples](https://github.com/HandyS11/RustPlusApi/tree/develop/samples)

Targets **.NET Standard 2.0** and **.NET 10**.

## Install

```bash
dotnet add package RustPlusApi.Camera
```

## Usage

`CameraController` manages the session (the server stops streaming rays for subscriptions
that are not renewed; the controller re-subscribes every 10 s) and exposes turret/PTZ
helpers; `CameraRenderer` turns the frames into PNGs:

```csharp
using RustPlusApi.Camera;

var response = await CameraController.SubscribeAsync(rustPlus, "CAM01");
if (!response.IsSuccess) return;

await using var camera = response.Data!;
var renderer = new CameraRenderer(camera.Info.Width, camera.Info.Height);

camera.OnFrameReceived += (_, frame) =>
{
    renderer.AddRays(frame);
    byte[] png = renderer.Render();   // save / display
};

// Turrets (camera.IsAutoTurret): await camera.ShootAsync(); await camera.ReloadAsync();
// PTZ cameras: await camera.ZoomAsync();  Drones: camera.IsDrone
```

Frames accumulate — each `AddRays` fills in more samples, so the image sharpens over time.

> Validated against real captured frames: a golden render test pins the decode output to a
> frame sequence captured from a live server. Refresh the fixture any time with the sample's
> headless capture mode (`RustPlus.ConsoleApp … capture <cameraId> <seconds> [outDir]`).

## Documentation

- [Cameras guide](https://handys11.github.io/RustPlusApi/articles/cameras.html)
- [Troubleshooting](https://handys11.github.io/RustPlusApi/articles/troubleshooting.html)
- [API reference](https://handys11.github.io/RustPlusApi/) ·
  [source & samples](https://github.com/HandyS11/RustPlusApi)
