using RustPlusApi.Fcm.Data;
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
        Assert.Equal(1, ev.EntityType);
        Assert.Equal(42, ev.EntityId);
        Assert.Equal("Switch", ev.EntityName);
        Assert.Equal(42, body.ToEntityId());
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
    public void ToAlarmEvent_MapsTitleAndMessage()
    {
        var data = new MessageData
        {
            Title = "Base attacked", Message = "Door opened"
        };
        var ev = data.ToAlarmEvent();
        Assert.Equal("Base attacked", ev.Title);
        Assert.Equal("Door opened", ev.Message);
    }
}
