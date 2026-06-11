namespace RustPlusApi.Data.Cameras;

/// <summary>
/// Bitmask of camera input buttons sent via <c>SendCameraInputAsync</c>.
/// Values mirror the Rust+ protocol (liamcottle/rustplus.js).
/// </summary>
[Flags]
public enum CameraButtons
{
    /// <summary>No button pressed.</summary>
    None = 0,

    /// <summary>Move forward.</summary>
    Forward = 1 << 1,

    /// <summary>Move backward.</summary>
    Backward = 1 << 2,

    /// <summary>Strafe left.</summary>
    Left = 1 << 3,

    /// <summary>Strafe right.</summary>
    Right = 1 << 4,

    /// <summary>Jump.</summary>
    Jump = 1 << 5,

    /// <summary>Crouch / duck.</summary>
    Duck = 1 << 6,

    /// <summary>Sprint.</summary>
    Sprint = 1 << 7,

    /// <summary>Use / interact.</summary>
    Use = 1 << 8,

    /// <summary>Fire primary weapon.</summary>
    FirePrimary = 1 << 10,

    /// <summary>Fire secondary (aim down sights / alt-fire).</summary>
    FireSecondary = 1 << 11,

    /// <summary>Reload.</summary>
    Reload = 1 << 13,

    /// <summary>Fire tertiary (underbarrel / melee).</summary>
    FireThird = 1 << 27,
}
