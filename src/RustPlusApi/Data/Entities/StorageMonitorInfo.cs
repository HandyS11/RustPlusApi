namespace RustPlusApi.Data.Entities;

public class StorageMonitorInfo
{
    public int? Capacity { get; set; }
    public bool? HasProtection { get; set; }
    public DateTime ProtectionExpiry { get; set; }
    public IEnumerable<StorageMonitorItemInfo>? Items { get; set; }
}
