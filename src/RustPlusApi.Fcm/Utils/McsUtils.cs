using McsProto;
using static RustPlusApi.Fcm.Data.Tags;

namespace RustPlusApi.Fcm.Utils;

/// <summary>Low-level helpers for MCS binary protocol framing and tag dispatch.</summary>
public static class McsUtils
{
    /// <summary>Encodes <paramref name="value"/> as a base-128 varint byte sequence.</summary>
    /// <param name="value">The integer to encode.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="value"/> is negative.</exception>
    public static byte[] EncodeVarInt32(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "MCS varints encode non-negative lengths only.");

        List<byte> result = [];
        do
        {
            var b = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0)
                b |= 0x80;
            result.Add(b);
        } while (value != 0);
        return [.. result];
    }

    /// <summary>Returns the <see cref="McsProtoTag"/> that corresponds to the given MCS protobuf message type.</summary>
    /// <param name="type">A recognised MCS protobuf CLR type.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="type"/> is not a recognised MCS type.</exception>
    public static McsProtoTag GetTagFromProtobufType(Type type)
    {
        if (type == typeof(HeartbeatPing))
            return McsProtoTag.KHeartbeatPingTag;
        if (type == typeof(HeartbeatAck))
            return McsProtoTag.KHeartbeatAckTag;
        if (type == typeof(LoginRequest))
            return McsProtoTag.KLoginRequestTag;
        if (type == typeof(LoginResponse))
            return McsProtoTag.KLoginResponseTag;
        if (type == typeof(Close))
            return McsProtoTag.KCloseTag;
        if (type == typeof(IqStanza))
            return McsProtoTag.KIqStanzaTag;
        if (type == typeof(DataMessageStanza))
            return McsProtoTag.KDataMessageStanzaTag;
        if (type == typeof(StreamErrorStanza))
            return McsProtoTag.KStreamErrorStanzaTag;
        throw new ArgumentOutOfRangeException(nameof(type), type, null);
    }

    /// <summary>Returns the CLR <see cref="Type"/> that corresponds to the given MCS protocol tag.</summary>
    /// <param name="tag">An MCS protocol tag value.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="tag"/> has no corresponding protobuf type.</exception>
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
