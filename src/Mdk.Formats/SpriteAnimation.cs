namespace Mdk.Formats;

/// <summary>An RLE sprite animation, such as Kurt's <c>K_RUN</c> in <c>TRAVSPRT.BNI</c>.
/// <code>
/// u32 frame count, u32 frame offsets[count] (from the count), then per frame:
///   u16 width, u16 height, s16 hotspot x, s16 hotspot y, rows of commands:
///   0x00-0x7F  n + 1 literal indices follow
///   0x80-0xFD  the next index repeated n - 0x7C times
///   0xFE       end of row (the rest is transparent)
///   0xFF       end of frame
/// </code>
/// Palette index 0 is transparent.</summary>
public sealed class SpriteAnimation
{
    private const byte LiteralLast = 0x7F;
    private const byte EndOfRow = 0xFE;
    private const int RepeatBias = 0x7C;

    /// <summary>A frame and its anchor. Kurt's top-left corner is drawn at (feet x - hotspot x,
    /// feet y - 101 - hotspot y) on a 600x360 view.</summary>
    public sealed record Frame(Texture Image, int HotspotX, int HotspotY);

    private readonly byte[] _bytes;
    private readonly int _base;
    private readonly Dictionary<int, Frame> _frames = [];

    public string Name { get; }
    public int FrameCount { get; }

    private SpriteAnimation(string name, byte[] bytes, int offset)
    {
        Name = name;
        _bytes = bytes;
        _base = offset;
        FrameCount = (int)Bin.U32(bytes, offset);
    }

    public static SpriteAnimation Parse(string name, byte[] bytes, int offset) => new(name, bytes, offset);

    /// <summary>Frame <paramref name="index"/>, decoded on first use.</summary>
    public Frame GetFrame(int index)
    {
        if (!_frames.TryGetValue(index, out var frame))
        {
            frame = _frames[index] = Decode(_base + (int)Bin.U32(_bytes, _base + 4 + index * 4));
        }

        return frame;
    }

    private Frame Decode(int offset)
    {
        int width = Bin.U16(_bytes, offset);
        int height = Bin.U16(_bytes, offset + 2);
        var indices = new byte[width * height];
        var p = offset + 8;
        for (var y = 0; y < height; y++)
        {
            var i = y * width;
            while (true)
            {
                var command = _bytes[p++];
                if (command >= EndOfRow)
                {
                    break;
                }

                if (command <= LiteralLast)
                {
                    _bytes.AsSpan(p, command + 1).CopyTo(indices.AsSpan(i));
                    i += command + 1;
                    p += command + 1;
                    continue;
                }

                var value = _bytes[p++];
                indices.AsSpan(i, command - RepeatBias).Fill(value);
                i += command - RepeatBias;
            }
        }

        var image = new Texture { Name = Name, Width = width, Height = height, Indices = indices };
        return new Frame(image, Bin.S16(_bytes, offset + 4), Bin.S16(_bytes, offset + 6));
    }
}
