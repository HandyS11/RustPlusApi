namespace RustPlusApi.Data.Cameras;

/// <summary>A single entity (player/tree/…) sampled within a camera frame.</summary>
public sealed record CameraEntity
{
    /// <summary>Unique entity identifier.</summary>
    public uint EntityId { get; init; }

    /// <summary>The kind of entity (player, tree, etc.).</summary>
    public CameraEntityType Type { get; init; }

    /// <summary>World-space position of the entity.</summary>
    public Vector3 Position { get; init; } = new();

    /// <summary>Euler rotation of the entity in world space.</summary>
    public Vector3 Rotation { get; init; } = new();

    /// <summary>World-space bounding size of the entity.</summary>
    public Vector3 Size { get; init; } = new();

    /// <summary>Display name of the entity, if available (e.g. player name).</summary>
    public string? Name { get; init; }
}
