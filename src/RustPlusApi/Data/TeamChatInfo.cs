namespace RustPlusApi.Data;

/// <summary>Team chat history returned by <c>GetTeamChatAsync</c>.</summary>
public sealed record TeamChatInfo
{
    /// <summary>Recent team chat messages, ordered oldest-first.</summary>
    public IEnumerable<TeamMessage>? Messages { get; init; }
}
