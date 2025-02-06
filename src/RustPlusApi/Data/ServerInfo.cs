namespace RustPlusApi.Data;

public class ServerInfo
{
    public string? Name { get; set; }
    public string? HeaderImage { get; set; }
    public string? Url { get; set; }
    public string? Map { get; set; }
    public uint? MapSize { get; set; }
    public DateTime? WipeTime { get; set; }
    public uint? PlayerCount { get; set; }
    public uint? MaxPlayerCount { get; set; }
    public uint? QueuedPlayerCount { get; set; }
    public uint? Seed { get; set; }
    public uint? Salt { get; set; }
    public string? LogoImage { get; set; }
    public string? Nexus { get; set; }
    public string? NexusZone { get; set; }
}
