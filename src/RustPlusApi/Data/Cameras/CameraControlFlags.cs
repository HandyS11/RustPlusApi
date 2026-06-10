using System.Diagnostics.CodeAnalysis;

namespace RustPlusApi.Data.Cameras;

/// <summary>
/// Bitmask describing which inputs a subscribed camera supports
/// (from <c>AppCameraInfo.controlFlags</c>). Values mirror liamcottle/rustplus.js.
/// </summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Mirrors the protocol's controlFlags; 'Flags' suffix is idiomatic for [Flags] enums.")]
[SuppressMessage("Minor Code Smell", "S2344:Enumeration type names should not have a 'Flags' or 'Enum' suffix",
    Justification = "Mirrors the protocol's controlFlags; 'Flags' suffix is idiomatic for [Flags] enums.")]
public enum CameraControlFlags
{
    /// <summary>No controls available.</summary>
    None = 0,

    /// <summary>WASD movement is supported.</summary>
    Movement = 1 << 0,

    /// <summary>Mouse look is supported.</summary>
    Mouse = 1 << 1,

    /// <summary>Sprint and duck inputs are supported.</summary>
    SprintAndDuck = 1 << 2,

    /// <summary>Fire inputs are supported.</summary>
    Fire = 1 << 3,

    /// <summary>Reload input is supported.</summary>
    Reload = 1 << 4,

    /// <summary>The camera renders a crosshair overlay.</summary>
    Crosshair = 1 << 5,
}
