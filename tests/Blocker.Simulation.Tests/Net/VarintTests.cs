using Blocker.Simulation.Net;
using Xunit;

namespace Blocker.Simulation.Tests.Net;

public class VarintTests
{
    [Theory]
    [InlineData(0u, new byte[] { 0x00 })]
    [InlineData(1u, new byte[] { 0x01 })]
    [InlineData(127u, new byte[] { 0x7F })]
    [InlineData(128u, new byte[] { 0x80, 0x01 })]
    [InlineData(300u, new byte[] { 0xAC, 0x02 })]
    [InlineData(16384u, new byte[] { 0x80, 0x80, 0x01 })]
    public void Write_Produces_Expected_Bytes(uint value, byte[] expected)
    {
        var buf = new byte[8];
        int written = Varint.Write(buf, 0, value);
        Assert.Equal(expected.Length, written);
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], buf[i]);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(127u)]
    [InlineData(128u)]
    [InlineData(12345u)]
    [InlineData(uint.MaxValue)]
    public void Roundtrip(uint value)
    {
        var buf = new byte[8];
        int written = Varint.Write(buf, 0, value);
        var (decoded, consumed) = Varint.Read(buf, 0);
        Assert.Equal(value, decoded);
        Assert.Equal(written, consumed);
    }

    [Fact]
    public void Read_Rejects_Overlong_Encoding()
    {
        // 6-byte sequence is invalid (max is 5 bytes for uint32)
        var buf = new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x01 };
        Assert.Throws<FormatException>(() => Varint.Read(buf, 0));
    }
}
