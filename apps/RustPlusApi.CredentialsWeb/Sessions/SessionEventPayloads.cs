namespace RustPlusApi.CredentialsWeb.Sessions;

/// <summary>Payload of a <c>step</c> event.</summary>
/// <param name="State">The new <see cref="SessionState"/>, as its enum name.</param>
internal sealed record StepPayload(string State);

/// <summary>Payload of a <c>credentials</c> event.</summary>
/// <param name="SteamId">Steam64 as a string: it exceeds JavaScript's safe integer range.</param>
/// <param name="ConfigJson">The exact contents of rustplus.config.json, from <c>CredentialsStore.Serialize</c>.</param>
/// <param name="AuthToken">The Steam login's Rust+ auth token — what the companion socket authenticates with
/// (<c>RustPlusConnection.AuthToken</c>). Not part of rustplus.config.json, so it is handed over beside it.</param>
internal sealed record CredentialsPayload(string SteamId, string ConfigJson, string AuthToken);

/// <summary>Payload of a <c>paired</c> event.</summary>
/// <param name="Ip">Server address.</param>
/// <param name="Port">Server app port.</param>
/// <param name="PlayerId">Steam64 as a string, for the same reason as <see cref="CredentialsPayload.SteamId"/>.</param>
/// <param name="PlayerToken">The pairing token. Full Rust+ account access.</param>
/// <param name="Name">Server name, when the push carried one.</param>
/// <param name="ServerId">Facepunch's server id (<c>RustPlusConnection.ServerId</c>), as a hyphenated GUID.</param>
internal sealed record PairedPayload(
    string Ip,
    int Port,
    string PlayerId,
    int PlayerToken,
    string? Name,
    string ServerId);

/// <summary>Payload of an <c>error</c> event. Always a fixed, non-reflective message: never an
/// exception message, which could carry upstream response content.</summary>
/// <param name="Message">What to show the visitor.</param>
internal sealed record ErrorPayload(string Message);
