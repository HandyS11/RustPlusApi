namespace RustPlusApi.Data.Entities;

/// <summary>A single item slot reported by a storage monitor.</summary>
public sealed record StorageMonitorItemInfo
{
    /// <summary>Rust item definition ID.</summary>
    public int Id { get; init; }

    /// <summary>Stack quantity.</summary>
    public int? Quantity { get; init; }

    /// <summary><see langword="true"/> if the item is a blueprint.</summary>
    public bool? IsItemBlueprint { get; init; }
}
