namespace RustPlusApi.Data;

public sealed record MemberInfo
{
    public ulong SteamId { get; init; }
    public string? Name { get; init; }
    public float X { get; init; }
    public float Y { get; init; }
    public bool IsOnline { get; init; }
    public DateTime LastSpawnTime { get; init; }
    public bool IsAlive { get; init; }
    public DateTime LastDeathTime { get; init; }
}
