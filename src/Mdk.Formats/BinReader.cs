using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Mdk.Formats;

/// <summary>Sequential little-endian reader over a byte array.</summary>
public sealed class BinReader(byte[] bytes, int pos = 0)
{
    public byte[] Bytes { get; } = bytes;
    public int Pos { get; set; } = pos;

    public bool Eof => Pos >= Bytes.Length;

    public void Skip(int count) => Pos += count;

    public byte U8() => Bytes[Pos++];

    public ushort U16()
    {
        Pos += 2;
        return BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(Pos - 2));
    }

    public short S16()
    {
        Pos += 2;
        return BinaryPrimitives.ReadInt16LittleEndian(Bytes.AsSpan(Pos - 2));
    }

    public uint U32()
    {
        Pos += 4;
        return BinaryPrimitives.ReadUInt32LittleEndian(Bytes.AsSpan(Pos - 4));
    }

    public int S32()
    {
        Pos += 4;
        return BinaryPrimitives.ReadInt32LittleEndian(Bytes.AsSpan(Pos - 4));
    }

    public float F32()
    {
        Pos += 4;
        return BinaryPrimitives.ReadSingleLittleEndian(Bytes.AsSpan(Pos - 4));
    }

    public Vector3 Vec3() => new(F32(), F32(), F32());

    /// <summary>Reads a fixed-length, NUL-padded ASCII string.</summary>
    public string Name(int length)
    {
        Pos += length;
        return Bin.Ascii(Bytes, Pos - length, length);
    }

    public byte[] Buffer(int length)
    {
        Pos += length;
        return Bytes.AsSpan(Pos - length, length).ToArray();
    }
}

/// <summary>Random-access reads at an offset.</summary>
public static class Bin
{
    public static ushort U16(byte[] b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at));
    public static short S16(byte[] b, int at) => BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(at));
    public static uint U32(byte[] b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
    public static int S32(byte[] b, int at) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at));
    public static float F32(byte[] b, int at) => BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(at));

    /// <summary>ASCII up to the first NUL within <paramref name="length"/> bytes.</summary>
    public static string Ascii(byte[] b, int at, int length)
    {
        var span = b.AsSpan(at, Math.Min(length, b.Length - at));
        var end = span.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? span : span[..end]);
    }
}
