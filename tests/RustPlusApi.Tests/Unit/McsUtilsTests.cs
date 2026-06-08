using McsProto;
using RustPlusApi.Fcm.Data;
using Xunit;
using static RustPlusApi.Fcm.Utils.McsUtils;

namespace RustPlusApi.Tests.Unit;

/// <summary>Covers the throw arms and edge encodings in <see cref="McsUtils"/>.</summary>
public class McsUtilsTests
{
    [Fact]
    public void GetTagFromProtobufType_UnknownType_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => GetTagFromProtobufType(typeof(string)));

    [Fact]
    public void BuildProtobufFromTag_UnknownTag_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildProtobufFromTag((Tags.McsProtoTag)250));

    [Fact]
    public void EncodeVarInt32_Negative_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => EncodeVarInt32(-1));

    /// <summary>
    /// Asserts the ArgumentOutOfRangeException message is non-empty and meaningful —
    /// kills the String mutation that replaces the message literal with "".
    /// </summary>
    [Fact]
    public void EncodeVarInt32_Negative_ExceptionMessageIsNonEmpty()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => EncodeVarInt32(-1));
        Assert.False(string.IsNullOrEmpty(ex.Message));
        // The message must mention varints or encoding (confirms it's the right throw site).
        Assert.True(
            ex.Message.Contains("varint", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("non-negative", StringComparison.OrdinalIgnoreCase),
            $"Exception message did not contain expected text: {ex.Message}");
    }
}
