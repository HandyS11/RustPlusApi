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
    None = 0,
    Movement = 1 << 0,
    Mouse = 1 << 1,
    SprintAndDuck = 1 << 2,
    Fire = 1 << 3,
    Reload = 1 << 4,
    Crosshair = 1 << 5,
}
