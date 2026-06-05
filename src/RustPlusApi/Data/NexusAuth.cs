namespace RustPlusApi.Data;

public sealed record NexusAuth
{
    public string ServerId { get; init; } = null!;
    public int PlayerToken { get; init; }
}
