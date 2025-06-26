namespace RustPlusApi.Data;

public sealed record TeamChatInfo
{
    public IEnumerable<TeamMessage>? Messages { get; init; }
}
