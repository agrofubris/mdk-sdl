namespace Mdk.Engine.Render;

/// <summary>The enhanced look's textures on the CPU: palette indices expanded to RGBA8, and their
/// mipmaps. Colours are premultiplied by their cover (alpha), index 0 being clear (0, 0, 0, 0), so
/// filtering weighs texels by cover and clear ones add no dark fringe. Each level halves the last
/// (a box filter); an odd size's last texel joins its neighbours' (3 texels).
/// <code>
///   level 0   r g r g      level 1   (r+g+r+g)/4 ...
///             r g r g
/// </code></summary>
public static class ColourMips
{
    private const int Channels = 4;
    private const int Alpha = 3;
    private const int Opaque = 255;

    /// <summary>Indices through an RGBA8 palette; index 0 (or one past the palette) is clear.</summary>
    public static byte[] Expand(ReadOnlySpan<byte> indices, ReadOnlySpan<byte> palette)
    {
        var rgba = new byte[indices.Length * Channels];
        for (var i = 0; i < indices.Length; i++)
        {
            var colour = indices[i] * Channels;
            if (indices[i] == 0 || colour + Channels > palette.Length)
            {
                continue;
            }

            palette.Slice(colour, Alpha).CopyTo(rgba.AsSpan(i * Channels));
            rgba[i * Channels + Alpha] = Opaque;
        }

        return rgba;
    }

    /// <summary>Levels from <paramref name="width"/> x <paramref name="height"/> down to 1 x 1.</summary>
    public static int Levels(int width, int height) => (int)Math.Log2(Math.Max(Math.Max(width, height), 1)) + 1;

    /// <summary>A size's next level.</summary>
    public static int Half(int size) => Math.Max(size / 2, 1);

    /// <summary>All levels of an RGBA8 image, the image first.</summary>
    public static List<byte[]> Chain(byte[] rgba, int width, int height)
    {
        var levels = new List<byte[]> { rgba };
        var count = Levels(width, height);
        for (var level = 1; level < count; level++)
        {
            levels.Add(Downsample(levels[^1], width, height));
            (width, height) = (Half(width), Half(height));
        }

        return levels;
    }

    /// <summary>The next level: each texel the mean of the 2 x 2 (up to 3 x 3 at odd edges) it covers.</summary>
    private static byte[] Downsample(byte[] rgba, int width, int height)
    {
        var (halfWidth, halfHeight) = (Half(width), Half(height));
        var result = new byte[halfWidth * halfHeight * Channels];
        Span<int> sum = stackalloc int[Channels];
        for (var y = 0; y < halfHeight; y++)
        {
            var (top, bottom) = Span(y, height, halfHeight);
            for (var x = 0; x < halfWidth; x++)
            {
                var (left, right) = Span(x, width, halfWidth);
                sum.Clear();
                for (var sy = top; sy < bottom; sy++)
                {
                    for (var sx = left; sx < right; sx++)
                    {
                        for (var c = 0; c < Channels; c++)
                        {
                            sum[c] += rgba[(sy * width + sx) * Channels + c];
                        }
                    }
                }

                // Rounded to the nearest.
                var count = (bottom - top) * (right - left);
                for (var c = 0; c < Channels; c++)
                {
                    result[(y * halfWidth + x) * Channels + c] = (byte)((sum[c] + count / 2) / count);
                }
            }
        }

        return result;
    }

    /// <summary>The source texels [first, last) a texel of the next level covers: two, the whole
    /// size when it's 1, and the odd one left over joins the last.</summary>
    private static (int First, int Last) Span(int index, int size, int half)
    {
        var first = Math.Min(index * 2, size - 1);
        var last = index == half - 1 ? size : first + 2;
        return (first, last);
    }
}
