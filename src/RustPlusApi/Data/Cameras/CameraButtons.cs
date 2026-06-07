namespace RustPlusApi.Data.Cameras;

/// <summary>
/// Bitmask of camera input buttons sent via <c>SendCameraInputAsync</c>.
/// Values mirror the Rust+ protocol (liamcottle/rustplus.js).
/// </summary>
[Flags]
public enum CameraButtons
{
    None = 0,
    Forward = 1 << 1,
    Backward = 1 << 2,
    Left = 1 << 3,
    Right = 1 << 4,
    Jump = 1 << 5,
    Duck = 1 << 6,
    Sprint = 1 << 7,
    Use = 1 << 8,
    FirePrimary = 1 << 10,
    FireSecondary = 1 << 11,
    Reload = 1 << 13,
    FireThird = 1 << 27,
}
