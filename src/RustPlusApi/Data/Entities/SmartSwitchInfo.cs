namespace RustPlusApi.Data.Entities;

/// <summary>State of a smart switch entity.</summary>
public record SmartSwitchInfo
{
    /// <summary><see langword="true"/> if the switch is currently on.</summary>
    public bool IsActive { get; init; }
}
