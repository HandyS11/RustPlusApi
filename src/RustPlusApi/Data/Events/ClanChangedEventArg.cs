using RustPlusApi.Data.Clans;

namespace RustPlusApi.Data.Events;

public sealed record ClanChangedEventArg
{
    public ClanInfo? ClanInfo { get; init; }
}
