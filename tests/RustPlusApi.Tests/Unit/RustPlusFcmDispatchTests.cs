using RustPlusApi.Fcm;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;
using Xunit;

namespace RustPlusApi.Tests.Unit;

/// <summary>Drives <see cref="RustPlusFcm"/>'s notification dispatch directly via a test
/// subclass that exposes the protected <c>ParseNotification</c> hook — no socket needed.</summary>
public class RustPlusFcmDispatchTests
{
    private sealed class TestFcm() : RustPlusFcm(new Credentials { Gcm = new Gcm { AndroidId = 1, SecurityToken = 1 } })
    {
        public void Feed(FcmMessage message) => ParseNotification(message);
    }

    private static FcmMessage Pairing(Body body) =>
        new() { Data = new MessageData { ChannelId = "pairing", Body = body } };

    [Fact]
    public void Pairing_Server_RaisesOnServerPairing()
    {
        using var fcm = new TestFcm();
        Notification<ServerEvent?>? captured = null;
        fcm.OnServerPairing += (_, n) => captured = n;

        fcm.Feed(Pairing(new Body { Type = "server", Ip = "1.2.3.4", Port = 28083, PlayerId = 7, PlayerToken = "9" }));

        Assert.NotNull(captured);
        Assert.Equal("1.2.3.4", captured!.Data!.Ip);
        Assert.Equal(7ul, captured.PlayerId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Pairing_Entity_RaisesTypedEntityEvents(int entityType)
    {
        using var fcm = new TestFcm();
        var smart = false; var alarm = false; var storage = false;
        fcm.OnSmartSwitchParing += (_, _) => smart = true;
        fcm.OnSmartAlarmParing += (_, _) => alarm = true;
        fcm.OnStorageMonitorParing += (_, _) => storage = true;
        var entity = false;
        fcm.OnEntityParing += (_, _) => entity = true;

        fcm.Feed(Pairing(new Body { Type = "entity", EntityType = entityType, EntityId = 42, PlayerToken = "0" }));

        Assert.True(entity);
        Assert.Equal(entityType == 1, smart);
        Assert.Equal(entityType == 2, alarm);
        Assert.Equal(entityType == 3, storage);
    }

    [Fact]
    public void Pairing_RaisesOnParing()
    {
        using var fcm = new TestFcm();
        var raised = false;
        fcm.OnParing += (_, _) => raised = true;
        fcm.Feed(Pairing(new Body { Type = "server", Ip = "1.1.1.1", Port = 1, PlayerToken = "0" }));
        Assert.True(raised);
    }

    [Fact]
    public void Alarm_RaisesOnAlarmTriggered()
    {
        using var fcm = new TestFcm();
        AlarmEvent? captured = null;
        fcm.OnAlarmTriggered += (_, e) => captured = e;

        fcm.Feed(new FcmMessage { Data = new MessageData { ChannelId = "alarm", Title = "t", Message = "m" } });

        Assert.NotNull(captured);
        Assert.Equal("t", captured!.Title);
    }

    [Fact]
    public void UnknownChannel_RaisesNothing()
    {
        using var fcm = new TestFcm();
        var any = false;
        fcm.OnParing += (_, _) => any = true;
        fcm.OnAlarmTriggered += (_, _) => any = true;
        fcm.Feed(new FcmMessage { Data = new MessageData { ChannelId = "mystery" } });
        Assert.False(any);
    }

    [Fact]
    public void UnknownPairingType_AndUnknownEntityType_RaiseNoTypedEvent()
    {
        using var fcm = new TestFcm();
        var any = false;
        fcm.OnServerPairing += (_, _) => any = true;
        fcm.OnSmartSwitchParing += (_, _) => any = true;
        fcm.Feed(Pairing(new Body { Type = "mystery" }));
        fcm.Feed(Pairing(new Body { Type = "entity", EntityType = 99, PlayerToken = "0" }));
        Assert.False(any);
    }
}
