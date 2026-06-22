using RustPlusApi.Data.Entities;

namespace RustPlusApi.Data.Events;

/// <summary>Event argument raised when a subscribed binary-state smart device
/// (a smart switch or a smart alarm) changes state.</summary>
public sealed record SmartDeviceEventArg : SmartDeviceInfo
{
    /// <summary>Entity ID of the smart device that changed.</summary>
    public ulong Id { get; init; }
}
