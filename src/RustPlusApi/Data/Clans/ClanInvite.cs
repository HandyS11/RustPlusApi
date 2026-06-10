namespace RustPlusApi.Data.Clans;

/// <summary>A pending invitation to join a clan.</summary>
public sealed record ClanInvite
{
    /// <summary>Steam64 ID of the invited player.</summary>
    public ulong SteamId { get; init; }

    /// <summary>Steam64 ID of the clan member who sent the invitation.</summary>
    public ulong Recruiter { get; init; }

    /// <summary>UTC timestamp when the invitation was created.</summary>
    public DateTime Timestamp { get; init; }
}
