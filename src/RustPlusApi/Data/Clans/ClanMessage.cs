namespace RustPlusApi.Data.Clans;

public record ClanMessage
{
    public ulong SteamId { get; init; }
    public string Name { get; init; } = null!;
    public string Message { get; init; } = null!;
    public DateTime Time { get; init; }
}
