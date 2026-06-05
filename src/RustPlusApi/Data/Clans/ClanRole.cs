namespace RustPlusApi.Data.Clans;

public sealed record ClanRole
{
    public int RoleId { get; init; }
    public int Rank { get; init; }
    public string Name { get; init; } = null!;
    public bool CanSetMotd { get; init; }
    public bool CanSetLogo { get; init; }
    public bool CanInvite { get; init; }
    public bool CanKick { get; init; }
    public bool CanPromote { get; init; }
    public bool CanDemote { get; init; }
    public bool CanSetPlayerNotes { get; init; }
    public bool CanAccessLogs { get; init; }
}
