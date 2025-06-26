using RustPlusApi.Data.Entities;

namespace RustPlusApi.Data.Events;

public sealed record StorageMonitorEventArg : StorageMonitorInfo
{
    public uint Id { get; init; }
}
