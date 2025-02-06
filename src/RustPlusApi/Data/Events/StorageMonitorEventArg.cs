using RustPlusApi.Data.Entities;

namespace RustPlusApi.Data.Events;

public class StorageMonitorEventArg : StorageMonitorInfo
{
    public uint Id { get; set; }
}
