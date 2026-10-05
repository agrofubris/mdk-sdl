namespace Mdk.Formats;

/// <summary>A font of <c>MISC/MDKFONT.FTI</c> (<c>FONTBIG</c>, <c>FONTSML</c>).
/// <code>
/// u32 glyph offsets[256] (from the font, 0 = none), then per glyph:
///   s8 ascent, s8 descent, u8 width, (ascent + descent + 1) x width palette indices
/// </code>
/// A glyph spans <c>ascent</c> rows above the baseline to <c>descent</c> below. Characters without a
/// glyph are spaces. Pixels use the palette's first 64 colours (<c>SYS_PAL</c>); 0 is transparent.</summary>
public sealed class Font
{
    private const int CharacterCount = 256;

    public sealed record Glyph(int Ascent, int Descent, int Width, byte[] Indices);

    /// <summary>Per character, null without a glyph.</summary>
    public Glyph?[] Glyphs { get; } = new Glyph?[CharacterCount];
    public int SpaceWidth { get; }

    private Font(int spaceWidth) => SpaceWidth = spaceWidth;

    /// <summary>Space widths: 6 in <c>FONTBIG</c>, 4 in <c>FONTSML</c>.</summary>
    public static Font Parse(byte[] data, int spaceWidth)
    {
        var font = new Font(spaceWidth);
        for (var c = 0; c < CharacterCount; c++)
        {
            var offset = (int)Bin.U32(data, c * 4);
            if (offset == 0)
            {
                continue;
            }

            int ascent = (sbyte)data[offset];
            int descent = (sbyte)data[offset + 1];
            int width = data[offset + 2];
            var size = (ascent + descent + 1) * width;
            font.Glyphs[c] = new Glyph(ascent, descent, width, data.AsSpan(offset + 3, size).ToArray());
        }

        return font;
    }

    /// <summary>Width of a text in pixels (0x4159d4).</summary>
    public int Width(ReadOnlySpan<byte> text)
    {
        var width = 0;
        foreach (var c in text)
        {
            width += Glyphs[c]?.Width ?? SpaceWidth;
        }

        return width;
    }
}
