namespace RustPlusApi.Data.Clans;

public sealed record ClanInvite
{
    public ulong SteamId { get; init; }
    public ulong Recruiter { get; init; }
    public DateTime Timestamp { get; init; }
}
