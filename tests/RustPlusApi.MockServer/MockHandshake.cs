namespace RustPlusApi.MockServer;

/// <summary>What a client sent in the WebSocket upgrade request the mock server accepted.</summary>
/// <param name="Path">The request path, e.g. <c>/</c> for a direct connection or <c>/socket/{id}</c>
/// for the companion socket.</param>
/// <param name="SubProtocols">The <c>Sec-WebSocket-Protocol</c> values the client offered, in order.</param>
public sealed record MockHandshake(string Path, IReadOnlyList<string> SubProtocols);
