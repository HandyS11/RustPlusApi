using RustPlusApi.Data.Clans;

namespace RustPlusApi.Data.Events;

/// <summary>Event argument raised when a new clan chat message arrives.</summary>
public sealed record ClanMessageEventArg : ClanMessage
{
    /// <summary>Identifier of the clan the message was posted in.</summary>
    public long ClanId { get; init; }
}
