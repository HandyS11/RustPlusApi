namespace RustPlusApi.Data.Clans;

public sealed record ClanMember
{
    public ulong SteamId { get; init; }
    public int RoleId { get; init; }
    public DateTime Joined { get; init; }
    public DateTime LastSeen { get; init; }
    public string? Notes { get; init; }
    public bool? Online { get; init; }
}
