namespace RustPlusApi.Data.Events;

/// <summary>Event argument raised when the team snapshot changes.</summary>
public sealed record TeamChangedEventArg
{
    /// <summary>The Steam id of the player whose action triggered the change.</summary>
    public ulong PlayerId { get; init; }

    /// <summary>The updated team snapshot.</summary>
    public TeamInfo? TeamInfo { get; init; }
}
