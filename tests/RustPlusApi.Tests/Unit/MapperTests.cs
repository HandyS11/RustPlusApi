using RustPlusApi.Extensions;
using RustPlusApi.MockServer;
using Xunit;

namespace RustPlusApi.Tests.Unit;

/// <summary>
/// Guards the <c>Extensions/*</c> mappers that lift wire types into the typed
/// <c>Response&lt;T&gt;</c> models (the presence-sensitive mapping is the part
/// the protobuf-net swap must not break).
/// </summary>
public class MapperTests
{
    [Fact]
    public void ToServerInfo_MapsEveryField()
    {
        var info = MockResponses.SampleInfo();

        var model = info.ToServerInfo();

        Assert.Equal(info.Name, model.Name);
        Assert.Equal(info.HeaderImage, model.HeaderImage);
        Assert.Equal(info.Url, model.Url);
        Assert.Equal(info.Map, model.Map);
        Assert.Equal(info.MapSize, model.MapSize);
        Assert.Equal(info.Players, model.PlayerCount);
        Assert.Equal(info.MaxPlayers, model.MaxPlayerCount);
        Assert.Equal(info.QueuedPlayers, model.QueuedPlayerCount);
        Assert.Equal(info.Seed, model.Seed);
        Assert.Equal(info.Salt, model.Salt);
        Assert.Equal(info.LogoImage, model.LogoImage);
    }

    [Fact]
    public void ToServerInfo_ConvertsWipeTimeFromUnixSeconds()
    {
        var info = MockResponses.SampleInfo();

        var model = info.ToServerInfo();

        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(info.WipeTime).UtcDateTime,
            model.WipeTime);
    }

    [Fact]
    public void ToTimeInfo_MapsEveryField()
    {
        var time = MockResponses.SampleTime();

        var model = time.ToTimeInfo();

        Assert.Equal(time.DayLengthMinutes, model.DayLengthMinutes);
        Assert.Equal(time.TimeScale, model.TimeScale);
        Assert.Equal(time.Sunrise, model.Sunrise);
        Assert.Equal(time.Sunset, model.Sunset);
        Assert.Equal(time.Time, model.Time);
    }
}
