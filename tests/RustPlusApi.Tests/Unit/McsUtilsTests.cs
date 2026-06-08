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
}
