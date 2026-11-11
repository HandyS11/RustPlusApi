using System.Text;

namespace RustPlusApi;

/// <summary>
/// A connection through Facepunch's companion socket, <c>wss://companion-rust.facepunch.com/socket/{ServerId}</c>:
/// the route the official Rust+ app uses as Facepunch phases out direct connections to a server's app port.
/// </summary>
/// <remarks>
/// <see cref="ServerId"/> comes from the server pairing notification (<c>ServerPairing.ServerId</c>), and
/// <see cref="AuthToken"/> from the Steam login (<c>SteamLoginResult.Token</c>, returned by
/// <c>FcmRegistration.RegisterWithRustPlusAsync</c>). The token expires 14 days after that login.
/// </remarks>
public sealed record CompanionConnection : RustPlusConnection
{
    /// <summary>Initializes a new instance of the <see cref="CompanionConnection"/> class.</summary>
    /// <param name="serverId">Facepunch's id for the server, from the pairing notification.</param>
    /// <param name="authToken">The Rust+ companion auth token from the Steam login.</param>
    /// <param name="playerId">Your Steam ID.</param>
    /// <param name="playerToken">Your player token, from the server pairing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="authToken"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="serverId"/> is <see cref="Guid.Empty"/>, or
    /// <paramref name="authToken"/> is empty or whitespace: the companion socket would refuse the handshake
    /// with a bare 404 or 403.</exception>
    public CompanionConnection(Guid serverId, string authToken, ulong playerId, int playerToken)
        : base(playerId, playerToken)
    {
        if (serverId == Guid.Empty)
        {
            throw new ArgumentException(
                "The server id is Guid.Empty; pass the server id from the pairing notification.",
                nameof(serverId));
        }

#pragma warning disable CA1510 // ArgumentNullException.ThrowIfNull is not available on netstandard2.0
        if (authToken is null)
        {
            throw new ArgumentNullException(nameof(authToken));
        }
#pragma warning restore CA1510

        if (string.IsNullOrWhiteSpace(authToken))
        {
            throw new ArgumentException(
                "The auth token is blank; pass the Rust+ auth token from the Steam login.",
                nameof(authToken));
        }

        ServerId = serverId;
        AuthToken = authToken;
    }

    /// <summary>Facepunch's id for the server: the <c>id</c> field of the server's pairing notification.</summary>
    public Guid ServerId { get; }

    /// <summary>The Rust+ companion auth token from the Steam login. A credential: <see cref="object.ToString"/>
    /// prints it as <c>***</c>, and it is only ever sent to Facepunch's companion host.</summary>
    public string AuthToken { get; }

    /// <summary>Prints the player identity (token redacted), the server id and a redacted
    /// <see cref="AuthToken"/>.</summary>
    /// <param name="builder">The builder the record's <c>ToString</c> writes its members into.</param>
    /// <returns><see langword="true"/> so the record renders its members between braces.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        base.PrintMembers(builder);
        builder
            .Append(", ").Append(nameof(ServerId)).Append(" = ").Append(ServerId.ToString("D"))
            .Append(", ").Append(nameof(AuthToken)).Append(" = ***");
        return true;
    }
}
