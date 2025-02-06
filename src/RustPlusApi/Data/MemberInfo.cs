namespace RustPlusApi.Data;

public class MemberInfo
{
    public ulong SteamId { get; set; }
    public string? Name { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public bool IsOnline { get; set; }
    public DateTime LastSpawnTime { get; set; }
    public bool IsAlive { get; set; }
    public DateTime LastDeathTime { get; set; }
}
