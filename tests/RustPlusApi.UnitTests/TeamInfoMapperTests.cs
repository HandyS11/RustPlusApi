using RustPlusApi.Data.Notes;
using RustPlusApi.Extensions;
using RustPlusContracts;
using Xunit;

namespace RustPlusApi.UnitTests;

/// <summary>Locks team-info mapping: member projection, the death/player note fork, the
/// leader-notes path, and the unknown-note-type throw.</summary>
public class TeamInfoMapperTests
{
    [Fact]
    public void ToTeamInfo_MapsMembers_DeathNote_PlayerNotes_AndLeaderNotes()
    {
        var info = new AppTeamInfo
        {
            LeaderSteamId = 76561198000000001,
            Members = { new AppTeamInfo.Member
            {
                SteamId = 76561198000000001, Name = "Leader", X = 10, Y = 20,
                IsOnline = true, SpawnTime = 1_600_000_000, IsAlive = true, DeathTime = 1_600_000_500
            }},
            MapNotes =
            {
                new AppTeamInfo.Note { Type = 0, X = 1, Y = 2 },
                new AppTeamInfo.Note { Type = 1, X = 3, Y = 4, Icon = 2, ColourIndex = 1, Label = "base" }
            },
            LeaderMapNotes = { new AppTeamInfo.Note { Type = 1, X = 5, Y = 6, Icon = 0, ColourIndex = 0, Label = "rally" } }
        };

        var model = info.ToTeamInfo();

        Assert.Equal(76561198000000001ul, model.LeaderSteamId);
        var member = Assert.Single(model.Members!);
        Assert.Equal("Leader", member.Name);
        Assert.True(member.IsOnline);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_600_000_000).UtcDateTime, member.LastSpawnTime);
        Assert.NotNull(model.DeathNote);
        var note = Assert.Single(model.Notes!);
        Assert.Equal("base", note.Text);
        Assert.Equal(NoteIcons.Home, note.Icon);
        Assert.Equal(NoteColors.Blue, note.Color);
        Assert.Single(model.LeaderNotes!);
    }

    [Fact]
    public void ToTeamInfo_UnknownNoteType_Throws()
    {
        var info = new AppTeamInfo { MapNotes = { new AppTeamInfo.Note { Type = 99 } } };
        Assert.Throws<ArgumentException>(info.ToTeamInfo);
    }

    [Fact]
    public void ToTeamInfo_NoDeathNote_LeavesDeathNoteNull()
    {
        var info = new AppTeamInfo { MapNotes = { new AppTeamInfo.Note { Type = 1, Label = "x" } } };
        Assert.Null(info.ToTeamInfo().DeathNote);
    }
}
