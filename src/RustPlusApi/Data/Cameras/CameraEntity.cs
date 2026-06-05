namespace RustPlusApi.Data.Cameras;

/// <summary>A single entity (player/tree/…) sampled within a camera frame.</summary>
public sealed record CameraEntity
{
    public uint EntityId { get; init; }
    public CameraEntityType Type { get; init; }
    public Vector3 Position { get; init; } = new();
    public Vector3 Rotation { get; init; } = new();
    public Vector3 Size { get; init; } = new();
    public string? Name { get; init; }
}
