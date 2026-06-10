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

```csharp
using RustPlusApi.Camera;

var info = (await rustPlus.SubscribeToCameraAsync("CAM01")).Data!;
var renderer = new CameraRenderer(info.Width, info.Height);

rustPlus.OnCameraRaysReceived += (_, frame) =>
{
    renderer.AddRays(frame);
    byte[] png = renderer.Render();   // save / display
};
```

Frames accumulate — each `AddRays` fills in more samples, so the image sharpens over time.

> **Experimental.** The ray decode, sample shuffle and colouring are ported faithfully from
> rustplus.js but have not yet been validated against a captured real frame. Treat image fidelity
> as experimental until that validation lands.

## Documentation

- [Cameras guide](https://handys11.github.io/RustPlusApi/articles/cameras.html)
- [Troubleshooting](https://handys11.github.io/RustPlusApi/articles/troubleshooting.html)
- [API reference](https://handys11.github.io/RustPlusApi/) ·
  [source & samples](https://github.com/HandyS11/RustPlusApi)
