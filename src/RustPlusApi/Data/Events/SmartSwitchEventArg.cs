using RustPlusApi.Data.Entities;

namespace RustPlusApi.Data.Events;

/// <summary>Event argument raised when a subscribed smart switch changes state.</summary>
public sealed record SmartSwitchEventArg : SmartSwitchInfo
{
    /// <summary>Entity ID of the smart switch that changed.</summary>
    public ulong Id { get; init; }
}
