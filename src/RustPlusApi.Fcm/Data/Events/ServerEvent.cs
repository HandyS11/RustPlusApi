namespace RustPlusApi.Fcm.Data.Events;

/// <summary>Describes a Rust game server received in a server-pairing FCM notification.</summary>
public sealed record ServerEvent
{
    /// <summary>The server's unique Rust+ ID.</summary>
    public Guid Id { get; set; }

    /// <summary>Display name of the server.</summary>
    public string Name { get; set; } = null!;

    /// <summary>IP address or hostname of the Rust+ companion port.</summary>
    public string Ip { get; set; } = null!;

    /// <summary>Rust+ companion port number.</summary>
    public int Port { get; set; }

    /// <summary>Optional server description.</summary>
    public string? Desc { get; set; }

    /// <summary>Optional URL to the server's logo image.</summary>
    public string? Logo { get; set; }

    /// <summary>Optional URL to a server banner image.</summary>
    public string? Img { get; set; }

    /// <summary>Optional URL to the server's website.</summary>
    public string? Url { get; set; }
}
