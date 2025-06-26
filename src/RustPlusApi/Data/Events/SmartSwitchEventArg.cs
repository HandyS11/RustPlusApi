using RustPlusApi.Data.Entities;

namespace RustPlusApi.Data.Events;

public sealed record SmartSwitchEventArg : SmartSwitchInfo
{
    public uint Id { get; init; }
}
