namespace RustPlusApi.Data;

/// <summary>Server metadata returned by <c>GetInfoAsync</c>.</summary>
public sealed record ServerInfo
{
    /// <summary>Display name of the server.</summary>
    public string? Name { get; init; }

    /// <summary>URL of the server's header/banner image.</summary>
    public string? HeaderImage { get; init; }

    /// <summary>URL of the server's website or information page.</summary>
    public string? Url { get; init; }

    /// <summary>Map procedural seed name (e.g. <c>Barren</c>, <c>HapisIsland</c>, or the seed number).</summary>
    public string? Map { get; init; }

    /// <summary>Size of the map in game units.</summary>
    public uint? MapSize { get; init; }

    /// <summary>UTC time of the last forced wipe.</summary>
    public DateTime? WipeTime { get; init; }

    /// <summary>Number of players currently connected.</summary>
    public uint? PlayerCount { get; init; }

    /// <summary>Maximum number of players the server allows.</summary>
    public uint? MaxPlayerCount { get; init; }

    /// <summary>Number of players currently in the connection queue.</summary>
    public uint? QueuedPlayerCount { get; init; }

    /// <summary>Procedural map generation seed.</summary>
    public uint? Seed { get; init; }

    /// <summary>Procedural map generation salt.</summary>
    public uint? Salt { get; init; }

    /// <summary>URL of the server's logo image.</summary>
    public string? LogoImage { get; init; }

    /// <summary>Nexus cluster identifier, if the server participates in a Nexus.</summary>
    public string? Nexus { get; init; }

    /// <summary>Zone identifier within the Nexus cluster.</summary>
    public string? NexusZone { get; init; }
}
