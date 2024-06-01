using RustPlusApi.Data;
using RustPlusApi.Data.Notes;

using RustPlusContracts;

using static RustPlusContracts.AppTeamInfo.Types;

namespace RustPlusApi.Extensions
{
    public static class AppTeamInfoToModel
    {
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

        public static IEnumerable<MemberInfo> ToMemberInfos(this IEnumerable<Member> members)
        {
            return members.Select(ToMemberInfo);
        }

        public static DeathNote ToDeathNote(this AppTeamInfo.Types.Note note)
        {
            return new DeathNote
            {
                X = note.X,
                Y = note.Y,
            };
        }

        public static PlayerNote ToPlayerNote(this AppTeamInfo.Types.Note note)
        {
            return new PlayerNote
            {
                X = note.X,
                Y = note.Y,
                Icon = (NoteIcons)note.Icon,
                Color = (NoteColors)note.Colour,
                Text = note.Name
            };
        }

        public static IEnumerable<PlayerNote> ToPlayerNotes(this IEnumerable<AppTeamInfo.Types.Note> notes)
        {
            return notes.Select(ToPlayerNote);
        }
    }
}
