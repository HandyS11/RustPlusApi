using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;

namespace RustPlusApi.Fcm.Extensions;

/// <summary>Extension methods that project a <see cref="MessageData"/> into FCM event model types.</summary>
public static class MessageDataToEventModel
{
    /// <summary>Maps the notification message data to an <see cref="AlarmEvent"/>.</summary>
    /// <param name="data">The message data to map.</param>
    public static AlarmEvent ToAlarmEvent(this MessageData data)
    {
        return new AlarmEvent
        {
            Title = data.Title, Message = data.Message
        };
    }
}
