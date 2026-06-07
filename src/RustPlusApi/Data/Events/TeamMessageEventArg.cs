namespace RustPlusApi.Data.Events;

/// <summary>
/// Event argument raised when a new team chat message arrives.
/// Inherits all <see cref="TeamMessage"/> fields.
/// </summary>
public sealed record TeamMessageEventArg : TeamMessage;
