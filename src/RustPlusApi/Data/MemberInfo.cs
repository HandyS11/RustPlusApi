namespace RustPlusApi.Data;

/// <summary>Snapshot of a single team member's status.</summary>
public sealed record MemberInfo
{
    /// <summary>Steam64 ID of the member.</summary>
    public ulong SteamId { get; init; }

    /// <summary>In-game display name of the member.</summary>
    public string? Name { get; init; }

    /// <summary>Horizontal map coordinate (west → east).</summary>
    public float X { get; init; }

    /// <summary>Vertical map coordinate (south → north).</summary>
    public float Y { get; init; }

    /// <summary><see langword="true"/> if the member is currently connected to the server.</summary>
    public bool IsOnline { get; init; }

    /// <summary>UTC time of the member's last spawn.</summary>
    public DateTime LastSpawnTime { get; init; }

    /// <summary><see langword="true"/> if the member is currently alive.</summary>
    public bool IsAlive { get; init; }

    /// <summary>UTC time of the member's last death.</summary>
    public DateTime LastDeathTime { get; init; }
}
