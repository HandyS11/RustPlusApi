namespace RustPlusApi.Data
{
    public class StorageMonitorInfo
    {
        public int? Capacity { get; set; }
        public bool? HasProtection { get; set; }
        public DateTime ProtectionExpiry { get; set; }
        public List<StorageMonitorItemInfo>? Items { get; set; }
    }
}
