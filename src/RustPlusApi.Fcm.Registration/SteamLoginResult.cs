namespace RustPlusApi.Fcm.Registration;

/// <summary>
/// The Steam identity captured from the Facepunch login redirect — the auth token needed to
/// register the device with Rust Companion, plus the Steam64 ID of the account that signed in.
/// </summary>
public sealed record SteamLoginResult
{
    /// <summary>The Steam64 ID of the account that signed in. Surfaces again later as
    /// <see cref="ServerPairing.PlayerId"/> once a server is paired.</summary>
    public ulong SteamId { get; init; }

    /// <summary>The Rust+ auth token handed back by the Facepunch login.</summary>
    public string Token { get; init; } = null!;
}
