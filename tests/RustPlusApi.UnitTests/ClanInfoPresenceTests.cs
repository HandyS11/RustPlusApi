using RustPlusApi.Extensions;
using RustPlusContracts;
using Xunit;
using ProtoClanInfo = RustPlusContracts.ClanInfo;

namespace RustPlusApi.UnitTests;

/// <summary>Exercises both sides of every presence fork in <see cref="AppClanInfoToModel"/>.</summary>
public class ClanInfoPresenceTests
{
    private static ProtoClanInfo Minimal() => new()
    {
        ClanId = 1, Name = "c", Created = 0, Creator = 0
    };

    [Fact]
    public void ToClanInfo_AllOptionalsUnset_AreNull()
    {
        var model = Minimal().ToClanInfo()!;
        Assert.Null(model.Motd);
        Assert.Null(model.MotdTimestamp);
        Assert.Null(model.MotdAuthor);
        Assert.Null(model.Logo);
        Assert.Null(model.Color);
        Assert.Null(model.MaxMemberCount);
        Assert.Null(model.Score);
    }

    [Fact]
    public void ToClanInfo_AllOptionalsSet_AreMapped()
    {
        var proto = Minimal();
        proto.Motd = "m";
        proto.MotdTimestamp = 1_700_000_000_000;
        proto.MotdAuthor = 5;
        proto.Logo = [1, 2];
        proto.Color = 42;
        proto.MaxMemberCount = 50;
        proto.Score = 99;

        var model = proto.ToClanInfo()!;

        Assert.Equal("m", model.Motd);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000).UtcDateTime, model.MotdTimestamp);
        Assert.Equal(5ul, model.MotdAuthor);
        Assert.Equal(42, model.Color);
        Assert.Equal(50, model.MaxMemberCount);
        Assert.Equal(99, model.Score);
    }

    [Fact]
    public void ToClanRole_CanAccessScoreEvents_RespectsPresenceAndValue()
    {
        var setTrue = new ProtoClanInfo.Role
        {
            RoleId = 1, Name = "r", CanAccessScoreEvents = true
        };
        var unset = new ProtoClanInfo.Role
        {
            RoleId = 2, Name = "r"
        };
        Assert.True(setTrue.ToClanRole().CanAccessScoreEvents);
        Assert.False(unset.ToClanRole().CanAccessScoreEvents);
    }

    [Fact]
    public void ToClanMember_OptionalNotesAndOnline_RespectPresence()
    {
        var withOpts = new ProtoClanInfo.Member
        {
            SteamId = 1, Notes = "hi", Online = true
        };
        var without = new ProtoClanInfo.Member
        {
            SteamId = 2
        };
        Assert.Equal("hi", withOpts.ToClanMember().Notes);
        Assert.True(withOpts.ToClanMember().Online);
        Assert.Null(without.ToClanMember().Notes);
        Assert.Null(without.ToClanMember().Online);
    }

    [Fact]
    public void ToClanInvite_MapsFieldsAndTimestamp()
    {
        var invite = new ProtoClanInfo.Invite
        {
            SteamId = 1, Recruiter = 2, Timestamp = 1_700_000_000_000
        };
        var model = invite.ToClanInvite();
        Assert.Equal(1ul, model.SteamId);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000).UtcDateTime, model.Timestamp);
    }

    [Fact]
    public void ToClanChangedEvent_WrapsClanInfo()
    {
        var changed = new AppClanChanged
        {
            ClanInfo = Minimal()
        };
        Assert.Equal(1, changed.ToClanChangedEvent().ClanInfo!.ClanId);
    }
}
