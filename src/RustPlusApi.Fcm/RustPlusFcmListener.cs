using System.Diagnostics;

using Newtonsoft.Json;

using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;
using RustPlusApi.Fcm.Extensions;

using static RustPlusApi.Fcm.Utils.ResponseHelper;

namespace RustPlusApi.Fcm;

/// <summary>
/// Represents a RustPlus FCM listener.
/// </summary>
/// <param name="credentials">The credentials used for authentication.</param>
/// <param name="persistentIds">The collection of persistent IDs.</param>
public class RustPlusFcmListener(Credentials credentials, ICollection<string>? persistentIds = null)
        : RustPlusFcmListenerClient(credentials, persistentIds)
{
    public event EventHandler<FcmMessage>? OnParing;

    public event EventHandler<Notification<EntityEvent?>>? OnEntityParing;
    public event EventHandler<Notification<ServerEvent?>>? OnServerPairing;

    public event EventHandler<Notification<int?>>? OnSmartSwitchParing;
    public event EventHandler<Notification<int?>>? OnSmartAlarmParing;
    public event EventHandler<Notification<int?>>? OnStorageMonitorParing;

    public event EventHandler<AlarmEvent?>? OnAlarmTriggered;

    protected override void ParseNotification(FcmMessage? message)
    {
        if (message is null) return;

        //// The message is in the format: {"channelId":"pairing","body":{...}}
        //var directMessage = JsonConvert.DeserializeObject<MessageData>(message);
        //if (directMessage is null)
        //{
        //    Console.WriteLine($"🚀 Failed to deserialize MessageData");
        //    return;
        //}

        //// Create an FcmMessage wrapper for compatibility
        //var msg = new FcmMessage
        //{
        //    FcmMessageId = Guid.NewGuid(),
        //    Data = directMessage
        //};

        switch (message.Data.ChannelId)
        {
            case "pairing":
                OnParing?.Invoke(this, message);
                var body = JsonConvert.DeserializeObject<Body>(message.Data.Body);
                if (body is null)
                {
                    return;
                }
                ParsePairing(message.FcmMessageId, body);
                break;
            case "alarm":
                OnAlarmTriggered?.Invoke(this, message.Data.ToAlarmEvent(message.FcmMessageId));
                break;
            default:
                Debug.WriteLine($"Unknown channel: {message.Data.ChannelId}");
                break;
        }
    }

    private void ParsePairing(Guid notificationId, Body body)
    {
        switch (body.Type)
        {
            case "entity":
                var entity = BuildGenericOutput(notificationId, body, body.ToEntityEvent());
                OnEntityParing?.Invoke(this, entity);
                ParsePairingEntity(notificationId, body);
                break;
            case "server":
                var server = BuildGenericOutput(notificationId, body, body.ToServerEvent());
                OnServerPairing?.Invoke(this, server);
                break;
            default:
                Debug.WriteLine($"Unknown pairing type: {body.Type}");
                break;
        }
    }

    private void ParsePairingEntity(Guid notificationId, Body body)
    {
        var response = BuildGenericOutput(notificationId, body, body.ToEntityId());

        switch (body.EntityType)
        {
            case 1:
                OnSmartSwitchParing?.Invoke(this, response);
                break;
            case 2:
                OnSmartAlarmParing?.Invoke(this, response);
                break;
            case 3:
                OnStorageMonitorParing?.Invoke(this, response);
                break;
            default:
                Debug.WriteLine($"Unknown entity type: {body.EntityType}");
                break;
        }
    }
}
