using System.Numerics;
using System.Text;

namespace Mdk.Formats.Tests;

/// <summary>Synthetic little-endian files for format tests.</summary>
internal sealed class Bytes
{
    private readonly MemoryStream _stream = new();
    private readonly BinaryWriter _w;

    public Bytes() => _w = new BinaryWriter(_stream);

    public int Position => (int)_stream.Position;

    public Bytes U8(params int[] values)
    {
        foreach (var v in values)
        {
            _w.Write((byte)v);
        }

        return this;
    }

    public Bytes S16(params int[] values)
    {
        foreach (var v in values)
        {
            _w.Write((short)v);
        }

        return this;
    }

    public Bytes U16(int value)
    {
        _w.Write((ushort)value);
        return this;
    }

    public Bytes U32(params long[] values)
    {
        foreach (var v in values)
        {
            _w.Write((uint)v);
        }

        return this;
    }

    public Bytes F32(params float[] values)
    {
        foreach (var v in values)
        {
            _w.Write(v);
        }

        return this;
    }

    public Bytes Vec(Vector3 v) => F32(v.X, v.Y, v.Z);

    /// <summary>A fixed-length, NUL-padded name.</summary>
    public Bytes Name(string text, int length)
    {
        var bytes = new byte[length];
        Encoding.ASCII.GetBytes(text).CopyTo(bytes, 0);
        _w.Write(bytes);
        return this;
    }

    /// <summary>A Pascal string: <c>u8 length</c>, the characters.</summary>
    public Bytes Pascal(string text) => U8(text.Length).Raw(Encoding.ASCII.GetBytes(text));

    public Bytes Raw(byte[] bytes)
    {
        _w.Write(bytes);
        return this;
    }

    /// <summary>Writes a <c>u32</c> at <paramref name="at"/>, later.</summary>
    public Bytes Patch(int at, long value)
    {
        var position = _stream.Position;
        _stream.Position = at;
        _w.Write((uint)value);
        _stream.Position = position;
        return this;
    }

    public byte[] ToArray() => _stream.ToArray();
}
