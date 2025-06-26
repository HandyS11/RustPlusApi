namespace RustPlusApi.Data.Entities;

public record StorageMonitorInfo
{
    public int? Capacity { get; init; }
    public bool? HasProtection { get; init; }
    public DateTime ProtectionExpiry { get; init; }
    public IEnumerable<StorageMonitorItemInfo>? Items { get; init; }
}
