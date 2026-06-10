namespace RustPlusApi.Data.Entities;

/// <summary>State of a storage monitor entity.</summary>
public record StorageMonitorInfo
{
    /// <summary>Total item capacity of the monitored container.</summary>
    public int? Capacity { get; init; }

    /// <summary><see langword="true"/> if the container has a tool-cupboard protection active.</summary>
    public bool? HasProtection { get; init; }

    /// <summary>UTC time when the TC protection expires.</summary>
    public DateTime ProtectionExpiry { get; init; }

    /// <summary>Items currently stored in the container.</summary>
    public IEnumerable<StorageMonitorItemInfo>? Items { get; init; }
}
