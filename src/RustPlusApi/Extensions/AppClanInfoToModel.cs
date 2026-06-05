using RustPlusApi.Data.Clans;
using RustPlusApi.Data.Events;

using AppClanInfo = RustPlusContracts.AppClanInfo;
using AppClanChanged = RustPlusContracts.AppClanChanged;
using ProtoClanInfo = RustPlusContracts.ClanInfo;
// ReSharper disable MemberCanBePrivate.Global

namespace RustPlusApi.Extensions;

public static class AppClanInfoToModel
{
    public static ClanInfo? ToClanInfo(this AppClanInfo appClanInfo)
    {
        return appClanInfo.ClanInfo.ToClanInfo();
    }

    public static ClanInfo? ToClanInfo(this ProtoClanInfo? clanInfo)
    {
        if (clanInfo is null) return null;

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
            MaxMemberCount = clanInfo.ShouldSerializeMaxMemberCount() ? clanInfo.MaxMemberCount : null
        };
    }

    public static ClanChangedEventArg ToClanChangedEvent(this AppClanChanged appClanChanged)
    {
        return new ClanChangedEventArg
        {
            ClanInfo = appClanChanged.ClanInfo.ToClanInfo()
        };
    }

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
            CanAccessLogs = role.CanAccessLogs
        };
    }

    public static IEnumerable<ClanRole> ToClanRoles(this IEnumerable<ProtoClanInfo.Role> roles)
    {
        return roles.Select(ToClanRole);
    }

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

    public static IEnumerable<ClanMember> ToClanMembers(this IEnumerable<ProtoClanInfo.Member> members)
    {
        return members.Select(ToClanMember);
    }

    public static ClanInvite ToClanInvite(this ProtoClanInfo.Invite invite)
    {
        return new ClanInvite
        {
            SteamId = invite.SteamId,
            Recruiter = invite.Recruiter,
            Timestamp = DateTimeOffset.FromUnixTimeSeconds(invite.Timestamp).UtcDateTime
        };
    }

    public static IEnumerable<ClanInvite> ToClanInvites(this IEnumerable<ProtoClanInfo.Invite> invites)
    {
        return invites.Select(ToClanInvite);
    }
}
