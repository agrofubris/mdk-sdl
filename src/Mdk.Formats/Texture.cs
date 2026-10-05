namespace Mdk.Formats;

/// <summary>An 8-bit paletted texture: <c>u16 width, u16 height</c>, then palette indices row by row.
/// Animated textures keep their frames one after another.</summary>
public sealed class Texture
{
    /// <summary>Animated kind whose frames are changes to the first one (<c>M_COMM</c>).</summary>
    public const uint Deltas = 0x20000;
    private const int DeltaUnit = 4;

    public string Name { get; init; } = "";
    public int Width { get; init; }
    /// <summary>Height of one frame.</summary>
    public int Height { get; init; }
    public int FrameCount { get; init; } = 1;
    /// <summary>Palette indices of all frames.</summary>
    public byte[] Indices { get; init; } = [];

    public static Texture Parse(string name, byte[] bytes, int offset)
    {
        int width = Bin.U16(bytes, offset);
        int height = Bin.U16(bytes, offset + 2);
        return new Texture
        {
            Name = name,
            Width = width,
            Height = height,
            Indices = bytes.AsSpan(offset + 4, width * height).ToArray(),
        };
    }

    /// <summary><c>u32 frame count, u16 width, u16 height</c>, then the frames back to back, or with
    /// <see cref="Deltas"/> the first frame and the changes making each next one (0x422c00).</summary>
    public static Texture ParseAnimated(string name, byte[] bytes, int offset, uint kind)
    {
        var count = (int)Bin.U32(bytes, offset);
        int width = Bin.U16(bytes, offset + 4);
        int height = Bin.U16(bytes, offset + 6);
        var size = width * height;
        var indices = (kind & Deltas) != 0
            ? ApplyDeltas(bytes, offset + 8, size, count)
            : bytes.AsSpan(offset + 8, size * count).ToArray();
        return new Texture { Name = name, Width = width, Height = height, FrameCount = count, Indices = indices };
    }

    /// <summary>After the first frame: <c>f32</c>, then <c>u32</c> offsets (from after it) to 2n blocks,
    /// n controls then n data. A control is <c>u16 start, u16 runs</c>, then per run <c>u8 copy, u8 skip</c>
    /// in units of 4 pixels. Change k turns frame k into frame k + 1.</summary>
    private static byte[] ApplyDeltas(byte[] bytes, int p, int size, int count)
    {
        var frame = bytes.AsSpan(p, size).ToArray();
        var frames = new byte[size * count];
        frame.CopyTo(frames, 0);
        var table = p + size + 4;
        for (var k = 0; k < count - 1; k++)
        {
            var control = table + (int)Bin.U32(bytes, table + k * 4);
            var data = table + (int)Bin.U32(bytes, table + (count + k) * 4);
            var destination = Bin.U16(bytes, control) * DeltaUnit;
            int runs = Bin.U16(bytes, control + 2);
            for (var run = 0; run < runs; run++)
            {
                var copy = bytes[control + 4 + run * 2] * DeltaUnit;
                var skip = bytes[control + 5 + run * 2] * DeltaUnit;
                for (var i = 0; i < copy; i++)
                {
                    if (destination + i < size)
                    {
                        frame[destination + i] = bytes[data + i];
                    }
                }

                data += copy;
                destination += copy + skip;
            }

            frame.CopyTo(frames, (k + 1) * size);
        }

        return frames;
    }
}
