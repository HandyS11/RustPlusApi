using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;

namespace RustPlusApi.Fcm.Extensions;

/// <summary>Extension methods that project a <see cref="Body"/> into FCM event model types.</summary>
public static class BodyToEventModel
{
    /// <summary>Extracts the entity ID from the notification body.</summary>
    /// <param name="body">The FCM notification body to read from.</param>
    public static ulong? ToEntityId(this Body body)
    {
        return body.EntityId;
    }

    /// <summary>Maps the notification body to an <see cref="EntityEvent"/>.</summary>
    /// <param name="body">The FCM notification body to map.</param>
    public static EntityEvent ToEntityEvent(this Body body)
    {
        return new EntityEvent
        {
            EntityType = (EntityType?)body.EntityType, EntityId = body.EntityId, EntityName = body.EntityName
        };
    }

    /// <summary>Maps the notification body to a <see cref="ServerEvent"/>.</summary>
    /// <param name="body">The FCM notification body to map.</param>
    public static ServerEvent ToServerEvent(this Body body)
    {
        return new ServerEvent
        {
            Id = body.Id,
            Name = body.Name ?? string.Empty,
            Ip = body.Ip,
            Port = body.Port,
            Desc = body.Desc,
            Logo = body.Logo,
            Img = body.Img,
            Url = body.Url
        };
    }
}
