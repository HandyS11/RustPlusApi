using RustPlusApi.Fcm;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;
using Xunit;

namespace RustPlusApi.Fcm.UnitTests;

/// <summary>Drives <see cref="RustPlusFcm"/>'s notification dispatch directly via a test
/// subclass that exposes the protected <c>ParseNotification</c> hook — no socket needed.</summary>
public class RustPlusFcmDispatchTests
{
    private sealed class TestFcm() : RustPlusFcm(new Credentials
    {
        Gcm = new Gcm
        {
            AndroidId = 1, SecurityToken = 1
        }
    })
    {
        public void Feed(FcmMessage message) => ParseNotification(message);
    }

    private static FcmMessage Pairing(Body body) =>
        new()
        {
            Data = new MessageData
            {
                ChannelId = "pairing", Body = body
            }
        };

    [Fact]
    public void Pairing_Server_RaisesOnServerPairing()
    {
        using var fcm = new TestFcm();
        Notification<ServerEvent?>? captured = null;
        fcm.OnServerPairing += (_, n) => captured = n;

        var serverId = Guid.NewGuid();
        fcm.Feed(Pairing(new Body
        {
            Type = "server",
            Id = serverId,
            Ip = "1.2.3.4",
            Port = 28083,
            EntityName = "My Server",
            Desc = "a desc",
            Logo = "logo-url",
            Img = "img-url",
            Url = "site-url",
            PlayerId = 7,
            PlayerToken = "9"
        }));

        Assert.NotNull(captured);
        // Notification envelope context (BuildGenericOutput).
        Assert.Equal(7ul, captured!.PlayerId);
        Assert.Equal(9, captured.PlayerToken);
        Assert.Equal(serverId, captured.ServerId);
        // Mapped ServerEvent payload (ToServerEvent).
        var server = captured.Data!;
        Assert.Equal(serverId, server.Id);
        Assert.Equal("1.2.3.4", server.Ip);
        Assert.Equal(28083, server.Port);
        Assert.Equal("My Server", server.Name);
        Assert.Equal("a desc", server.Desc);
        Assert.Equal("logo-url", server.Logo);
        Assert.Equal("img-url", server.Img);
        Assert.Equal("site-url", server.Url);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Pairing_Entity_RaisesTypedEntityEvents(int entityType)
    {
        using var fcm = new TestFcm();
        Notification<int?>? smart = null;
        Notification<int?>? alarm = null;
        Notification<int?>? storage = null;
        fcm.OnSmartSwitchPairing += (_, n) => smart = n;
        fcm.OnSmartAlarmPairing += (_, n) => alarm = n;
        fcm.OnStorageMonitorPairing += (_, n) => storage = n;
        Notification<EntityEvent?>? entity = null;
        fcm.OnEntityPairing += (_, n) => entity = n;

        var serverId = Guid.NewGuid();
        fcm.Feed(Pairing(new Body
        {
            Type = "entity",
            Id = serverId,
            EntityType = entityType,
            EntityId = 42,
            EntityName = "switch-1",
            PlayerId = 11,
            PlayerToken = "5"
        }));

        // OnEntityPairing always fires with the mapped EntityEvent + envelope context.
        Assert.NotNull(entity);
        Assert.Equal(11ul, entity!.PlayerId);
        Assert.Equal(5, entity.PlayerToken);
        Assert.Equal(serverId, entity.ServerId);
        Assert.Equal(entityType, entity.Data!.EntityType);
        Assert.Equal(42, entity.Data.EntityId);
        Assert.Equal("switch-1", entity.Data.EntityName);

        // Exactly one typed event fires, carrying the entity id (42) as its payload.
        var fired = new[]
        {
            smart, alarm, storage
        };
        var raised = Assert.Single(fired, n => n is not null);
        Assert.Equal(42, raised!.Data);
        Assert.Equal(serverId, raised.ServerId);
        Assert.Equal(11ul, raised.PlayerId);

        Assert.Equal(entityType == 1, smart is not null);
        Assert.Equal(entityType == 2, alarm is not null);
        Assert.Equal(entityType == 3, storage is not null);
    }

    [Fact]
    public void Pairing_RaisesOnPairing()
    {
        using var fcm = new TestFcm();
        FcmMessage? raised = null;
        fcm.OnPairing += (_, m) => raised = m;
        var message = Pairing(new Body
        {
            Type = "server", Ip = "1.1.1.1", Port = 1, PlayerToken = "0"
        });
        fcm.Feed(message);
        // OnPairing forwards the original FcmMessage instance unchanged.
        Assert.Same(message, raised);
    }

    [Fact]
    public void Alarm_RaisesOnAlarmTriggered()
    {
        using var fcm = new TestFcm();
        AlarmEvent? captured = null;
        fcm.OnAlarmTriggered += (_, e) => captured = e;

        fcm.Feed(new FcmMessage
        {
            Data = new MessageData
            {
                ChannelId = "alarm", Title = "the title", Message = "the message"
            }
        });

        Assert.NotNull(captured);
        Assert.Equal("the title", captured!.Title);
        Assert.Equal("the message", captured.Message);
    }

    [Fact]
    public void UnknownChannel_RaisesNothing()
    {
        using var fcm = new TestFcm();
        var any = false;
        fcm.OnPairing += (_, _) => any = true;
        fcm.OnAlarmTriggered += (_, _) => any = true;
        fcm.Feed(new FcmMessage
        {
            Data = new MessageData
            {
                ChannelId = "mystery"
            }
        });
        Assert.False(any);
    }

    [Fact]
    public void UnknownPairingType_AndUnknownEntityType_RaiseNoTypedEvent()
    {
        using var fcm = new TestFcm();
        var any = false;
        fcm.OnServerPairing += (_, _) => any = true;
        fcm.OnSmartSwitchPairing += (_, _) => any = true;
        fcm.Feed(Pairing(new Body
        {
            Type = "mystery"
        }));
        fcm.Feed(Pairing(new Body
        {
            Type = "entity", EntityType = 99, PlayerToken = "0"
        }));
        Assert.False(any);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Pairing_Entity_NoHandlers_DoesNotRaise(int entityType)
    {
        using var fcm = new TestFcm();
        var raised = false;
        // Subscribe only OnEntityPairing to confirm the entity arm fires, but the typed events are null.
        fcm.OnEntityPairing += (_, _) => raised = true;
        fcm.Feed(Pairing(new Body
        {
            Type = "entity", EntityType = entityType, EntityId = 1, PlayerToken = "0"
        }));
        Assert.True(raised);
    }

    [Fact]
    public void Alarm_NoHandler_DoesNotRaiseOtherEvents()
    {
        using var fcm = new TestFcm();
        var pairingRaised = false;
        fcm.OnPairing += (_, _) => pairingRaised = true;
        // OnAlarmTriggered not subscribed — verifies null branch of ?.Invoke.
        fcm.Feed(new FcmMessage
        {
            Data = new MessageData
            {
                ChannelId = "alarm", Title = "t", Message = "m"
            }
        });
        Assert.False(pairingRaised);
    }

    [Fact]
    public void Pairing_Server_NoServerPairingHandler_OnlyRaisesOnPairing()
    {
        using var fcm = new TestFcm();
        var pairingRaised = false;
        fcm.OnPairing += (_, _) => pairingRaised = true;
        // OnServerPairing not subscribed — verifies null branch of its ?.Invoke.
        fcm.Feed(Pairing(new Body
        {
            Type = "server", Ip = "1.2.3.4", Port = 1, PlayerToken = "0"
        }));
        Assert.True(pairingRaised);
    }
}
