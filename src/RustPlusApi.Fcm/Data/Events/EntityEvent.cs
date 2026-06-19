namespace RustPlusApi.Fcm.Data.Events;

/// <summary>Identifies a Rust+ entity (smart switch, alarm, or storage monitor) in a pairing notification.</summary>
public sealed record EntityEvent
{
    /// <summary>Entity type: Smart Switch, Smart Alarm, or Storage Monitor.</summary>
    public EntityType? EntityType { get; init; }

    /// <summary>The entity's Rust+ ID.</summary>
    public ulong? EntityId { get; init; }

    /// <summary>The entity name as configured in the Rust+ app.</summary>
    public string? EntityName { get; init; }
}
