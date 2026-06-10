using RustPlusApi.Data.Clans;

namespace RustPlusApi.Data.Events;

/// <summary>Event argument raised when the clan snapshot changes.</summary>
public sealed record ClanChangedEventArg
{
    /// <summary>The updated clan snapshot, or <see langword="null"/> if the clan was dissolved.</summary>
    public ClanInfo? ClanInfo { get; init; }
}
