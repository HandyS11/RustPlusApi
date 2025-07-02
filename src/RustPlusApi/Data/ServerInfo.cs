namespace RustPlusApi.Data;

public sealed record ServerInfo
{
    public string? Name { get; init; }
    public string? HeaderImage { get; init; }
    public string? Url { get; init; }
    public string? Map { get; init; }
    public uint? MapSize { get; init; }
    public DateTime? WipeTime { get; init; }
    public uint? PlayerCount { get; init; }
    public uint? MaxPlayerCount { get; init; }
    public uint? QueuedPlayerCount { get; init; }
    public uint? Seed { get; init; }
    public uint? Salt { get; init; }
    public string? LogoImage { get; init; }
    public string? Nexus { get; init; }
    public string? NexusZone { get; init; }
}
