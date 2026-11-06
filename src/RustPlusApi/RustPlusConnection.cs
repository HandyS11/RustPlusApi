using System.Globalization;
using System.Text;

namespace RustPlusApi;

/// <summary>
/// What a <see cref="RustPlus"/> client connects as: the player every request is issued as, plus the route
/// to the server, chosen by the concrete type.
/// <list type="bullet">
/// <item><see cref="DirectConnection"/> connects to the server's app port.</item>
/// <item><see cref="CompanionConnection"/> connects through Facepunch's companion socket, the route the
/// official Rust+ app uses as Facepunch phases out direct connections.</item>
/// </list>
/// </summary>
/// <remarks>Only those two types derive from this one; <see cref="RustPlus"/> rejects any other.</remarks>
public abstract record RustPlusConnection
{
    /// <summary>Initializes the player identity shared by every route.</summary>
    /// <param name="playerId">Your Steam ID.</param>
    /// <param name="playerToken">Your player token, from the server pairing.</param>
    private protected RustPlusConnection(ulong playerId, int playerToken)
    {
        PlayerId = playerId;
        PlayerToken = playerToken;
    }

    /// <summary>Your Steam ID.</summary>
    public ulong PlayerId { get; }

    /// <summary>Your player token, from the server pairing. A credential: <see cref="object.ToString"/>
    /// prints it as <c>***</c>.</summary>
    public int PlayerToken { get; }

    /// <summary>
    /// Redacts <see cref="PlayerToken"/> (a credential) from the record's string form so it cannot leak
    /// through logs, exceptions, or string interpolation. Equality is unaffected: the generated
    /// <c>Equals</c>/<c>GetHashCode</c> still use every member, including the tokens.
    /// </summary>
    /// <param name="builder">The builder the record's <c>ToString</c> writes its members into.</param>
    /// <returns><see langword="true"/> so the record renders its members between braces.</returns>
    protected virtual bool PrintMembers(StringBuilder builder)
    {
        builder
            .Append(nameof(PlayerId)).Append(" = ").Append(PlayerId.ToString(CultureInfo.InvariantCulture))
            .Append(", ").Append(nameof(PlayerToken)).Append(" = ***");
        return true;
    }
}
