namespace RustPlusApi.Data.Clans;

/// <summary>A permission role within a clan.</summary>
public sealed record ClanRole
{
    /// <summary>Unique identifier of this role within the clan.</summary>
    public int RoleId { get; init; }

    /// <summary>Rank order; lower values are higher ranks.</summary>
    public int Rank { get; init; }

    /// <summary>Display name of the role.</summary>
    public string Name { get; init; } = null!;

    /// <summary><see langword="true"/> if members with this role can set the clan MOTD.</summary>
    public bool CanSetMotd { get; init; }

    /// <summary><see langword="true"/> if members with this role can set the clan logo.</summary>
    public bool CanSetLogo { get; init; }

    /// <summary><see langword="true"/> if members with this role can invite players.</summary>
    public bool CanInvite { get; init; }

    /// <summary><see langword="true"/> if members with this role can kick other members.</summary>
    public bool CanKick { get; init; }

    /// <summary><see langword="true"/> if members with this role can promote others.</summary>
    public bool CanPromote { get; init; }

    /// <summary><see langword="true"/> if members with this role can demote others.</summary>
    public bool CanDemote { get; init; }

    /// <summary><see langword="true"/> if members with this role can set notes on other members.</summary>
    public bool CanSetPlayerNotes { get; init; }

    /// <summary><see langword="true"/> if members with this role can view clan audit logs.</summary>
    public bool CanAccessLogs { get; init; }
}
