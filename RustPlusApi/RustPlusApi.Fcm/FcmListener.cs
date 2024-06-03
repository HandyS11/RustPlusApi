using System.Diagnostics;

using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;

namespace RustPlusApi.Fcm
{
    public class FcmListener(Credentials credentials, ICollection<string>? persistentIds = null) : FcmListenerBasic(credentials, persistentIds)
    {
        public event EventHandler<MessageData>? OnParing;

        public event EventHandler<(ServerEventArg, EntityEventArg)>? OnEntityParing;
        public event EventHandler<ServerFullEventArg>? OnServerPairing;

        public event EventHandler<(ServerEventArg, int)>? OnSmartSwitchParing;
        public event EventHandler<(ServerEventArg, int)>? OnSmartAlarmParing;
        public event EventHandler<(ServerEventArg, int)>? OnStorageMonitorParing;

        public event EventHandler<AlarmEventArg>? OnAlarmTriggered;

        protected override void ParseNotification(FcmMessage? message)
        {
            if (message == null) return;

            switch (message.Data.ChannelId)
            {
                case "pairing":
                    OnParing?.Invoke(this, message.Data);
                    ParsePairing(message.Data.Body);
                    break;
                case "alarm":
                    var alarm = new AlarmEventArg()
                    {
                        ServerId = message.Data.Body.Id,
                        Title = message.Data.Title,
                        Message = message.Data.Message
                    };
                    OnAlarmTriggered?.Invoke(this, alarm);
                    break;
                default:
                    Debug.WriteLine($"Unknown channel: {message.Data.ChannelId}");
                    break;
            }
        }

        private void ParsePairing(Body body)
        {
            switch (body.Type)
            {
                case "entity":
                    var server = new ServerEventArg()
                    {
                        Id = body.Id,
                        Name = body.Name
                    };
                    var entity = new EntityEventArg()
                    {
                        EntityType = body.EntityType ?? 0,
                        EntityId = body.EntityId ?? 0,
                        EntityName = body.EntityName
                    };
                    OnEntityParing?.Invoke(this, (server, entity));
                    ParsePairingEntity(body);
                    break;
                case "server":
                    var serverFull = new ServerFullEventArg()
                    {
                        Id = body.Id,
                        Name = body.Name,
                        Ip = body.Ip,
                        Port = body.Port,
                        Desc = body.Desc,
                        Logo = body.Logo,
                        Img = body.Img,
                        Url = body.Url,
                        PlayerId = body.PlayerId,
                        PlayerToken = body.PlayerToken
                    };
                    OnServerPairing?.Invoke(this, serverFull);
                    break;
                default:
                    Debug.WriteLine($"Unknown pairing type: {body.Type}");
                    break;
            }
        }

        private void ParsePairingEntity(Body body)
        {
            var server = new ServerEventArg()
            {
                Id = body.Id,
                Name = body.Name
            };

            switch (body.EntityType)
            {
                case 1:
                    OnSmartSwitchParing?.Invoke(this, (server, body.EntityId ?? 0));
                    break;
                case 2:
                    OnSmartAlarmParing?.Invoke(this, (server, body.EntityId ?? 0));
                    break;
                case 3:
                    OnStorageMonitorParing?.Invoke(this, (server, body.EntityId ?? 0));
                    break;
                default:
                    Debug.WriteLine($"Unknown entity type: {body.EntityType}");
                    break;
            }
        }
    }
}
