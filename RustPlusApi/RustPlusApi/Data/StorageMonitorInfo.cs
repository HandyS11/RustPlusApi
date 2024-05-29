namespace RustPlusApi.Data
{
    public class StorageMonitorInfo
    {
        public int? Capacity { get; set; }
        public bool? HasProtection { get; set; }
        public uint? ProtectionExpiry { get; set; }
        public List<StorageMonitorItemInfo>? Items { get; set; }
    }
}
