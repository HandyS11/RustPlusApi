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
    bool UseFacepunchProxy = false);
