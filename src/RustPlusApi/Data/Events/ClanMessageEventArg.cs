using RustPlusApi.Data.Clans;

namespace RustPlusApi.Data.Events;

public sealed record ClanMessageEventArg : ClanMessage
{
    public long ClanId { get; init; }
}
