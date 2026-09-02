namespace RustPlusApi.Fcm.Registration;

/// <summary>
/// The Steam identity captured from the Facepunch login redirect — the auth token needed to
/// register the device with Rust Companion, plus the Steam64 ID of the account that signed in.
/// </summary>
/// <param name="SteamId">The Steam64 ID of the account that signed in. Surfaces again later as
/// <see cref="ServerPairing.PlayerId"/> once a server is paired.</param>
/// <param name="Token">The Rust+ auth token handed back by the Facepunch login. Must not be
/// null, empty, or whitespace-only — enforced on construction and on <c>with</c>.</param>
public sealed record SteamLoginResult(ulong SteamId, string Token)
{
    private readonly string _token = ValidateToken(Token);

    /// <summary>The Rust+ auth token handed back by the Facepunch login.</summary>
    /// <exception cref="ArgumentException">Thrown when assigned a null, empty, or
    /// whitespace-only value.</exception>
    public string Token
    {
        get => _token;
        init => _token = ValidateToken(value);
    }

    private static string ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("The token must not be blank.", nameof(token));
        }

        return token;
    }
}
