using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;
using RustPlusApi.Fcm.Extensions;
using Xunit;

namespace RustPlusApi.Fcm.UnitTests;

/// <summary>Locks the FCM body/message-data → event-model mappers.</summary>
public class FcmExtensionMapperTests
{
    [Fact]
    public void ToEntityEvent_MapsTypeIdName()
    {
        var body = new Body
        {
            EntityType = 1, EntityId = 42, EntityName = "Switch"
        };
        var ev = body.ToEntityEvent();
        Assert.Equal(EntityType.Switch, ev.EntityType);
        Assert.Equal(42UL, ev.EntityId);
        Assert.Equal("Switch", ev.EntityName);
        Assert.Equal(42UL, body.ToEntityId());
    }

    [Fact]
    public void ToEntityEvent_NullEntityType_StaysNull()
    {
        // A server-pairing body carries no entity fields; the nullable enum cast must preserve null.
        var body = new Body
        {
            EntityType = null, EntityId = null, EntityName = null
        };
        var ev = body.ToEntityEvent();
        Assert.Null(ev.EntityType);
        Assert.Null(ev.EntityId);
        Assert.Null(ev.EntityName);
        Assert.Null(body.ToEntityId());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(99)]
    public void ToEntityEvent_UnknownEntityType_MapsToNull(int entityType)
    {
        // An out-of-range numeric type must map to null instead of an undefined enum member, so
        // consumers that switch over the entity type never see a value outside the known set.
        var body = new Body
        {
            EntityType = entityType, EntityId = 42, EntityName = "x"
        };
        var ev = body.ToEntityEvent();
        Assert.Null(ev.EntityType);
        Assert.Equal(42UL, ev.EntityId);
    }

    [Fact]
    public void ToServerEvent_NullName_BecomesEmpty()
    {
        var body = new Body
        {
            Id = Guid.Empty, Ip = "1.2.3.4", Port = 28083
        };
        var ev = body.ToServerEvent();
        Assert.Equal(string.Empty, ev.Name);
        Assert.Equal("1.2.3.4", ev.Ip);
        Assert.Equal(28083, ev.Port);
    }

    [Fact]
    public void ToServerEvent_UsesServerName_NotEntityName()
    {
        // Server-pairing bodies carry the server name in "name"; "entityName" is only set
        // for entity pairings, so it must never leak into the server event.
        var body = new Body
        {
            Name = "My Server", EntityName = "Switch", Ip = "1.2.3.4", Port = 1
        };
        Assert.Equal("My Server", body.ToServerEvent().Name);
    }

    [Fact]
    public void ToAlarmEvent_MapsServerIdTitleAndMessage()
    {
        var serverId = Guid.Parse("52d121e8-9d14-4dc5-928a-84aa531cfc9e");
        var data = new MessageData
        {
            Title = "Base attacked",
            Message = "Door opened",
            Body = new Body
            {
                Id = serverId
            }
        };
        var ev = data.ToAlarmEvent();
        Assert.Equal(serverId, ev.ServerId);
        Assert.Equal("Base attacked", ev.Title);
        Assert.Equal("Door opened", ev.Message);
    }
}
