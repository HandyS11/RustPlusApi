using System.Diagnostics;

using McsProto;

using ProtoBuf;

using RustPlusApi.Fcm.Data.Events;

using static RustPlusApi.Fcm.Data.Constants;

namespace RustPlusApi.Fcm.Utils
{
    internal class RawMessageParser()
    {
        internal event EventHandler<Exception>? ErrorOccurred;
        internal event EventHandler<MessageEventArgs>? MessageReceived;

        private byte[] _data = [];
        private bool _handshakeComplete;

        internal void OnData(byte[] buffer, Type type)
        {
            _data = buffer;
            OnGotMessageBytes(type);
        }

        internal void OnGotLoginResponse()
        {
            _handshakeComplete = true;
        }

        private void OnGotMessageBytes(Type type)
        {
            try
            {
                var messageTag = GetTagFromProtobufType(type);

                if (_data.Length == 0)
                {
                    MessageReceived?.Invoke(this, new MessageEventArgs { Tag = messageTag, Object = Activator.CreateInstance(type) });
                    return;
                }

                var buffer = _data.Take(_data.Length).ToArray();
                _data = _data.Skip(_data.Length).ToArray();

                using var stream = new MemoryStream(buffer);
                var message = Serializer.NonGeneric.Deserialize(type, stream);

                MessageReceived?.Invoke(this, new MessageEventArgs { Tag = messageTag, Object = message });

                if (messageTag == McsProtoTag.KLoginResponseTag)
                {
                    if (_handshakeComplete) Debug.WriteLine("Unexpected login response");
                    else
                    {
                        _handshakeComplete = true;
                        Debug.WriteLine("GCM Handshake complete.");
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
            }
        }

        internal static McsProtoTag GetTagFromProtobufType(Type type)
        {
            if (type == typeof(HeartbeatPing)) return McsProtoTag.KHeartbeatPingTag;
            else if (type == typeof(HeartbeatAck)) return McsProtoTag.KHeartbeatAckTag;
            else if (type == typeof(LoginRequest)) return McsProtoTag.KLoginRequestTag;
            else if (type == typeof(LoginResponse)) return McsProtoTag.KLoginResponseTag;
            else if (type == typeof(Close)) return McsProtoTag.KCloseTag;
            else if (type == typeof(IqStanza)) return McsProtoTag.KIqStanzaTag;
            else if (type == typeof(DataMessageStanza)) return McsProtoTag.KDataMessageStanzaTag;
            else if (type == typeof(StreamErrorStanza)) return McsProtoTag.KStreamErrorStanzaTag;
            else throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }

        internal static Type BuildProtobufFromTag(McsProtoTag tag)
        {
            // ReSharper disable once SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault
            return tag switch
            {
                McsProtoTag.KHeartbeatPingTag => typeof(HeartbeatPing),
                McsProtoTag.KHeartbeatAckTag => typeof(HeartbeatAck),
                McsProtoTag.KLoginRequestTag => typeof(LoginRequest),
                McsProtoTag.KLoginResponseTag => typeof(LoginResponse),
                McsProtoTag.KCloseTag => typeof(Close),
                McsProtoTag.KIqStanzaTag => typeof(IqStanza),
                McsProtoTag.KDataMessageStanzaTag => typeof(DataMessageStanza),
                McsProtoTag.KStreamErrorStanzaTag => typeof(StreamErrorStanza),
                _ => throw new ArgumentOutOfRangeException(nameof(tag), tag, null)
            };
        }
    }
}
