using RustPlusApi.Data;
using RustPlusApi.Data.Notes;

using RustPlusContracts;

using static RustPlusContracts.AppTeamInfo;
// ReSharper disable MemberCanBePrivate.Global

namespace RustPlusApi.Extensions;

/// <summary>Mapping extensions from protobuf team-info messages to model types.</summary>
public static class AppTeamInfoToModel
{
    /// <summary>Maps an <see cref="AppTeamInfo"/> to a <see cref="TeamInfo"/>, routing map notes to typed models.</summary>
    /// <param name="appTeamInfo">The protobuf team info response.</param>
    /// <exception cref="ArgumentException">Thrown when a map note has an unrecognized type.</exception>
    public static TeamInfo ToTeamInfo(this AppTeamInfo appTeamInfo)
    {
        DeathNote? deathNote = null;
        List<PlayerNote> notes = [];

        foreach (var note in appTeamInfo.MapNotes)
        {
            switch (note.Type)
            {
                case 0:
                    deathNote = note.ToDeathNote();
                    break;
                case 1:
                    notes.Add(note.ToPlayerNote());
                    break;
                default:
                    throw new ArgumentException($"Unknown note type: {note.Type}");
            }
        }

        return new TeamInfo
        {
            LeaderSteamId = appTeamInfo.LeaderSteamId,
            Members = appTeamInfo.Members.ToMemberInfos(),
            DeathNote = deathNote,
            Notes = notes,
            LeaderNotes = appTeamInfo.LeaderMapNotes.ToPlayerNotes()
        };
    }

    /// <summary>Maps a single protobuf team member to a <see cref="MemberInfo"/>.</summary>
    /// <param name="member">The protobuf team member.</param>
    public static MemberInfo ToMemberInfo(this Member member)
    {
        return new MemberInfo
        {
            SteamId = member.SteamId,
            Name = member.Name,
            X = member.X,
            Y = member.Y,
            IsOnline = member.IsOnline,
            LastSpawnTime = DateTimeOffset.FromUnixTimeSeconds(member.SpawnTime).UtcDateTime,
            IsAlive = member.IsAlive,
            LastDeathTime = DateTimeOffset.FromUnixTimeSeconds(member.DeathTime).UtcDateTime
        };
    }

    /// <summary>Maps a sequence of protobuf team members to <see cref="MemberInfo"/> instances.</summary>
    /// <param name="members">The protobuf team members to map.</param>
    public static IEnumerable<MemberInfo> ToMemberInfos(this IEnumerable<Member> members)
    {
        return members.Select(ToMemberInfo);
    }

    /// <summary>Maps a type-0 protobuf note to a <see cref="DeathNote"/>.</summary>
    /// <param name="note">The protobuf map note.</param>
    public static DeathNote ToDeathNote(this AppTeamInfo.Note note)
    {
        return new DeathNote
        {
            X = note.X,
            Y = note.Y,
        };
    }

    /// <summary>Maps a type-1 protobuf note to a <see cref="PlayerNote"/>.</summary>
    /// <param name="note">The protobuf map note.</param>
    public static PlayerNote ToPlayerNote(this AppTeamInfo.Note note)
    {
        return new PlayerNote
        {
            X = note.X,
            Y = note.Y,
            Icon = (NoteIcons)note.Icon,
            Color = (NoteColors)note.ColourIndex,
            Text = note.Label
        };
    }

    /// <summary>Maps a sequence of protobuf notes to <see cref="PlayerNote"/> instances.</summary>
    /// <param name="notes">The protobuf notes to map.</param>
    public static IEnumerable<PlayerNote> ToPlayerNotes(this IEnumerable<AppTeamInfo.Note> notes)
    {
        return notes.Select(ToPlayerNote);
    }
}
