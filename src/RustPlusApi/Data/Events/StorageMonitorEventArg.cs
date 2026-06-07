using RustPlusApi.Data.Entities;

namespace RustPlusApi.Data.Events;

/// <summary>Event argument raised when a subscribed storage monitor reports a change.</summary>
public sealed record StorageMonitorEventArg : StorageMonitorInfo
{
    /// <summary>Entity ID of the storage monitor that changed.</summary>
    public uint Id { get; init; }
}
