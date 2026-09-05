namespace RustPlusApi.CredentialsWeb.Sessions;

/// <summary>One server-sent event. <paramref name="Type"/> becomes the SSE <c>event:</c> name and
/// <paramref name="Data"/> is serialized to JSON for the <c>data:</c> line.</summary>
/// <param name="Type">One of <c>step</c>, <c>credentials</c>, <c>paired</c>, <c>error</c>, <c>expired</c>.</param>
/// <param name="Data">Payload, or <see langword="null"/> for an event that carries none.</param>
internal sealed record SessionEvent(string Type, object? Data);
