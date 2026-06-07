namespace RustPlusApi.Data.Cameras;

/// <summary>A 3-component floating-point vector used for camera entity positions, rotations and sizes.</summary>
public sealed record Vector3
{
    /// <summary>The X component.</summary>
    public float X { get; init; }

    /// <summary>The Y component.</summary>
    public float Y { get; init; }

    /// <summary>The Z component.</summary>
    public float Z { get; init; }
}
