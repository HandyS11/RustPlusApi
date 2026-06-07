using System.Drawing;

namespace RustPlusApi.Data;

/// <summary>A single message in the team chat.</summary>
public record TeamMessage
{
    /// <summary>Steam64 ID of the sender.</summary>
    public ulong SteamId { get; init; }

    /// <summary>Display name of the sender at the time the message was posted.</summary>
    public string Name { get; init; } = null!;

    /// <summary>Body of the team chat message.</summary>
    public string Message { get; init; } = null!;

    /// <summary>Name colour of the sender.</summary>
    public Color Color { get; init; }

    /// <summary>UTC timestamp when the message was sent.</summary>
    public DateTime Time { get; init; }
}
