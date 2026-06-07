namespace RustPlusApi.Data.Clans;

/// <summary>Full clan snapshot returned by <c>GetClanInfoAsync</c>.</summary>
public sealed record ClanInfo
{
    /// <summary>Unique identifier of the clan.</summary>
    public long ClanId { get; init; }

    /// <summary>Display name of the clan.</summary>
    public string Name { get; init; } = null!;

    /// <summary>UTC timestamp when the clan was created.</summary>
    public DateTime Created { get; init; }

    /// <summary>Steam64 ID of the clan creator.</summary>
    public ulong Creator { get; init; }

    /// <summary>Message of the day, if set.</summary>
    public string? Motd { get; init; }

    /// <summary>UTC timestamp when the MOTD was last changed.</summary>
    public DateTime? MotdTimestamp { get; init; }

    /// <summary>Steam64 ID of the player who last changed the MOTD.</summary>
    public ulong? MotdAuthor { get; init; }

    /// <summary>Raw bytes of the clan logo image, if set.</summary>
    public byte[]? Logo { get; init; }

    /// <summary>Clan colour as a packed ARGB integer, if set.</summary>
    public int? Color { get; init; }

    /// <summary>Roles defined in this clan.</summary>
    public IEnumerable<ClanRole>? Roles { get; init; }

    /// <summary>Current members of the clan.</summary>
    public IEnumerable<ClanMember>? Members { get; init; }

    /// <summary>Pending invitations to the clan.</summary>
    public IEnumerable<ClanInvite>? Invites { get; init; }

    /// <summary>Maximum number of members allowed in this clan, if capped.</summary>
    public int? MaxMemberCount { get; init; }
}
