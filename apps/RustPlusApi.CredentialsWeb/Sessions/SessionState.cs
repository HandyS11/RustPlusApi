namespace RustPlusApi.CredentialsWeb.Sessions;

/// <summary>Where a visitor is in the credential flow.</summary>
internal enum SessionState
{
    /// <summary>Created; the visitor has not completed the Steam login yet. Nothing upstream touched.</summary>
    Created = 0,

    /// <summary>The Facepunch callback arrived with a usable Steam token.</summary>
    Authenticated = 1,

    /// <summary>Steps 1-3 and 5 are running.</summary>
    Registering = 2,

    /// <summary>Credentials acquired and registered with Rust Companion.</summary>
    Ready = 3,

    /// <summary>Holding an MCS socket, waiting for the in-game pairing push.</summary>
    AwaitingPairing = 4,

    /// <summary>A pairing push arrived; the flow is complete.</summary>
    Paired = 5,

    /// <summary>A step failed. Terminal.</summary>
    Failed = 6
}

// There is deliberately no Expired state. A pairing wait that times out returns the session to
// Ready and emits an `expired` event: the credentials are still valid, so the visitor can retry
// the pairing without repeating the Steam login. A session that outlives its TTL is removed by
// the sweeper rather than parked in a state nobody can observe.
