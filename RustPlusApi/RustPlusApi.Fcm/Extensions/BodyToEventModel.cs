using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;

namespace RustPlusApi.Fcm.Extensions
{
    public static class BodyToEventModel
    {
        public static int? ToEntityId(this Body body)
        {
            return body.EntityId;
        }

        public static EntityEvent ToEntityEvent(this Body body)
        {
            return new EntityEvent
            {
                EntityType = body.EntityType,
                EntityId = body.EntityId,
                EntityName = body.EntityName
            };
        }

        public static ServerEvent ToServerEvent(this Body body)
        {
            return new ServerEvent
            {
                Id = body.Id,
                Name = body.EntityName,
                Ip = body.Ip,
                Port = body.Port,
                Desc = body.Desc,
                Logo = body.Logo,
                Img = body.Img,
                Url = body.Url
            };
        }
    }
}
