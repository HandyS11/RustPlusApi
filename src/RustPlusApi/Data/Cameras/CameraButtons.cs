namespace RustPlusApi.Data.Cameras;

/// <summary>
/// Bitmask of camera input buttons sent via <c>SendCameraInputAsync</c>.
/// Values mirror the Rust+ protocol (liamcottle/rustplus.js).
/// </summary>
[Flags]
public enum CameraButtons
{
    None = 0,
    Forward = 2,
    Backward = 4,
    Left = 8,
    Right = 16,
    Jump = 32,
    Duck = 64,
    Sprint = 128,
    Use = 256,
    FirePrimary = 1024,
    FireSecondary = 2048,
    Reload = 8192,
    FireThird = 134217728,
}
