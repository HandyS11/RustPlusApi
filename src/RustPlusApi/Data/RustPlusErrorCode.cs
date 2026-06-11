namespace RustPlusApi.Data;

/// <summary>
/// Machine-readable Rust+ server error identifiers, parsed from the raw error string so consumers
/// can branch on failure type without string comparisons. The raw identifier always remains
/// available on <see cref="ErrorMessage.Message"/>; unrecognized identifiers map to <see cref="Unknown"/>.
/// The numeric values are part of the public contract: append new members, never renumber.
/// </summary>
public enum RustPlusErrorCode
{
    /// <summary>The server returned an identifier this library does not recognize (or none at all);
    /// inspect <see cref="ErrorMessage.Message"/> for the raw value.</summary>
    Unknown = 0,

    /// <summary><c>server_error</c> — the server failed to process the request.</summary>
    ServerError = 1,

    /// <summary><c>banned</c> — the player is banned from the server.</summary>
    Banned = 2,

    /// <summary><c>rate_limit</c> — the request was throttled; retry later.</summary>
    RateLimit = 3,

    /// <summary><c>not_found</c> — the requested entity or resource does not exist.</summary>
    NotFound = 4,

    /// <summary><c>wrong_type</c> — the entity exists but is not of the requested kind.</summary>
    WrongType = 5,

    /// <summary><c>no_team</c> — the player is not in a team.</summary>
    NoTeam = 6,

    /// <summary><c>no_clan</c> — the player is not in a clan.</summary>
    NoClan = 7,

    /// <summary><c>no_map</c> — the server has no map image available.</summary>
    NoMap = 8,

    /// <summary><c>access_denied</c> — the player token does not grant access to this resource.</summary>
    AccessDenied = 9,

    /// <summary><c>message_not_sent</c> — the team chat message was rejected.</summary>
    MessageNotSent = 10,

    /// <summary><c>too_many_subscribers</c> — the entity has reached its subscription limit.</summary>
    TooManySubscribers = 11,

    /// <summary><c>not_enabled</c> — the requested feature is disabled on this server.</summary>
    NotEnabled = 12,

    /// <summary><c>no_player</c> — the server has no player entity for the paired account,
    /// e.g. the character was killed while away (observed on camera subscribe attempts:
    /// cameras are accessed while disconnected, but need the character to still exist).</summary>
    NoPlayer = 13,
}
