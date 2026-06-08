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

    // NOTE: EncodeVarInt32(-1) triggers an infinite loop in the current signed-int implementation.
    // The do-while loop condition `value != 0` never terminates because arithmetic right-shift of
    // -1 by 7 bits stays -1 on 32-bit signed int. A correct base-128 varint encoding of -1 as
    // uint32 would produce { 0xFF, 0xFF, 0xFF, 0xFF, 0x0F }, but the current signed loop diverges.
    // This is a REAL BUG: EncodeVarInt32 must not be called with negative values.
    // The test is intentionally omitted to avoid hanging the test runner.
    // See: DONE_WITH_CONCERNS note in the Task 3.2 report.
}
