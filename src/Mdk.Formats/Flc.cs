namespace Mdk.Formats;

/// <summary>An Autodesk FLC animation (<c>MISC/FLIC/*.FLC</c>): 8-bit frames with a palette, decoded
/// one frame at a time into <see cref="Indices"/> and <see cref="Palette"/>.
/// <code>
///   header (128) ─► frame 1 (palette + BYTE_RUN) ─► frames 2..n (DELTA_FLC) ─► ring frame (to frame 1)
/// </code></summary>
public sealed class Flc
{
    private const ushort Magic = 0xAF12;
    private const int HeaderSize = 128;
    private const int FrameHeaderSize = 16;
    private const int ChunkHeaderSize = 6;
    private const int PaletteBytes = 768;
    private const int FirstFrameOffset = 80;

    /// <summary>Chunk types: frames; in a frame the palette (256 or 64 levels), deltas (word FLC,
    /// byte FLI), all black, a full run-length frame, raw pixels.</summary>
    private enum Chunk : ushort
    {
        Color256 = 4,
        DeltaFlc = 7,
        Color64 = 11,
        DeltaFli = 12,
        Black = 13,
        ByteRun = 15,
        Copy = 16,
        Frame = 0xF1FA,
    }

    /// <summary>DELTA_FLC line words: the top bits tell a packet count (00), a line skip (11) or the
    /// last pixel of an odd-width line (10).</summary>
    private const int OpcodeMask = 0xC000;
    private const int OpcodeSkip = 0xC000;
    private const int OpcodeLastByte = 0x8000;
    private const int SkipBase = 0x10000;
    private const int Levels64Scale = 4;
    private const int FullCount = 256;

    private readonly byte[] _bytes;
    private readonly int _firstFrame;
    private int _pos;

    public int Width { get; }
    public int Height { get; }
    /// <summary>Frames to show (the file has one more, the ring frame).</summary>
    public int FrameCount { get; }
    /// <summary>Milliseconds per frame.</summary>
    public int Speed { get; }
    /// <summary>The current frame's palette indices and palette (256 RGB, 0-255).</summary>
    public byte[] Indices { get; }
    public byte[] Palette { get; } = new byte[PaletteBytes];
    /// <summary>Frames decoded since the start.</summary>
    public int Frame { get; private set; }

    private Flc(byte[] bytes)
    {
        _bytes = bytes;
        FrameCount = Bin.U16(bytes, 6);
        Width = Bin.U16(bytes, 8);
        Height = Bin.U16(bytes, 10);
        Speed = (int)Bin.U32(bytes, 16);
        var first = (int)Bin.U32(bytes, FirstFrameOffset);
        _firstFrame = first == 0 ? HeaderSize : first;
        Indices = new byte[Width * Height];
        Rewind();
    }

