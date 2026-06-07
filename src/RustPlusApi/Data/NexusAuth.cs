namespace RustPlusApi.Data;

/// <summary>Cross-server authentication token returned by <c>GetNexusAuthAsync</c>.</summary>
public sealed record NexusAuth
{
    /// <summary>Identifier of the server within the Nexus cluster.</summary>
    public string ServerId { get; init; } = null!;

    /// <summary>Player authentication token for the target Nexus server.</summary>
    public int PlayerToken { get; init; }
}
