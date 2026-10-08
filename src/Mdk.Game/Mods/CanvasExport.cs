using Mdk.Formats;
using Mdk.Game.HdTextures;
using Mdk.Game.Hud;
using Mdk.Game.Level;

namespace Mdk.Game.Mods;

/// <summary>The canvas's 2D images as the screens draw them (<see cref="CanvasImages"/>), for the
/// exports and the HD textures: by their names in mods, through the palettes they're drawn with.
/// Not listed (mods can still replace them by name): the statistics' pages, the stream's HUD.
/// <code>
///   a level (its palette): the HUD, sniper mode's screen, the bomber's sight
///   the menus (level 0):   MDKOPT, LOAD_n, the falls' HUD (FALLPn), the fonts (exports only)
/// </code></summary>
public static class CanvasExport
{
    /// <summary>The menus' images go to no level's folder.</summary>
    public const int NoLevel = 0;
    private const int RgbSize = 3;
    private const string Options = "MISC/OPTIONS.BNI";
    private const string Background = "MDKOPT";
    private const string Falls = "FALL3D/FALL3D.BNI";
    private const string FallPalette = "FALLP";
    private const int FallCount = 5;
    private const string SystemPalette = "SYS_PAL";

    /// <summary>Whether the fonts are listed: exports yes, AI upscaling no (it blurs them).</summary>
    public enum Fonts { Listed, Skipped }

    public static IEnumerable<HdSource> Level(LevelData level, Bni sprites)
    {
        var palette = level.Dti.Palette;
        var images = HudView.Images(sprites).Concat(SniperOverlay.Images(sprites)).Concat(BomberOverlay.Images(sprites));
        return images.Select(i => Source(level.Number, i.Name, i.Texture, palette));
    }

    public static IEnumerable<HdSource> Menus(MdkData data, Fonts fonts)
    {
        var options = Bni.Load(data.PathOf(Options));
        if (options.Has(Background))
        {
            var offset = options.Entries[Background].Offset;
            yield return Source(NoLevel, Background, Texture.Parse(Background, options.Bytes, offset + Palette.Size * RgbSize), PaletteAt(options.Bytes, offset));
        }

        foreach (var level in HdOptions.AllLevels)
        {
            var path = data.PathOf($"MISC/LOAD_{level}.LBB");
            if (File.Exists(path) && File.ReadAllBytes(path) is { Length: > Palette.Size * RgbSize } bytes)
            {
                yield return Source(NoLevel, $"LOAD_{level}", Texture.Parse("LOAD", bytes, Palette.Size * RgbSize), PaletteAt(bytes, 0));
            }
        }

        var falls = Bni.Load(data.PathOf(Falls));
        for (var n = 1; n <= FallCount; n++)
        {
            var palette = PaletteAt(falls.Bytes, falls.Entries[$"{FallPalette}{n}"].Offset);
            foreach (var (name, texture) in HudView.Images(falls))
            {
                yield return Source(NoLevel, name, texture, palette);
            }
        }

        if (fonts == Fonts.Skipped)
        {
            yield break;
        }

        var fti = Fti.Load(data.PathOf("MISC/MDKFONT.FTI"));
        var system = Palette.FromRgb(fti.GetBytes(SystemPalette));
        foreach (var name in new[] { FontView.Big, FontView.Small })
        {
            yield return Source(NoLevel, name, FontView.Atlas(Font.Parse(fti.GetBytes(name), 0), name), system);
        }
    }

    private static Palette PaletteAt(byte[] bytes, int offset) => Palette.FromRgb(bytes.AsSpan(offset, Palette.Size * RgbSize));

    /// <summary>A 2D image: soft cut-outs (the canvas blends its edges).</summary>
    private static HdSource Source(int level, string name, Texture texture, Palette palette) =>
        new(level, name, HdKey.Of(texture, palette), texture, palette, HdAlpha.Soft, ModImages.Kind.Images);
}
