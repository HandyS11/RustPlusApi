using System.Text.Json.Nodes;

namespace RustPlusApi.Fcm.Registration.Steps.Cdp;

/// <summary>A DevTools Protocol event: a message from the browser that answers no command.</summary>
/// <param name="Method">The event name, e.g. <c>Fetch.requestPaused</c>.</param>
/// <param name="Params">The event's <c>params</c> object, or <see langword="null"/> when it carried none.</param>
internal readonly record struct CdpEvent(string Method, JsonNode? Params);
