namespace RustPlusApi.Data.Clans;

/// <summary>A single message in the clan chat.</summary>
public record ClanMessage
{
    /// <summary>Steam64 ID of the sender.</summary>
    public ulong SteamId { get; init; }

    /// <summary>Display name of the sender at the time the message was posted.</summary>
    public string Name { get; init; } = null!;

    /// <summary>Body of the clan chat message.</summary>
    public string Message { get; init; } = null!;

    /// <summary>UTC timestamp when the message was sent.</summary>
    public DateTime Time { get; init; }
}
