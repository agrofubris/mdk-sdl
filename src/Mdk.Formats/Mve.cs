using System.Numerics;

namespace Mdk.Formats;

/// <summary>An Interplay MVE movie (<c>MISC/FLIC/MDKBZK.MVE</c>, the end of the game): chunks of
/// opcodes that set up the screen and sound, then per frame a decoding map (4 bits per 8x8 block),
/// the blocks' data (video format 0x11, 8 bits) and DPCM sound. <see cref="NextFrame"/> decodes up
/// to the next shown frame.
/// <code>
///   signature (26) ─► chunk [u16 size, u16 type] ─► opcode [u16 size, u8 type, u8 version, data]
/// </code>
/// Frames decode into the back buffer, which still holds the frame before the last (double
/// buffering): blocks stay, copy from the frame shown last or from the back buffer, or are drawn
/// from 1 to 64 colours.</summary>
public sealed class Mve
{
    private const string Signature = "Interplay MVE File\u001a";
    private const int SignatureSize = 26;
    private const int ChunkHeader = 4;
    private const int OpcodeHeader = 4;
    private const int PaletteBytes = 768;

    private enum Opcode : byte
    {
        EndOfStream = 0x00,
        CreateTimer = 0x02,
        InitAudio = 0x03,
        InitVideo = 0x05,
        SendBuffer = 0x07,
        AudioFrame = 0x08,
        SilenceFrame = 0x09,
        SetPalette = 0x0C,
        DecodingMap = 0x0F,
        VideoData = 0x11,
    }

    // Audio flags: stereo, 16 bits, compressed (DPCM, from version 1).
    private const int AudioStereo = 1;
    private const int Audio16Bit = 2;
    private const int AudioCompressed = 4;
    /// <summary>The sound of stream 0 only.</summary>
    private const int StreamMask = 1;
    /// <summary>The video data starts with a 14-byte header.</summary>
    private const int VideoHeader = 14;
    private const int Block = 8;
    private const float SampleScale = 1f / 32768f;
    private const int ByteMiddle = 128;

    /// <summary>Interplay DPCM: each byte indexes this table of steps.</summary>
    private static readonly short[] Deltas =
    [
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
        16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31,
        32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 47, 51, 56, 61,
        66, 72, 79, 86, 94, 102, 112, 122, 133, 145, 158, 173, 189, 206, 225, 245,
        267, 292, 318, 348, 379, 414, 452, 493, 538, 587, 640, 699, 763, 832, 908, 991,
        1081, 1180, 1288, 1405, 1534, 1673, 1826, 1993, 2175, 2373, 2590, 2826, 3084, 3365, 3672, 4008,
        4373, 4772, 5208, 5683, 6202, 6767, 7385, 8059, 8794, 9597, 10472, 11428, 12471, 13609, 14851, 16206,
        17685, 19298, 21060, 22981, 25078, 27367, 29864, 32589, -29973, -26728, -23186, -19322, -15105, -10503, -5481, -1,
        1, 1, 5481, 10503, 15105, 19322, 23186, 26728, 29973, -32589, -29864, -27367, -25078, -22981, -21060, -19298,
        -17685, -16206, -14851, -13609, -12471, -11428, -10472, -9597, -8794, -8059, -7385, -6767, -6202, -5683, -5208, -4772,
        -4373, -4008, -3672, -3365, -3084, -2826, -2590, -2373, -2175, -1993, -1826, -1673, -1534, -1405, -1288, -1180,
        -1081, -991, -908, -832, -763, -699, -640, -587, -538, -493, -452, -414, -379, -348, -318, -292,
        -267, -245, -225, -206, -189, -173, -158, -145, -133, -122, -112, -102, -94, -86, -79, -72,
        -66, -61, -56, -51, -47, -43, -42, -41, -40, -39, -38, -37, -36, -35, -34, -33,
        -32, -31, -30, -29, -28, -27, -26, -25, -24, -23, -22, -21, -20, -19, -18, -17,
        -16, -15, -14, -13, -12, -11, -10, -9, -8, -7, -6, -5, -4, -3, -2, -1,
    ];

