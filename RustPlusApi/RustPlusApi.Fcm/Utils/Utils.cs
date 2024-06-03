using McsProto;

using static RustPlusApi.Fcm.Data.Tags;

namespace RustPlusApi.Fcm.Utils
{
    public static class Utils
    {
        public static byte[] EncodeVarint32(int value)
        {
            List<byte> result = [];
            while (value != 0)
            {
                byte b = (byte)(value & 0x7F);
                value >>= 7;
                if (value != 0) b |= 0x80;
                result.Add(b);
            }
            return [.. result];
        }

        public static McsProtoTag GetTagFromProtobufType(Type type)
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

        public static Type BuildProtobufFromTag(McsProtoTag tag)
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
