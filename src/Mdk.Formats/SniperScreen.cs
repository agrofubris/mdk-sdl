namespace Mdk.Formats;

/// <summary>Sniper mode's screen in <c>TRAVSPRT.BNI</c> (godot-mdk docs/gameplay.md "Sniper mode"):
/// <c>SNIPERS1</c>, the 640x480 frame (palette indices, no header), and <c>SNIPERS2</c>, the mask
/// over the 600x360 view with holes for the scope and the round cameras.
/// <code>
/// SNIPERS2: u32 size, then u16 words over 600-pixel rows:
///   below 0x8000  that many x 4 literal indices follow
///   0x8nnn        nnn transparent pixels
///   0xFFnn        nn literal indices (1-3)
///   0xFF00        end
/// </code>
/// Palette index 0 is transparent.</summary>
public static class SniperScreen
{
    public const int ScreenWidth = 640;
    public const int ScreenHeight = 480;
    public const int ViewWidth = 600;
    public const int ViewHeight = 360;

    private const string FrameName = "SNIPERS1";
    private const string MaskName = "SNIPERS2";
    private const int SizeField = 4;
    private const int WordSize = 2;
    private const ushort End = 0xFF00;
    private const ushort ShortLiteral = 0xFF00;
    private const ushort Skip = 0x8000;
    private const int SkipMask = 0xFFF;
    private const int ShortCountMask = 0xFF;
    private const int LiteralUnit = 4;

    /// <summary>The 640x480 frame's palette indices.</summary>
    public static Texture Frame(Bni sprites)
    {
        var offset = sprites.Entries[FrameName].Offset;
        var indices = sprites.Bytes.AsSpan(offset, ScreenWidth * ScreenHeight).ToArray();
        return new Texture { Name = FrameName, Width = ScreenWidth, Height = ScreenHeight, Indices = indices };
    }

    /// <summary>The 600x360 mask's palette indices (0 in its holes).</summary>
    public static Texture Mask(Bni sprites) =>
        new() { Name = MaskName, Width = ViewWidth, Height = ViewHeight, Indices = DecodeMask(sprites.Bytes, sprites.Entries[MaskName].Offset + SizeField) };

    /// <summary>Decodes the mask's words from <paramref name="offset"/>.</summary>
    public static byte[] DecodeMask(byte[] bytes, int offset)
    {
        var indices = new byte[ViewWidth * ViewHeight];
        var pixel = 0;
        while (pixel < indices.Length && offset + WordSize <= bytes.Length)
        {
            var word = Bin.U16(bytes, offset);
            offset += WordSize;
            if (word == End)
            {
                break;
            }

            int count;
            if ((word & ShortLiteral) == ShortLiteral)
            {
                count = word & ShortCountMask;
            }
            else if ((word & Skip) != 0)
            {
                pixel += word & SkipMask;
                continue;
            }
            else
            {
                count = word * LiteralUnit;
            }

            for (var i = 0; i < count && pixel < indices.Length; i++, pixel++)
            {
                indices[pixel] = bytes[offset + i];
            }

            offset += count;
        }

        return indices;
    }
}
