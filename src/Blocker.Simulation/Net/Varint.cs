namespace Blocker.Simulation.Net;

/// <summary>
/// Protobuf-style varint for uint32. 1-5 bytes, 7 data bits per byte,
/// MSB = continuation flag.
/// </summary>
public static class Varint
{
    public const int MaxBytes = 5;

    public static int Write(byte[] buf, int offset, uint value)
    {
        int start = offset;
        while (value >= 0x80)
        {
            buf[offset++] = (byte)(value | 0x80);
            value >>= 7;
        }
        buf[offset++] = (byte)value;
        return offset - start;
    }

    public static int Write(Span<byte> buf, uint value)
    {
        int i = 0;
        while (value >= 0x80)
        {
            buf[i++] = (byte)(value | 0x80);
            value >>= 7;
        }
        buf[i++] = (byte)value;
        return i;
    }

    public static (uint value, int consumed) Read(ReadOnlySpan<byte> buf, int offset)
    {
        uint result = 0;
        int shift = 0;
        int i = offset;
        while (true)
        {
            if (i - offset >= MaxBytes) throw new FormatException("Varint too long");
            if (i >= buf.Length) throw new FormatException("Varint truncated");
            byte b = buf[i++];
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }
        return (result, i - offset);
    }

    /// <summary>
    /// How many bytes Write would produce for this value. Used for sizing buffers.
    /// </summary>
    public static int SizeOf(uint value)
    {
        int n = 1;
        while (value >= 0x80) { value >>= 7; n++; }
        return n;
    }
}
