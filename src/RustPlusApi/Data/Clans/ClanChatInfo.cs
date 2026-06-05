namespace RustPlusApi.Data.Clans;

public sealed record ClanChatInfo
{
    public IEnumerable<ClanMessage>? Messages { get; init; }
}
