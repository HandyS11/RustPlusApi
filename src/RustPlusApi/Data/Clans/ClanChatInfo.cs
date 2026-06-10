namespace RustPlusApi.Data.Clans;

/// <summary>Clan chat history returned by <c>GetClanChatAsync</c>.</summary>
public sealed record ClanChatInfo
{
    /// <summary>Recent clan chat messages, ordered oldest-first.</summary>
    public IEnumerable<ClanMessage>? Messages { get; init; }
}
