namespace RustPlusApi;

/// <summary>
/// A connection straight to the server's app port, <c>ws://{Server}:{Port}</c>. Facepunch is phasing these
/// out; where a server no longer accepts them, use <see cref="CompanionConnection"/>.
/// </summary>
public sealed record DirectConnection : RustPlusConnection
{
    /// <summary>Initializes a new instance of the <see cref="DirectConnection"/> class.</summary>
    /// <param name="server">The server's IP address.</param>
    /// <param name="port">The Rust+ companion port (<c>app.port</c>), not the one used to connect in game.</param>
    /// <param name="playerId">Your Steam ID.</param>
    /// <param name="playerToken">Your player token, from the server pairing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="server"/> is <see langword="null"/>.</exception>
    public DirectConnection(string server, int port, ulong playerId, int playerToken)
        : base(playerId, playerToken)
    {
        Server = server ?? throw new ArgumentNullException(nameof(server));
        Port = port;
    }

    /// <summary>The server's IP address.</summary>
    public string Server { get; }

    /// <summary>The Rust+ companion port (<c>app.port</c>), not the one used to connect in game.</summary>
    public int Port { get; }
}
