using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;

namespace RustPlusApi.Fcm.Extensions;

/// <summary>Extension methods that project a <see cref="MessageData"/> into FCM event model types.</summary>
public static class MessageDataToEventModel
{
    /// <summary>Maps the notification message data to an <see cref="AlarmNotification"/>.</summary>
    /// <param name="data">The message data to map.</param>
    /// <param name="serverId">The ID of the server the alarm was triggered on.</param>
    /// <param name="persistentId">The FCM persistent id of the alarm message (may be <see langword="null"/>).</param>
    public static AlarmNotification ToAlarmNotification(this MessageData data, Guid serverId, string? persistentId)
    {
        return new AlarmNotification
        {
            ServerId = serverId, PersistentId = persistentId, Title = data.Title, Message = data.Message
        };
    }
}
