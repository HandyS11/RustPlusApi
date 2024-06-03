using System.Diagnostics;

using ProtoBuf;

using RustPlusApi.Fcm.Data.Events;

using static RustPlusApi.Fcm.Data.Tags;
using static RustPlusApi.Fcm.Utils.Utils;

namespace RustPlusApi.Fcm.Utils
{
    internal class RawMessageParser()
    {
        internal event EventHandler<MessageEventArgs>? MessageReceived;

        internal event EventHandler<Exception>? ErrorOccurred;

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
    }
}
