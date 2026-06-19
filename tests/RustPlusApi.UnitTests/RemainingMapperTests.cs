using RustPlusApi.Extensions;
using RustPlusApi.MockServer;
using RustPlusContracts;
using Xunit;

namespace RustPlusApi.UnitTests;

/// <summary>Brings the remaining lightweight extension mappers to full coverage.</summary>
public class RemainingMapperTests
{
    [Fact]
    public void ToServerMap_MapsDimensionsAndMonuments()
    {
        var map = MockResponses.SampleMap();
        map.Monuments.Add(new AppMap.Monument
        {
            Token = "cave", X = 1, Y = 2
        });

        var model = map.ToServerMap();

        Assert.Equal(2000u, model.Width);
        Assert.Equal(2000u, model.Height);
        var monument = Assert.Single(model.Monuments!);
        Assert.Equal("cave", monument.Name);
    }

    [Fact]
    public void ToSubscriptionInfo_MapsValue()
    {
        var info = new AppFlag
        {
            Value = true
        }.ToSubscriptionInfo();
        Assert.True(info.IsSubscribed);
    }

    [Fact]
    public void ToSmartSwitchEvent_MapsIdAndActiveState()
    {
        var changed = new AppEntityChanged
        {
            EntityId = 99,
            Payload = new AppEntityPayload
            {
                Value = true
            }
        };

        var ev = changed.ToSmartSwitchEvent();

        Assert.Equal(99u, ev.Id);
        Assert.True(ev.IsActive);
    }

    [Fact]
    public void ToStorageMonitorEvent_MapsCapacityAndItems()
    {
        var changed = new AppEntityChanged
        {
            EntityId = 7,
            Payload = new AppEntityPayload
            {
                Capacity = 24,
                HasProtection = false,
                ProtectionExpiry = 1_700_000_000,
                Items =
                {
                    new AppEntityPayload.Item
                    {
                        ItemId = 1, Quantity = 2
                    }
                }
            }
        };

        var ev = changed.ToStorageMonitorEvent();

        Assert.Equal(7u, ev.Id);
        Assert.Equal(24, ev.Capacity);
        Assert.Single(ev.Items!);
    }

    [Fact]
    public void ToTeamChatInfo_MapsMessages()
    {
        var chat = new AppTeamChat
        {
            Messages =
            {
                new AppTeamMessage
                {
                    SteamId = 1,
                    Name = "Alice",
                    Message = "hello",
                    Color = "#FF0000",
                    Time = 1_700_000_000
                }
            }
        };

        var model = chat.ToTeamChatInfo();

        var msg = Assert.Single(model.Messages!);
        Assert.Equal(1ul, msg.SteamId);
        Assert.Equal("Alice", msg.Name);
        Assert.Equal("hello", msg.Message);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).UtcDateTime, msg.Time);
    }

    [Fact]
    public void ToTeamMessage_MapsColorAndTime()
    {
        var proto = new AppTeamMessage
        {
            SteamId = 2,
            Name = "Bob",
            Message = "hi",
            Color = "#00FF00",
            Time = 1_600_000_000
        };

        var msg = proto.ToTeamMessage();

        Assert.Equal(2ul, msg.SteamId);
        Assert.Equal(255, msg.Color.G);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_600_000_000).UtcDateTime, msg.Time);
    }

    [Fact]
    public void ToTeamMessageEvent_MapsAllFields()
    {
        var proto = new AppTeamMessage
        {
            SteamId = 3,
            Name = "Charlie",
            Message = "wave",
            Color = "#0000FF",
            Time = 1_500_000_000
        };

        var ev = proto.ToTeamMessageEvent();

        Assert.Equal(3ul, ev.SteamId);
        Assert.Equal("Charlie", ev.Name);
        Assert.Equal("wave", ev.Message);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_500_000_000).UtcDateTime, ev.Time);
    }

    [Fact]
    public void ToClanChatInfo_MapsMessages()
    {
        var model = MockResponses.SampleClanChat().ToClanChatInfo();
        var msg = Assert.Single(model.Messages!);
        Assert.Equal("Tester", msg.Name);
        Assert.Equal("clan chat fixture", msg.Message);
    }

    [Fact]
    public void ToClanMessage_MapsFields()
    {
        var proto = new AppClanMessage
        {
            SteamId = 1, Name = "D", Message = "hey", Time = 1_700_000_000
        };
        var msg = proto.ToClanMessage();
        Assert.Equal(1ul, msg.SteamId);
        Assert.Equal("D", msg.Name);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).UtcDateTime, msg.Time);
    }

    [Fact]
    public void ToClanMessageEvent_MapsAllFields()
    {
        var broadcast = new AppNewClanMessage
        {
            ClanId = 42,
            Message = new AppClanMessage
            {
                SteamId = 7, Name = "Eve", Message = "sup", Time = 1_600_000_000
            }
        };

        var ev = broadcast.ToClanMessageEvent();

        Assert.Equal(42, ev.ClanId);
        Assert.Equal(7ul, ev.SteamId);
        Assert.Equal("Eve", ev.Name);
        Assert.Equal("sup", ev.Message);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_600_000_000).UtcDateTime, ev.Time);
    }

    [Fact]
    public void ToTimeInfo_MapsAllFields()
    {
        var time = MockResponses.SampleTime();
        var model = time.ToTimeInfo();
        Assert.Equal(time.DayLengthMinutes, model.DayLengthMinutes);
        Assert.Equal(time.TimeScale, model.TimeScale);
        Assert.Equal(time.Sunrise, model.Sunrise);
        Assert.Equal(time.Sunset, model.Sunset);
        Assert.Equal(time.Time, model.Time);
    }

    [Fact]
    public void ToNexusAuth_MapsFields()
    {
        var model = MockResponses.SampleNexusAuth().ToNexusAuth();
        Assert.Equal("mock-server-id", model.ServerId);
        Assert.Equal(987654321, model.PlayerToken);
    }

    [Fact]
    public void ToServerInfo_MapsNexusFields()
    {
        var info = MockResponses.SampleInfo();
        var model = info.ToServerInfo();
        Assert.Equal(info.Nexus, model.Nexus);
        Assert.Equal(info.NexusId, model.NexusId);
        Assert.Equal(info.NexusZone, model.NexusZone);
    }

    [Fact]
    public void ToServerInfo_AbsentNexusId_IsNull()
    {
        // nexus_id is optional; a non-Nexus server omits it, which must map to null (not 0).
        var info = new AppInfo
        {
            Name = "n", HeaderImage = "h", Url = "u", Map = "m"
        };
        Assert.False(info.ShouldSerializeNexusId());
        Assert.Null(info.ToServerInfo().NexusId);
    }
}
