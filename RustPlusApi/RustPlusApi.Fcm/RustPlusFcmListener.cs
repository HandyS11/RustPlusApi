using System.Diagnostics;

using Newtonsoft.Json;

using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;

namespace RustPlusApi.Fcm
{
    public class RustPlusFcmListener(Credentials credentials, ICollection<string>? persistentIds = null)
        : RustPlusFcmListenerClient(credentials, persistentIds)
    {
        public event EventHandler<MessageData>? OnParing;

        public event EventHandler<(ServerEventArg, EntityEventArg)>? OnEntityParing;
        public event EventHandler<ServerFullEventArg>? OnServerPairing;

        public event EventHandler<(ServerEventArg, int)>? OnSmartSwitchParing;
        public event EventHandler<(ServerEventArg, int)>? OnSmartAlarmParing;
        public event EventHandler<(ServerEventArg, int)>? OnStorageMonitorParing;

        public event EventHandler<AlarmEventArg>? OnAlarmTriggered;

        protected override void ParseNotification(string? message)
        {
            if (message is null) return;

            var msg = JsonConvert.DeserializeObject<FcmMessage>(message);
            if (msg is null) return;

            switch (msg.Data.ChannelId)
            {
                case "pairing":
                    OnParing?.Invoke(this, msg.Data);
                    ParsePairing(msg.Data.Body);
                    break;
                case "alarm":
                    var alarm = new AlarmEventArg()
                    {
                        ServerId = msg.Data.Body.Id,
                        Title = msg.Data.Title,
                        Message = msg.Data.Message
                    };
                    OnAlarmTriggered?.Invoke(this, alarm);
                    break;
                default:
                    Debug.WriteLine($"Unknown channel: {msg.Data.ChannelId}");
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
                        Url = body.Url
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
