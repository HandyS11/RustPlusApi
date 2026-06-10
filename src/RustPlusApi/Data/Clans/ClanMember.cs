namespace RustPlusApi.Data.Clans;

/// <summary>A member of a clan.</summary>
public sealed record ClanMember
{
    /// <summary>Steam64 ID of the member.</summary>
    public ulong SteamId { get; init; }

    /// <summary>ID of the role assigned to this member (see <see cref="ClanRole.RoleId"/>).</summary>
    public int RoleId { get; init; }

    /// <summary>UTC timestamp when the member joined the clan.</summary>
    public DateTime Joined { get; init; }

    /// <summary>UTC timestamp when the member was last seen online.</summary>
    public DateTime LastSeen { get; init; }

    /// <summary>Officer notes attached to this member, if any.</summary>
    public string? Notes { get; init; }

    /// <summary><see langword="true"/> if the member is currently online.</summary>
    public bool? Online { get; init; }
}
