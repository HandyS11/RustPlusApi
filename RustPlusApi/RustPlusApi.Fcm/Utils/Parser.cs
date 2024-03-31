using System.Diagnostics;

using McsProto;

using ProtoBuf;

using static RustPlusApi.Fcm.Data.Constants;

namespace RustPlusApi.Fcm.Utils
{
    public class Parser
    {
        public event EventHandler<Exception>? ErrorOccurred;
        public event EventHandler<MessageEventArgs>? MessageReceived;

        private ProcessingState _state = ProcessingState.McsVersionTagAndSize;
        private byte[] _data = [];

        private int _sizePacketSoFar = 0;
        private McsProtoTag _messageTag = 0;
        private int _messageSize = 0;

        private bool _handshakeComplete;
        private bool _isWaitingForData = true;

        public void OnData(byte[] buffer)
        {
            _data = [.. _data, .. buffer];
            if (!_isWaitingForData) return;
            _isWaitingForData = false;
            WaitForData();
        }

        private void WaitForData()
        {
            Debug.WriteLine($"waitForData state: {_state}");

            int minBytesNeeded;

            switch (_state)
            {
                case ProcessingState.McsVersionTagAndSize:
                    minBytesNeeded = KVersionPacketLen + KTagPacketLen + KSizePacketLenMin;
                    break;
                case ProcessingState.McsTagAndSize:
                    minBytesNeeded = KTagPacketLen + KSizePacketLenMin;
                    break;
                case ProcessingState.McsSize:
                    minBytesNeeded = _sizePacketSoFar + 1;
                    break;
                case ProcessingState.McsProtoBytes:
                    minBytesNeeded = _messageSize;
                    break;
                default:
                    ErrorOccurred?.Invoke(this, new Exception($"Unexpected state: {_state}"));
                    return;
            }

            if (_data.Length < minBytesNeeded)
            {
                Debug.WriteLine($"Socket read finished prematurely. Waiting for {minBytesNeeded - _data.Length} more bytes");
                _isWaitingForData = true;
                return;
            }

            Debug.WriteLine($"Processing MCS data: state == {_state}");

            switch (_state)
            {
                case ProcessingState.McsVersionTagAndSize:
                    OnGotVersion();
                    break;
                case ProcessingState.McsTagAndSize:
                    OnGotMessageTag();
                    break;
                case ProcessingState.McsSize:
                    OnGotMessageSize();
                    break;
                case ProcessingState.McsProtoBytes:
                    OnGotMessageBytes();
                    break;
                default:
                    ErrorOccurred?.Invoke(this, new Exception($"Unexpected state: {_state}"));
                    return;
            }
        }

        private void OnGotVersion()
        {
            var version = _data[0];
            _data = _data.Skip(1).ToArray();
            Debug.WriteLine($"VERSION IS {version}");

            if (version < KMcsVersion && version != 38)
            {
                ErrorOccurred?.Invoke(this, new Exception($"Got wrong version: {version}"));
                return;
            }
            OnGotMessageTag();
        }

        private void OnGotMessageTag()
        {
            _messageTag = (McsProtoTag)_data[0];
            _data = _data.Skip(1).ToArray();
            Debug.WriteLine($"RECEIVED PROTO OF TYPE {_messageTag}");

            OnGotMessageSize();
        }

        private void OnGotMessageSize()
        {
            var incompleteSizePacket = false;
            var reader = new BinaryReader(new MemoryStream(_data));

            try
            {
                _messageSize = reader.ReadInt32();
            }
            catch (EndOfStreamException)
            {
                incompleteSizePacket = true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
                return;
            }

            if (incompleteSizePacket)
            {
                _sizePacketSoFar = (int)reader.BaseStream.Position;
                _state = ProcessingState.McsSize;
                WaitForData();
                return;
            }

            _data = _data.Skip((int)reader.BaseStream.Position).ToArray();

            Debug.WriteLine($"Proto size: {_messageSize}");
            _sizePacketSoFar = 0;

            if (_messageSize > 0)
            {
                _state = ProcessingState.McsProtoBytes;
                WaitForData();
            }
            else OnGotMessageBytes();
        }

        private void OnGotMessageBytes()
        {
            var protobuf = BuildProtobufFromTag(_messageTag);

            if (_messageSize == 0)
            {
                MessageReceived?.Invoke(this, new MessageEventArgs { Tag = _messageTag, Object = Activator.CreateInstance(protobuf) });
                GetNextMessage();
                return;
            }

            if (_data.Length < _messageSize)
            {
                Debug.WriteLine($"Continuing data read. Buffer size is {_data.Length}, expecting {_messageSize}");
                _state = ProcessingState.McsProtoBytes;
                WaitForData();
                return;
            }

            var buffer = _data.Take(_messageSize).ToArray();
            _data = _data.Skip(_messageSize).ToArray();

            using var stream = new MemoryStream(buffer);
            var message = Serializer.NonGeneric.Deserialize(protobuf, stream);

            MessageReceived?.Invoke(this, new MessageEventArgs { Tag = _messageTag, Object = message });

            if (_messageTag == McsProtoTag.KLoginResponseTag)
            {
                if (_handshakeComplete) Debug.WriteLine("Unexpected login response");
                else
                {
                    _handshakeComplete = true;
                    Debug.WriteLine("GCM Handshake complete.");
                }
            }
            GetNextMessage();
        }

        private void GetNextMessage()
        {
            _messageTag = 0;
            _messageSize = 0;
            _state = ProcessingState.McsTagAndSize;
            WaitForData();
        }

        private static Type BuildProtobufFromTag(McsProtoTag tag)
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
