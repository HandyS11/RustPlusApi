using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;

namespace RustPlusApi.Fcm.Extensions
{
    public static class MessageDataToEventModel
    {
        public static AlarmEvent ToAlarmEvent(this MessageData data, Guid notificationId)
        {
            return new AlarmEvent
            {
                NotificationId = notificationId,
                Title = data.Title,
                Message = data.Message
            };
        }
    }
}