    public static Flc? Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return bytes.Length < HeaderSize || Bin.U16(bytes, 4) != Magic ? null : new Flc(bytes);
    }

    /// <summary>Back to before the first frame.</summary>
    public void Rewind()
    {
        _pos = _firstFrame;
        Frame = 0;
        Array.Clear(Indices);
    }

    /// <summary>Decodes the next frame; false after the last.</summary>
    public bool NextFrame()
    {
        if (Frame >= FrameCount)
        {
            return false;
        }

        while (_pos + ChunkHeaderSize <= _bytes.Length)
        {
            var size = (int)Bin.U32(_bytes, _pos);
            var type = (Chunk)Bin.U16(_bytes, _pos + 4);
            var start = _pos;
            _pos += Math.Max(size, ChunkHeaderSize);
            if (type != Chunk.Frame)
            {
                continue;
            }

            int chunks = Bin.U16(_bytes, start + 6);
            var p = start + FrameHeaderSize;
            for (var i = 0; i < chunks; i++)
            {
                var chunkSize = (int)Bin.U32(_bytes, p);
                DecodeChunk((Chunk)Bin.U16(_bytes, p + 4), p + ChunkHeaderSize);
                p += Math.Max(chunkSize, ChunkHeaderSize);
            }

            Frame++;
            return true;
        }

        return false;
    }

    private void DecodeChunk(Chunk type, int p)
    {
        switch (type)
        {
            case Chunk.Color256: DecodePalette(p, 1); break;
            case Chunk.Color64: DecodePalette(p, Levels64Scale); break;
            case Chunk.ByteRun: DecodeByteRun(p); break;
            case Chunk.DeltaFlc: DecodeDeltaFlc(p); break;
            case Chunk.DeltaFli: DecodeDeltaFli(p); break;
            case Chunk.Black: Array.Clear(Indices); break;
            case Chunk.Copy: _bytes.AsSpan(p, Indices.Length).CopyTo(Indices); break;
        }
    }

    /// <summary>Packets of (colours to skip, colours to set (0 = 256), RGB...); 64 levels scaled.</summary>
    private void DecodePalette(int p, int scale)
    {
        int packets = Bin.U16(_bytes, p);
        p += 2;
        var colour = 0;
        for (var i = 0; i < packets; i++)
        {
            colour += _bytes[p];
            int count = _bytes[p + 1];
            count = count == 0 ? FullCount : count;
            p += 2;
            for (var k = 0; k < count * 3; k++)
            {
                if (colour * 3 + k < Palette.Length)
                {
                    Palette[colour * 3 + k] = (byte)Math.Min(_bytes[p + k] * scale, byte.MaxValue);
                }
            }

            p += count * 3;
            colour += count;
        }
    }

    /// <summary>Per line a packet count (ignored), then runs: negative copies, positive repeats.</summary>
    private void DecodeByteRun(int p)
    {
        for (var y = 0; y < Height; y++)
        {
            p++;
            var x = 0;
            var row = y * Width;
            while (x < Width)
            {
                int count = (sbyte)_bytes[p++];
                if (count < 0)
                {
                    _bytes.AsSpan(p, -count).CopyTo(Indices.AsSpan(row + x));
                    p += -count;
                    x += -count;
                    continue;
                }

                Indices.AsSpan(row + x, count).Fill(_bytes[p++]);
                x += count;
            }
        }
    }

    /// <summary>Lines of word packets: line words (skips, odd lines' last byte, the packet count),
    /// then packets of (columns to skip, count): positive copies words, negative repeats one.</summary>
    private void DecodeDeltaFlc(int p)
    {
        int lines = Bin.U16(_bytes, p);
        p += 2;
        var y = 0;
        for (var line = 0; line < lines; line++)
        {
            var packets = 0;
            while (true)
            {
                int word = Bin.U16(_bytes, p);
                p += 2;
                if ((word & OpcodeMask) == OpcodeSkip)
                {
                    y += SkipBase - word;
                }
                else if ((word & OpcodeMask) == OpcodeLastByte)
                {
                    Indices[y * Width + Width - 1] = (byte)word;
                }
                else
                {
                    packets = word;
                    break;
                }
            }

            var x = 0;
            var row = y * Width;
            for (var i = 0; i < packets; i++)
            {
                x += _bytes[p];
                int count = (sbyte)_bytes[p + 1];
                p += 2;
                if (count > 0)
                {
                    _bytes.AsSpan(p, count * 2).CopyTo(Indices.AsSpan(row + x));
                    p += count * 2;
                    x += count * 2;
                    continue;
                }

                var a = _bytes[p];
                var b = _bytes[p + 1];
                p += 2;
                for (var k = 0; k < -count; k++)
                {
                    Indices[row + x] = a;
                    Indices[row + x + 1] = b;
                    x += 2;
                }
            }

            y++;
        }
    }

    /// <summary>The older byte delta: first line, line count, then per line packets of (skip,
    /// count): positive copies bytes, negative repeats one.</summary>
    private void DecodeDeltaFli(int p)
    {
        int y = Bin.U16(_bytes, p);
        int lines = Bin.U16(_bytes, p + 2);
        p += 4;
        for (var line = 0; line < lines; line++)
        {
            int packets = _bytes[p++];
            var x = 0;
            var row = y * Width;
            for (var i = 0; i < packets; i++)
            {
                x += _bytes[p];
                int count = (sbyte)_bytes[p + 1];
                p += 2;
                if (count > 0)
                {
                    _bytes.AsSpan(p, count).CopyTo(Indices.AsSpan(row + x));
                    p += count;
                    x += count;
                    continue;
                }

                Indices.AsSpan(row + x, -count).Fill(_bytes[p++]);
                x += -count;
            }

            y++;
        }
    }
}
