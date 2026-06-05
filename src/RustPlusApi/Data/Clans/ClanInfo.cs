namespace RustPlusApi.Data.Clans;

public sealed record ClanInfo
{
    public long ClanId { get; init; }
    public string Name { get; init; } = null!;
    public DateTime Created { get; init; }
    public ulong Creator { get; init; }
    public string? Motd { get; init; }
    public DateTime? MotdTimestamp { get; init; }
    public ulong? MotdAuthor { get; init; }
    public byte[]? Logo { get; init; }
    public int? Color { get; init; }
    public IEnumerable<ClanRole>? Roles { get; init; }
    public IEnumerable<ClanMember>? Members { get; init; }
    public IEnumerable<ClanInvite>? Invites { get; init; }
    public int? MaxMemberCount { get; init; }
}
