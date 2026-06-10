using RustPlusApi.Data.Clans;
using RustPlusApi.Data.Events;
using AppClanChanged = RustPlusContracts.AppClanChanged;
using AppClanInfo = RustPlusContracts.AppClanInfo;
using ProtoClanInfo = RustPlusContracts.ClanInfo;
// ReSharper disable MemberCanBePrivate.Global

namespace RustPlusApi.Extensions;

/// <summary>Mapping extensions from protobuf clan-info messages to clan model types.</summary>
public static class AppClanInfoToModel
{
    /// <summary>Maps an <see cref="AppClanInfo"/> response to a <see cref="ClanInfo"/>.</summary>
    /// <param name="appClanInfo">The protobuf clan info response.</param>
    public static ClanInfo? ToClanInfo(this AppClanInfo appClanInfo)
    {
        return appClanInfo.ClanInfo.ToClanInfo();
    }

    /// <summary>Maps a <see cref="ProtoClanInfo"/> to a <see cref="ClanInfo"/>, or <see langword="null"/> if the source is <see langword="null"/>.</summary>
    /// <param name="clanInfo">The protobuf clan info, or <see langword="null"/>.</param>
    public static ClanInfo? ToClanInfo(this ProtoClanInfo? clanInfo)
    {
        if (clanInfo is null)
        {
            return null;
        }

        return new ClanInfo
        {
            ClanId = clanInfo.ClanId,
            Name = clanInfo.Name,
            Created = DateTimeOffset.FromUnixTimeSeconds(clanInfo.Created).UtcDateTime,
            Creator = clanInfo.Creator,
            Motd = clanInfo.ShouldSerializeMotd() ? clanInfo.Motd : null,
            MotdTimestamp = clanInfo.ShouldSerializeMotdTimestamp()
                ? DateTimeOffset.FromUnixTimeSeconds(clanInfo.MotdTimestamp).UtcDateTime
                : null,
            MotdAuthor = clanInfo.ShouldSerializeMotdAuthor() ? clanInfo.MotdAuthor : null,
            Logo = clanInfo.ShouldSerializeLogo() ? clanInfo.Logo : null,
            Color = clanInfo.ShouldSerializeColor() ? clanInfo.Color : null,
            Roles = clanInfo.Roles.ToClanRoles(),
            Members = clanInfo.Members.ToClanMembers(),
            Invites = clanInfo.Invites.ToClanInvites(),
            MaxMemberCount = clanInfo.ShouldSerializeMaxMemberCount() ? clanInfo.MaxMemberCount : null,
            Score = clanInfo.ShouldSerializeScore() ? clanInfo.Score : null
        };
    }

    /// <summary>Maps an <see cref="AppClanChanged"/> broadcast to a <see cref="ClanChangedEventArg"/>.</summary>
    /// <param name="appClanChanged">The protobuf clan-changed broadcast.</param>
    public static ClanChangedEventArg ToClanChangedEvent(this AppClanChanged appClanChanged)
    {
        return new ClanChangedEventArg
        {
            ClanInfo = appClanChanged.ClanInfo.ToClanInfo()
        };
    }

    /// <summary>Maps a single protobuf clan role to a <see cref="ClanRole"/>.</summary>
    /// <param name="role">The protobuf clan role.</param>
    public static ClanRole ToClanRole(this ProtoClanInfo.Role role)
    {
        return new ClanRole
        {
            RoleId = role.RoleId,
            Rank = role.Rank,
            Name = role.Name,
            CanSetMotd = role.CanSetMotd,
            CanSetLogo = role.CanSetLogo,
            CanInvite = role.CanInvite,
            CanKick = role.CanKick,
            CanPromote = role.CanPromote,
            CanDemote = role.CanDemote,
            CanSetPlayerNotes = role.CanSetPlayerNotes,
            CanAccessLogs = role.CanAccessLogs,
            CanAccessScoreEvents = role.ShouldSerializeCanAccessScoreEvents() && role.CanAccessScoreEvents
        };
    }

    /// <summary>Maps a sequence of protobuf clan roles to <see cref="ClanRole"/> instances.</summary>
    /// <param name="roles">The protobuf clan roles to map.</param>
    public static IEnumerable<ClanRole> ToClanRoles(this IEnumerable<ProtoClanInfo.Role> roles)
    {
        return roles.Select(ToClanRole);
    }

    /// <summary>Maps a single protobuf clan member to a <see cref="ClanMember"/>.</summary>
    /// <param name="member">The protobuf clan member.</param>
    public static ClanMember ToClanMember(this ProtoClanInfo.Member member)
    {
        return new ClanMember
        {
            SteamId = member.SteamId,
            RoleId = member.RoleId,
            Joined = DateTimeOffset.FromUnixTimeSeconds(member.Joined).UtcDateTime,
            LastSeen = DateTimeOffset.FromUnixTimeSeconds(member.LastSeen).UtcDateTime,
            Notes = member.ShouldSerializeNotes() ? member.Notes : null,
            Online = member.ShouldSerializeOnline() ? member.Online : null
        };
    }

    /// <summary>Maps a sequence of protobuf clan members to <see cref="ClanMember"/> instances.</summary>
    /// <param name="members">The protobuf clan members to map.</param>
    public static IEnumerable<ClanMember> ToClanMembers(this IEnumerable<ProtoClanInfo.Member> members)
    {
        return members.Select(ToClanMember);
    }

    /// <summary>Maps a single protobuf clan invite to a <see cref="ClanInvite"/>.</summary>
    /// <param name="invite">The protobuf clan invite.</param>
    public static ClanInvite ToClanInvite(this ProtoClanInfo.Invite invite)
    {
        return new ClanInvite
        {
            SteamId = invite.SteamId,
            Recruiter = invite.Recruiter,
            Timestamp = DateTimeOffset.FromUnixTimeSeconds(invite.Timestamp).UtcDateTime
        };
    }

    /// <summary>Maps a sequence of protobuf clan invites to <see cref="ClanInvite"/> instances.</summary>
    /// <param name="invites">The protobuf clan invites to map.</param>
    public static IEnumerable<ClanInvite> ToClanInvites(this IEnumerable<ProtoClanInfo.Invite> invites)
    {
        return invites.Select(ToClanInvite);
    }
}
