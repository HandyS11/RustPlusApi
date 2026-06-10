using RustPlusApi.Data.Notes;

namespace RustPlusApi.Data;

/// <summary>Full team snapshot returned by <c>GetTeamInfoAsync</c>.</summary>
public sealed record TeamInfo
{
    /// <summary>Steam64 ID of the current team leader.</summary>
    public ulong LeaderSteamId { get; init; }

    /// <summary>Status snapshots for all team members.</summary>
    public IEnumerable<MemberInfo>? Members { get; init; }

    /// <summary>The leader's death note on the map, if one is set.</summary>
    public DeathNote? DeathNote { get; init; }

    /// <summary>Map notes placed by all team members.</summary>
    public IEnumerable<PlayerNote>? Notes { get; init; }

    /// <summary>Map notes placed by the team leader.</summary>
    public IEnumerable<PlayerNote>? LeaderNotes { get; init; }
}
