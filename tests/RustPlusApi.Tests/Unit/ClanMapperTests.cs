using RustPlusApi.Extensions;
using RustPlusApi.MockServer;
using Xunit;

namespace RustPlusApi.Tests.Unit;

/// <summary>
/// Guards the clan/nexus mappers lifted from the old <c>RustPlusLegacy</c> raw API into the
/// typed <c>Response&lt;T&gt;</c> surface.
/// </summary>
public class ClanMapperTests
{
    [Fact]
    public void ToClanInfo_MapsScalarsAndConvertsTimes()
    {
        var proto = MockResponses.SampleClanInfo();

        var model = proto.ToClanInfo();

        Assert.NotNull(model);
        Assert.Equal(4242, model!.ClanId);
        Assert.Equal("Mock Clan", model.Name);
        Assert.Equal("Welcome to the mock clan", model.Motd);
        Assert.Equal(50, model.MaxMemberCount);
        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(1_600_000_000).UtcDateTime,
            model.Created);
    }

    [Fact]
    public void ToClanInfo_MapsMembers()
    {
        var model = MockResponses.SampleClanInfo().ToClanInfo();

        var member = Assert.Single(model!.Members!);
        Assert.Equal(76561198000000001ul, member.SteamId);
        Assert.True(member.Online);
    }

    [Fact]
    public void ToClanInfo_WhenInnerClanMissing_ReturnsNull()
    {
        var empty = new RustPlusContracts.AppClanInfo();

        Assert.Null(empty.ToClanInfo());
    }

    [Fact]
    public void ToClanChatInfo_MapsMessages()
    {
        var model = MockResponses.SampleClanChat().ToClanChatInfo();

        var message = Assert.Single(model.Messages!);
        Assert.Equal("Tester", message.Name);
        Assert.Equal("clan chat fixture", message.Message);
    }

    [Fact]
    public void ToNexusAuth_MapsFields()
    {
        var model = MockResponses.SampleNexusAuth().ToNexusAuth();

        Assert.Equal("mock-server-id", model.ServerId);
        Assert.Equal(987654321, model.PlayerToken);
    }
}
