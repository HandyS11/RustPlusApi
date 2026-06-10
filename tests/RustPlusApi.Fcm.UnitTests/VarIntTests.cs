using Xunit;
using static RustPlusApi.Fcm.Utils.McsUtils;

namespace RustPlusApi.Fcm.UnitTests;

/// <summary>
/// Guards the MCS varint framing (regression: <c>EncodeVarInt32(0)</c> used to return
/// an empty array, corrupting the wire framing for zero-length payloads).
/// </summary>
public class VarIntTests
{
    [Fact]
    public void EncodeVarInt32_Zero_ReturnsSingleZeroByte()
    {
        var encoded = EncodeVarInt32(0);

        Assert.Equal(new byte[] { 0x00 }, encoded);
    }

    [Theory]
    [InlineData(1, new byte[] { 0x01 })]
    [InlineData(127, new byte[] { 0x7F })]
    [InlineData(128, new byte[] { 0x80, 0x01 })]
    [InlineData(300, new byte[] { 0xAC, 0x02 })]
    [InlineData(16384, new byte[] { 0x80, 0x80, 0x01 })]
    public void EncodeVarInt32_KnownValues_MatchProtobufEncoding(int value, byte[] expected)
    {
        var encoded = EncodeVarInt32(value);

        Assert.Equal(expected, encoded);
    }

    [Fact]
    public void EncodeVarInt32_RoundTrips_ThroughManualDecode()
    {
        foreach (var value in new[] { 0, 1, 5, 127, 128, 255, 300, 65_535, 1_000_000 })
        {
            var decoded = DecodeVarInt32(EncodeVarInt32(value));
            Assert.Equal(value, decoded);
        }
    }

    private static int DecodeVarInt32(IReadOnlyList<byte> bytes)
    {
        var result = 0;
        var shift = 0;
        foreach (var b in bytes)
        {
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                break;
            }

            shift += 7;
        }
        return result;
    }
}
