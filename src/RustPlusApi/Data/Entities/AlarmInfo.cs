namespace RustPlusApi.Data.Entities;

/// <summary>State of a smart alarm entity.</summary>
public record AlarmInfo
{
    /// <summary><see langword="true"/> if the alarm is currently triggered / active.</summary>
    public bool IsActive { get; init; }
}