    private readonly byte[] _bytes;
    private int _pos = SignatureSize;
    private int _audioFlags;
    /// <summary>The back buffer (being decoded) and the frame shown last.</summary>
    private byte[] _current = [];
    private byte[] _last = [];
    private byte[] _map = [];
    private readonly int[] _predictors = new int[2];
    private bool _shown;
    private bool _ended;

    public int Width { get; private set; }
    public int Height { get; private set; }
    /// <summary>Microseconds per frame.</summary>
    public int FrameTime { get; private set; }
    public int SampleRate { get; private set; }
    public int Channels { get; private set; } = 1;
    /// <summary>256 RGB, 0-255.</summary>
    public byte[] Palette { get; } = new byte[PaletteBytes];
    /// <summary>Sound decoded since the last call: stereo frames (-1 to 1).</summary>
    public List<Vector2> Audio { get; } = [];
    public int Frame { get; private set; }

    /// <summary>The frame shown last (palette indices).</summary>
    public byte[] Indices => _last;

    private Mve(byte[] bytes) => _bytes = bytes;

    public static Mve? Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return bytes.Length < SignatureSize || Bin.Ascii(bytes, 0, Signature.Length) != Signature ? null : new Mve(bytes);
    }

    /// <summary>Runs opcodes up to the next shown frame; false at the end of the movie.</summary>
    public bool NextFrame()
    {
        Audio.Clear();
        _shown = false;
        while (!_ended && !_shown && _pos + ChunkHeader <= _bytes.Length)
        {
            int size = Bin.U16(_bytes, _pos);
            var end = _pos + ChunkHeader + size;
            _pos += ChunkHeader;
            while (_pos + OpcodeHeader <= end)
            {
                int length = Bin.U16(_bytes, _pos);
                var type = (Opcode)_bytes[_pos + 2];
                Run(type, _bytes[_pos + 3], _pos + OpcodeHeader, length);
                _pos += OpcodeHeader + length;
                _ended |= type == Opcode.EndOfStream;
            }

            _pos = end;
        }

        if (_shown)
        {
            Frame++;
        }

        return _shown;
    }

    private void Run(Opcode type, int version, int p, int length)
    {
        switch (type)
        {
            case Opcode.CreateTimer:
                FrameTime = (int)Bin.U32(_bytes, p) * Bin.U16(_bytes, p + 4);
                break;
            case Opcode.InitAudio:
                _audioFlags = Bin.U16(_bytes, p + 2);
                if (version == 0)
                {
                    _audioFlags &= ~AudioCompressed;
                }

                SampleRate = Bin.U16(_bytes, p + 4);
                Channels = (_audioFlags & AudioStereo) != 0 ? 2 : 1;
                break;
            case Opcode.InitVideo:
                InitVideo(Bin.U16(_bytes, p) * Block, Bin.U16(_bytes, p + 2) * Block);
                break;
            case Opcode.SetPalette:
                SetPalette(p);
                break;
            case Opcode.DecodingMap:
                _map = _bytes.AsSpan(p, length).ToArray();
                break;
            case Opcode.VideoData:
                DecodeVideo(p + VideoHeader, p + length);
                break;
            case Opcode.SendBuffer:
                (_last, _current) = (_current, _last);
                _shown = true;
                break;
            case Opcode.AudioFrame:
                if ((Bin.U16(_bytes, p + 2) & StreamMask) != 0)
                {
                    DecodeAudio(p + 6, p + length, Bin.U16(_bytes, p + 4));
                }

                break;
            case Opcode.SilenceFrame:
                if ((Bin.U16(_bytes, p + 2) & StreamMask) != 0)
                {
                    Audio.AddRange(Enumerable.Repeat(Vector2.Zero, Bin.U16(_bytes, p + 4) / (2 * Channels)));
                }

                break;
        }
    }

    private void InitVideo(int width, int height)
    {
        Width = width;
        Height = height;
        _current = new byte[width * height];
        _last = new byte[width * height];
    }

    /// <summary>6-bit levels scaled to 8 bits as ffmpeg does (v × 4 + v / 16).</summary>
    private void SetPalette(int p)
    {
        int start = Bin.U16(_bytes, p);
        int count = Bin.U16(_bytes, p + 2);
        p += 4;
        for (var i = 0; i < count * 3; i++)
        {
            var index = start * 3 + i;
            if (index >= Palette.Length)
            {
                break;
            }

            var v = _bytes[p + i] & 0x3F;
            Palette[index] = (byte)((v << 2) | (v >> 4));
        }
    }

    private void DecodeAudio(int p, int end, int size)
    {
        if ((_audioFlags & AudioCompressed) == 0)
        {
            DecodePcm(p, end);
            return;
        }

        var samples = new int[size / 2];
        var count = 0;
        for (var ch = 0; ch < Channels; ch++)
        {
            _predictors[ch] = Bin.S16(_bytes, p);
            samples[count++] = _predictors[ch];
            p += 2;
        }

        var channel = 0;
        while (count < samples.Length && p < end)
        {
            _predictors[channel] = Math.Clamp(_predictors[channel] + Deltas[_bytes[p]], short.MinValue, short.MaxValue);
            samples[count++] = _predictors[channel];
            p++;
            channel ^= Channels - 1;
        }

        PushSamples(samples, count);
    }

    private void DecodePcm(int p, int end)
    {
        var samples = new List<int>();
        var step = (_audioFlags & Audio16Bit) != 0 ? 2 : 1;
        for (; p + step <= end; p += step)
        {
            samples.Add(step == 2 ? Bin.S16(_bytes, p) : (_bytes[p] - ByteMiddle) << 8);
        }

        PushSamples([.. samples], samples.Count);
    }

    private void PushSamples(int[] samples, int count)
    {
        if (Channels == 1)
        {
            for (var i = 0; i < count; i++)
            {
                Audio.Add(Vector2.One * samples[i] * SampleScale);
            }

            return;
        }

        for (var i = 0; i + 1 < count; i += 2)
        {
            Audio.Add(new Vector2(samples[i], samples[i + 1]) * SampleScale);
        }
    }

    // Video: one opcode (4 bits of the map, low nibble first) per 8x8 block.

    private void DecodeVideo(int p, int end)
    {
        var block = 0;
        for (var by = 0; by < Height; by += Block)
        {
            for (var bx = 0; bx < Width; bx += Block)
            {
                var code = (block & 1) != 0 ? _map[block >> 1] >> 4 : _map[block >> 1] & 0x0F;
                block++;
                p = DecodeBlock(code, bx, by, p);
                if (p > end)
                {
                    return;
                }
            }
        }
    }

    /// <summary>Decodes one block at (x, y) from the data at p; returns where the next block's data starts.</summary>
    private int DecodeBlock(int code, int x, int y, int p)
    {
        var o = y * Width + x;
        switch (code)
        {
            case 0x0:
                Copy(_last, o, 0, 0);
                return p;
            case 0x1:
                return p;
            case 0x2:
            {
                int b = _bytes[p];
                if (b < 56)
                {
                    Copy(_current, o, 8 + b % 7, b / 7);
                }
                else
                {
                    Copy(_current, o, -14 + (b - 56) % 29, 8 + (b - 56) / 29);
                }

                return p + 1;
            }
            case 0x3:
            {
                int b = _bytes[p];
                if (b < 56)
                {
                    Copy(_current, o, -(8 + b % 7), -(b / 7));
                }
                else
                {
                    Copy(_current, o, -(-14 + (b - 56) % 29), -(8 + (b - 56) / 29));
                }

                return p + 1;
            }
            case 0x4:
            {
                int b = _bytes[p];
                Copy(_last, o, -8 + (b & 0x0F), -8 + (b >> 4));
                return p + 1;
            }
            case 0x5:
                Copy(_last, o, (sbyte)_bytes[p], (sbyte)_bytes[p + 1]);
                return p + 2;
            case 0x7:
                return TwoColours(o, p);
            case 0x8:
                return TwoColourQuadrants(o, p);
            case 0x9:
                return FourColours(o, p);
            case 0xA:
                return FourColourQuadrants(o, p);
            case 0xB:
                for (var row = 0; row < Block; row++)
                {
                    _bytes.AsSpan(p + row * Block, Block).CopyTo(_current.AsSpan(o + row * Width));
                }

                return p + Block * Block;
            case 0xC:
                for (var i = 0; i < 16; i++)
                {
                    Fill(o + (i >> 2) * 2 * Width + (i & 3) * 2, 2, 2, _bytes[p + i]);
                }

                return p + 16;
            case 0xD:
                for (var i = 0; i < 4; i++)
                {
                    Fill(o + (i >> 1) * 4 * Width + (i & 1) * 4, 4, 4, _bytes[p + i]);
                }

                return p + 4;
            case 0xE:
                Fill(o, Block, Block, _bytes[p]);
                return p + 1;
            case 0xF:
                for (var row = 0; row < Block; row++)
                {
                    for (var col = 0; col < Block; col++)
                    {
                        _current[o + row * Width + col] = _bytes[p + ((row + col) & 1)];
                    }
                }

                return p + 2;
        }

        return p;
    }

    /// <summary>Copies the 8x8 block from <paramref name="source"/> at the block's place moved by (dx, dy).</summary>
    private void Copy(byte[] source, int o, int dx, int dy)
    {
        var from = o + dy * Width + dx;
        if (from < 0 || from + 7 * Width + Block > source.Length)
        {
            return;
        }

        for (var row = 0; row < Block; row++)
        {
            // Overlapping copies within the back buffer go pixel by pixel, as the original.
            for (var col = 0; col < Block; col++)
            {
                _current[o + row * Width + col] = source[from + row * Width + col];
            }
        }
    }

    private void Fill(int o, int w, int h, byte value)
    {
        for (var row = 0; row < h; row++)
        {
            _current.AsSpan(o + row * Width, w).Fill(value);
        }
    }

    /// <summary>Two colours: P0 ≤ P1, a bit per pixel (8 bytes, a row each, low bit first); else a
    /// bit per 2x2 (16 bits).</summary>
    private int TwoColours(int o, int p)
    {
        var p0 = _bytes[p];
        var p1 = _bytes[p + 1];
        p += 2;
        if (p0 <= p1)
        {
            for (var row = 0; row < Block; row++)
            {
                int flags = _bytes[p + row];
                for (var col = 0; col < Block; col++)
                {
                    _current[o + row * Width + col] = ((flags >> col) & 1) != 0 ? p1 : p0;
                }
            }

            return p + Block;
        }

        int bits = Bin.U16(_bytes, p);
        for (var i = 0; i < 16; i++)
        {
            Fill(o + (i >> 2) * 2 * Width + (i & 3) * 2, 2, 2, ((bits >> i) & 1) != 0 ? p1 : p0);
        }

        return p + 2;
    }

    /// <summary>Two colours per 4x4 quadrant (P0 ≤ P1: top-left, bottom-left, top-right,
    /// bottom-right, each with its colours and 16 bits), or per half (P2 ≤ P3: left and right, else
    /// top and bottom; 32 bits each).</summary>
    private int TwoColourQuadrants(int o, int p)
    {
        byte[] colours = [_bytes[p], _bytes[p + 1]];
        if (colours[0] <= colours[1])
        {
            for (var q = 0; q < 4; q++)
            {
                colours = [_bytes[p], _bytes[p + 1]];
                int bits16 = Bin.U16(_bytes, p + 2);
                p += 4;
                var qo = o + (q & 1) * 4 * Width + (q >> 1) * 4;
                for (var i = 0; i < 16; i++)
                {
                    _current[qo + (i >> 2) * Width + (i & 3)] = colours[(bits16 >> i) & 1];
                }
            }

            return p;
        }

        var bits = Bin.U32(_bytes, p + 2);
        byte[] other = [_bytes[p + 6], _bytes[p + 7]];
        p += 8;
        var sideBySide = other[0] <= other[1];
        for (var half = 0; half < 2; half++)
        {
            if (half == 1)
            {
                colours = other;
                bits = Bin.U32(_bytes, p);
                p += 4;
            }

            for (var i = 0; i < 32; i++)
            {
                var c = colours[(bits >> i) & 1];
                var at = sideBySide ? o + (i >> 2) * Width + half * 4 + (i & 3) : o + (half * 4 + (i >> 3)) * Width + (i & 7);
                _current[at] = c;
            }
        }

        return p;
    }

    /// <summary>Four colours: P0 ≤ P1 and P2 ≤ P3: 2 bits per pixel (16 bytes); P0 ≤ P1: per 2x2 (4
    /// bytes); else 64 bits per 2x1 (P2 ≤ P3) or 1x2 pairs.</summary>
    private int FourColours(int o, int p)
    {
        byte[] colours = [_bytes[p], _bytes[p + 1], _bytes[p + 2], _bytes[p + 3]];
        p += 4;
        if (colours[0] <= colours[1])
        {
            if (colours[2] <= colours[3])
            {
                for (var row = 0; row < Block; row++)
                {
                    int flags = Bin.U16(_bytes, p + row * 2);
                    for (var col = 0; col < Block; col++)
                    {
                        _current[o + row * Width + col] = colours[(flags >> (col * 2)) & 3];
                    }
                }

                return p + 16;
            }

            var bits = Bin.U32(_bytes, p);
            for (var i = 0; i < 16; i++)
            {
                Fill(o + (i >> 2) * 2 * Width + (i & 3) * 2, 2, 2, colours[(bits >> (i * 2)) & 3]);
            }

            return p + 4;
        }

        var all = (ulong)Bin.U32(_bytes, p) | ((ulong)Bin.U32(_bytes, p + 4) << 32);
        for (var i = 0; i < 32; i++)
        {
            var c = colours[(all >> (i * 2)) & 3];
            if (colours[2] <= colours[3])
            {
                Fill(o + (i >> 2) * Width + (i & 3) * 2, 2, 1, c);
            }
            else
            {
                Fill(o + (i >> 3) * 2 * Width + (i & 7), 1, 2, c);
            }
        }

        return p + 8;
    }

    /// <summary>Four colours per 4x4 quadrant (P0 ≤ P1: each with its colours and 32 bits), or per
    /// half (P4 ≤ P5 of the second half: left and right, else top and bottom; 64 bits each).</summary>
    private int FourColourQuadrants(int o, int p)
    {
        byte[] colours = [_bytes[p], _bytes[p + 1], _bytes[p + 2], _bytes[p + 3]];
        if (colours[0] <= colours[1])
        {
            for (var q = 0; q < 4; q++)
            {
                colours = [_bytes[p], _bytes[p + 1], _bytes[p + 2], _bytes[p + 3]];
                var bits = Bin.U32(_bytes, p + 4);
                p += 8;
                var qo = o + (q & 1) * 4 * Width + (q >> 1) * 4;
                for (var i = 0; i < 16; i++)
                {
                    _current[qo + (i >> 2) * Width + (i & 3)] = colours[(bits >> (i * 2)) & 3];
                }
            }

            return p;
        }

        var all = (ulong)Bin.U32(_bytes, p + 4) | ((ulong)Bin.U32(_bytes, p + 8) << 32);
        byte[] other = [_bytes[p + 12], _bytes[p + 13], _bytes[p + 14], _bytes[p + 15]];
        p += 16;
        var sideBySide = other[0] <= other[1];
        for (var half = 0; half < 2; half++)
        {
            if (half == 1)
            {
                colours = other;
                all = (ulong)Bin.U32(_bytes, p) | ((ulong)Bin.U32(_bytes, p + 4) << 32);
                p += 8;
            }

            for (var i = 0; i < 32; i++)
            {
                var c = colours[(all >> (i * 2)) & 3];
                var at = sideBySide ? o + (i >> 2) * Width + half * 4 + (i & 3) : o + (half * 4 + (i >> 3)) * Width + (i & 7);
                _current[at] = c;
            }
        }

        return p;
    }
}
