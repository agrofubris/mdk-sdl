namespace Mdk.Formats;

/// <summary>A 256-colour palette for MDK's 8-bit textures.</summary>
public sealed class Palette
{
    public const int Size = 256;
    /// <summary>First index replaced by an arena's colours (<see cref="WithArenaColors"/>).</summary>
    public const int ArenaFirstIndex = 64;

    /// <summary>RGBA8, 4 bytes per index.</summary>
    public byte[] Rgba { get; } = new byte[Size * 4];

    public static Palette FromRgb(ReadOnlySpan<byte> rgb)
    {
        var palette = new Palette();
        for (var i = 0; i < Math.Min(Size, rgb.Length / 3); i++)
        {
            palette.Rgba[i * 4] = rgb[i * 3];
            palette.Rgba[i * 4 + 1] = rgb[i * 3 + 1];
            palette.Rgba[i * 4 + 2] = rgb[i * 3 + 2];
            palette.Rgba[i * 4 + 3] = 255;
        }

        // The game forces index 0 to black; sprites treat it as transparent.
        palette.Rgba[0] = palette.Rgba[1] = palette.Rgba[2] = 0;
        return palette;
    }

    /// <summary>A copy with an arena's 112 colours at indices 64–175.</summary>
    public Palette WithArenaColors(ReadOnlySpan<byte> arenaRgb)
    {
        var palette = new Palette();
        Rgba.CopyTo(palette.Rgba, 0);
        for (var i = 0; i < arenaRgb.Length / 3; i++)
        {
            var index = ArenaFirstIndex + i;
            palette.Rgba[index * 4] = arenaRgb[i * 3];
            palette.Rgba[index * 4 + 1] = arenaRgb[i * 3 + 1];
            palette.Rgba[index * 4 + 2] = arenaRgb[i * 3 + 2];
        }

        return palette;
    }
}
