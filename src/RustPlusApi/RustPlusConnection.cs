using System.Globalization;
using System.Text;

namespace RustPlusApi;

/// <summary>
/// Connection identity for a <see cref="RustPlus"/> client: the server endpoint and the player
/// credentials a request is issued as. Grouping these into one value keeps the
/// <see cref="RustPlus"/> constructor readable at the call site.
/// </summary>
/// <param name="Server">The IP address of the Rust+ server.</param>
/// <param name="Port">The port dedicated for the Rust+ companion app (not the one used to connect in-game).</param>
/// <param name="PlayerId">Your Steam ID.</param>
/// <param name="PlayerToken">Your player token acquired with FCM.</param>
/// <param name="UseFacepunchProxy">Specifies whether to use the Facepunch proxy.</param>
/// <remarks>
/// Setting <see cref="ServerId"/> (with <see cref="AuthToken"/>) routes the connection through
/// Facepunch's companion socket, <c>wss://companion-rust.facepunch.com/socket/{ServerId}</c>, instead of
/// <see cref="Server"/>:<see cref="Port"/>. That is the route the official Rust+ app uses since direct
/// connections to a server's app port were removed; <see cref="Server"/> and <see cref="Port"/> are then
/// not used to connect.
/// </remarks>
public sealed record RustPlusConnection(
    string Server,
    int Port,
    ulong PlayerId,
    int PlayerToken,
    bool UseFacepunchProxy = false)
{
    /// <summary>
    /// Facepunch's id for the server — the <c>id</c> field of the server's pairing notification.
    /// When set, the client connects through the companion socket for this server and takes precedence
    /// over <see cref="UseFacepunchProxy"/>.
    /// </summary>
    public Guid? ServerId { get; init; }

    /// <summary>
    /// The Rust+ companion auth token (the <c>Token</c> returned by Facepunch's Steam login, also used to
    /// register for push notifications). Required by the companion socket when <see cref="ServerId"/> is
    /// set; ignored otherwise.
    /// </summary>
    public string? AuthToken { get; init; }

    /// <summary>
    /// Redacts <see cref="PlayerToken"/> and <see cref="AuthToken"/> (credentials) from the record's string
    /// form so they cannot leak through logs, exceptions, or string interpolation. Equality is unaffected —
    /// the generated <c>Equals</c>/<c>GetHashCode</c> still use every member, including the tokens.
    /// </summary>
    /// <param name="builder">The builder the record's <c>ToString</c> writes its members into.</param>
    /// <returns><see langword="true"/> so the record renders its members between braces.</returns>
    private bool PrintMembers(StringBuilder builder)
    {
        builder
            .Append(nameof(Server)).Append(" = ").Append(Server)
            .Append(", ").Append(nameof(Port)).Append(" = ").Append(Port.ToString(CultureInfo.InvariantCulture))
            .Append(", ").Append(nameof(PlayerId)).Append(" = ").Append(PlayerId.ToString(CultureInfo.InvariantCulture))
            .Append(", ").Append(nameof(PlayerToken)).Append(" = ***")
            .Append(", ").Append(nameof(UseFacepunchProxy)).Append(" = ").Append(UseFacepunchProxy)
            .Append(", ").Append(nameof(ServerId)).Append(" = ").Append(ServerId)
            .Append(", ").Append(nameof(AuthToken)).Append(" = ").Append(AuthToken is null ? string.Empty : "***");
        return true;
    }
}
