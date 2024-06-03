using System.Diagnostics;

using Newtonsoft.Json;

using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;
using RustPlusApi.Fcm.Extensions;

using static RustPlusApi.Fcm.Utils.ResponseHelper;

namespace RustPlusApi.Fcm
{
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

        protected override void ParseNotification(string? message)
        {
            if (message is null) return;

            var msg = JsonConvert.DeserializeObject<FcmMessage>(message);
            if (msg is null) return;

            switch (msg.Data.ChannelId)
            {
                case "pairing":
                    OnParing?.Invoke(this, msg);
                    ParsePairing(msg.FcmMessageId, msg.Data.Body);
                    break;
                case "alarm":
                    OnAlarmTriggered?.Invoke(this, msg.Data.ToAlarmEvent(msg.FcmMessageId));
                    break;
                default:
                    Debug.WriteLine($"Unknown channel: {msg.Data.ChannelId}");
                    break;
            }
        }

        private void ParsePairing(Guid notifId, Body body)
        {
            switch (body.Type)
            {
                case "entity":
                    var entity = BuildGenericOutput(notifId, body, body.ToEntityEvent());
                    OnEntityParing?.Invoke(this, entity);
                    ParsePairingEntity(notifId, body);
                    break;
                case "server":
                    var server = BuildGenericOutput(notifId, body, body.ToServerEvent());
                    OnServerPairing?.Invoke(this, server);
                    break;
                default:
                    Debug.WriteLine($"Unknown pairing type: {body.Type}");
                    break;
            }
        }

        private void ParsePairingEntity(Guid notifId, Body body)
        {
            var response = BuildGenericOutput(notifId, body, body.ToEntityId());

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
}
