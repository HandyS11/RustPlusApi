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
public sealed record RustPlusConnection(
    string Server,
    int Port,
    ulong PlayerId,
    int PlayerToken,
    bool UseFacepunchProxy = false)
{
    /// <summary>
    /// Redacts <see cref="PlayerToken"/> (a credential) from the record's string form so it cannot leak
    /// through logs, exceptions, or string interpolation. Equality is unaffected — the generated
    /// <c>Equals</c>/<c>GetHashCode</c> still use every member, including the token.
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
            .Append(", ").Append(nameof(UseFacepunchProxy)).Append(" = ").Append(UseFacepunchProxy);
        return true;
    }
}
