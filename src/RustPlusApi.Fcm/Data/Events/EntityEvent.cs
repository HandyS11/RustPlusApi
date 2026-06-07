namespace RustPlusApi.Fcm.Data.Events;

/// <summary>Identifies a Rust+ entity (smart switch, alarm, or storage monitor) in a pairing notification.</summary>
public sealed record EntityEvent
{
    /// <summary>Entity type: 1 = Smart Switch, 2 = Smart Alarm, 3 = Storage Monitor.</summary>
    public int? EntityType { get; set; }

    /// <summary>The entity's Rust+ ID.</summary>
    public int? EntityId { get; set; }

    /// <summary>The entity name as configured in the Rust+ app.</summary>
    public string? EntityName { get; set; }
}
