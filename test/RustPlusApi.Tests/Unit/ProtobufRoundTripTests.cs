using ProtoBuf;
using RustPlusApi.MockServer;
using RustPlusContracts;
using Xunit;

namespace RustPlusApi.Tests.Unit;

/// <summary>
/// Guards the Google.Protobuf → protobuf-net serializer swap (v2 §2): the contract types must
/// round-trip through protobuf-net's binary (field-number) encoding without losing data.
/// </summary>
public class ProtobufRoundTripTests
{
    private static T RoundTrip<T>(T value)
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, value);
        stream.Position = 0;
        return Serializer.Deserialize<T>(stream);
    }

    [Fact]
    public void AppRequest_RoundTrips()
    {
        var request = new AppRequest
        {
            Seq = 7,
            PlayerId = 76561198000000000,
            PlayerToken = 123456789,
            EntityId = 4242,
            GetInfo = new AppEmpty()
        };

        var result = RoundTrip(request);

        Assert.Equal(7u, result.Seq);
        Assert.Equal(76561198000000000ul, result.PlayerId);
        Assert.Equal(123456789, result.PlayerToken);
        Assert.Equal(4242u, result.EntityId);
        Assert.NotNull(result.GetInfo);
    }

    [Fact]
    public void AppMessage_WithResponse_RoundTrips()
    {
        var message = new AppMessage { Response = new AppResponse { Seq = 3, Info = MockResponses.SampleInfo() } };

        var result = RoundTrip(message);

        Assert.NotNull(result.Response);
        Assert.Equal(3u, result.Response.Seq);
        Assert.NotNull(result.Response.Info);
        Assert.Equal("Mock Rust Server", result.Response.Info.Name);
        Assert.Null(result.Broadcast);
    }

    [Fact]
    public void AppMessage_WithBroadcast_RoundTrips()
    {
        var message = new AppMessage { Broadcast = MockResponses.TeamMessageBroadcast(1, "Tester", "hi") };

        var result = RoundTrip(message);

        Assert.NotNull(result.Broadcast);
        Assert.NotNull(result.Broadcast.TeamMessage);
        Assert.Equal("Tester", result.Broadcast.TeamMessage.Message.Name);
        Assert.Null(result.Response);
    }

    [Fact]
    public void OptionalScalarPresence_IsTrackedViaShouldSerialize()
    {
        var withMotd = new ClanInfo { ClanId = 1, Name = "c", Created = 0, Creator = 0, Motd = "hi" };
        var withoutMotd = new ClanInfo { ClanId = 1, Name = "c", Created = 0, Creator = 0 };

        Assert.True(RoundTrip(withMotd).ShouldSerializeMotd());
        Assert.False(RoundTrip(withoutMotd).ShouldSerializeMotd());
    }
}
