namespace RustPlusApi.Data.Entities;

/// <summary>State of a binary-state smart device (a smart switch or a smart alarm).</summary>
public record SmartDeviceInfo
{
    /// <summary><see langword="true"/> if the device is currently on / triggered.</summary>
    public bool IsActive { get; init; }
}
