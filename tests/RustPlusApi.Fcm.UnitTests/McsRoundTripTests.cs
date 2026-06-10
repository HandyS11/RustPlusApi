using McsProto;
using ProtoBuf;
using RustPlusApi.Fcm.Data;
using Xunit;
using static RustPlusApi.Fcm.Utils.McsUtils;

namespace RustPlusApi.Fcm.UnitTests;

/// <summary>
/// Guards the hand-authored code-first MCS contracts: the [ProtoMember] field
/// numbers are the wire contract, so these round-trips ensure the manual types stay
/// byte-compatible with the Chromium MCS protocol.
/// </summary>
public class McsRoundTripTests
{
    private static T RoundTrip<T>(T value)
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, value);
        stream.Position = 0;
        return Serializer.Deserialize<T>(stream);
    }

    [Fact]
    public void HeartbeatPing_RoundTrips()
    {
        var result = RoundTrip(new HeartbeatPing
        {
            StreamId = 5,
            LastStreamIdReceived = 4,
            Status = 1
        });

        Assert.Equal(5, result.StreamId);
        Assert.Equal(4, result.LastStreamIdReceived);
        Assert.Equal(1, result.Status);
    }

    [Fact]
    public void HeartbeatPing_UnsetOptionals_StayNull()
    {
        var result = RoundTrip(new HeartbeatPing());

        Assert.Null(result.StreamId);
        Assert.Null(result.LastStreamIdReceived);
        Assert.Null(result.Status);
    }

    [Fact]
    public void LoginRequest_RoundTrips_IncludingEnumAndCollections()
    {
        var request = new LoginRequest
        {
            Id = "chrome-63",
            Domain = "mcs.android.com",
            User = "42",
            Resource = "42",
            AuthToken = "secret",
            auth_service = LoginRequest.AuthService.AndroidId,
            UseRmq2 = true,
            Settings =
            {
                new Setting
                {
                    Name = "new_vc",
                    Value = "1"
                }
            },
            ReceivedPersistentIds =
            {
                "abc",
                "def"
            }
        };

        var result = RoundTrip(request);

        Assert.Equal("chrome-63", result.Id);
        Assert.Equal("secret", result.AuthToken);
        Assert.Equal(LoginRequest.AuthService.AndroidId, result.auth_service);
        Assert.True(result.UseRmq2);
        var setting = Assert.Single(result.Settings);
        Assert.Equal("new_vc", setting.Name);
        Assert.Equal((string[])["abc", "def"], result.ReceivedPersistentIds);
    }

    [Fact]
    public void DataMessageStanza_RoundTrips_WithAppData()
    {
        var stanza = new DataMessageStanza
        {
            From = "123456789",
            PersistentId = "persist-1",
            Sent = 1_700_000_000_000,
            AppDatas =
            {
                new AppData
                {
                    Key = "channelId",
                    Value = "rust"
                },
                new AppData
                {
                    Key = "body",
                    Value = "{}"
                }
            }
        };

        var result = RoundTrip(stanza);

        Assert.Equal("123456789", result.From);
        Assert.Equal("persist-1", result.PersistentId);
        Assert.Equal(1_700_000_000_000, result.Sent);
        Assert.Equal(2, result.AppDatas.Count);
        Assert.Equal("channelId", result.AppDatas[0].Key);
        Assert.Equal("body", result.AppDatas[1].Key);
    }

    [Fact]
    public void Utils_TagMapping_IsBidirectional()
    {
        foreach (var type in new[]
                 {
                     typeof(HeartbeatPing), typeof(HeartbeatAck), typeof(LoginRequest), typeof(LoginResponse),
                     typeof(Close), typeof(IqStanza), typeof(DataMessageStanza), typeof(StreamErrorStanza)
                 })
        {
            var tag = GetTagFromProtobufType(type);
            Assert.Equal(type, BuildProtobufFromTag(tag));
        }
    }
}
