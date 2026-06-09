namespace RustPlusApi.Fcm.Data.Events;

/// <summary>Describes a Rust game server received in a server-pairing FCM notification.</summary>
public sealed record ServerEvent
{
    /// <summary>The server's unique Rust+ ID.</summary>
    public Guid Id { get; init; }

    /// <summary>Display name of the server.</summary>
    public string Name { get; init; } = null!;

    /// <summary>IP address or hostname of the Rust+ companion port.</summary>
    public string Ip { get; init; } = null!;

    /// <summary>Rust+ companion port number.</summary>
    public int Port { get; init; }

    /// <summary>Optional server description.</summary>
    public string? Desc { get; init; }

    /// <summary>Optional URL to the server's logo image.</summary>
    public string? Logo { get; init; }

    /// <summary>Optional URL to a server banner image.</summary>
    public string? Img { get; init; }

    /// <summary>Optional URL to the server's website.</summary>
    public string? Url { get; init; }
}
