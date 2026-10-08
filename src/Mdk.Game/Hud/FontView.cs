using System.Drawing;
using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Formats;
using Mdk.Game.Mods;

namespace Mdk.Game.Hud;

/// <summary>Draws a font of <c>MDKFONT.FTI</c> like the original (0x415a20): glyphs from
/// <c>ascent</c> rows above the baseline to <c>descent</c> below, scaled about the baseline
/// (0x415d8c). The glyphs sit side by side in one index texture.
/// <code>
///   atlas: | A | B | C | ... |   each glyph at its x, top at (max ascent - ascent)
/// </code></summary>
public sealed class FontView
{
    private readonly Renderer _renderer;
    private readonly Font _font;
    private readonly int _texture;
    private readonly int _palette;
    private readonly int[] _x = new int[256];
    private readonly int _maxAscent;
    private readonly Vector2 _size;

    /// <summary>The fonts' names in <c>MDKFONT.FTI</c>, and in mods (their atlas, <see cref="Atlas"/>).</summary>
    public const string Big = "FONTBIG";
    public const string Small = "FONTSML";

    /// <summary>A font through a palette; a mod's image of its atlas (<paramref name="name"/>) replaces it.</summary>
    public FontView(Renderer renderer, Font font, Palette palette, ModImages? mods = null, string name = "")
        : this(renderer, font, renderer.CreatePalette(palette.Rgba))
    {
        CanvasImages.Replace(renderer, mods, name, Atlas(font, name), palette, _texture, _palette);
    }

    /// <summary>A font drawn through a palette the caller owns (the fall's, with its effects).</summary>
    public FontView(Renderer renderer, Font font, int palette)
    {
        _renderer = renderer;
        _font = font;
        var atlas = Atlas(font, "", _x);
        _maxAscent = font.Glyphs.Max(g => g?.Ascent ?? 0);
        _size = new Vector2(atlas.Width, atlas.Height);
        _texture = renderer.CreateIndexTexture(atlas.Width, atlas.Height, atlas.Indices);
        _palette = palette;
    }

    /// <summary>The glyphs side by side, one column apart, tops at (max ascent - ascent); each
    /// glyph's x into <paramref name="x"/> when given (a mod's image of it: any size, same layout).</summary>
    public static Texture Atlas(Font font, string name, int[]? x = null)
    {
        var glyphs = font.Glyphs;
        var maxAscent = glyphs.Max(g => g?.Ascent ?? 0);
        var maxDescent = glyphs.Max(g => g?.Descent ?? 0);
        var width = Math.Max(1, glyphs.Sum(g => g == null ? 0 : g.Width + 1));
        var height = maxAscent + maxDescent + 1;
        var indices = new byte[width * height];
        var at = 0;
        for (var c = 0; c < glyphs.Length; c++)
        {
            if (glyphs[c] is not { } glyph)
            {
                continue;
            }

            var top = maxAscent - glyph.Ascent;
            for (var row = 0; row < glyph.Ascent + glyph.Descent + 1; row++)
            {
                glyph.Indices.AsSpan(row * glyph.Width, glyph.Width).CopyTo(indices.AsSpan((top + row) * width + at));
            }

            if (x != null)
            {
                x[c] = at;
            }

            at += glyph.Width + 1;
        }

        return new Texture { Name = name, Width = width, Height = height, Indices = indices };
    }

    public int Width(ReadOnlySpan<byte> text) => _font.Width(text);

    /// <summary>Draws a text from <paramref name="x"/> along the baseline <paramref name="y"/>,
    /// scaled about the baseline. Returns the x after it.</summary>
    public float Draw(ReadOnlySpan<byte> text, float x, float y, float scale = 1f, float alpha = 1f)
    {
        foreach (var c in text)
        {
            if (_font.Glyphs[c] is not { } glyph)
            {
                x += _font.SpaceWidth * scale;
                continue;
            }

            var height = glyph.Ascent + glyph.Descent + 1;
            var source = new RectangleF(_x[c], _maxAscent - glyph.Ascent, glyph.Width, height);
            var target = new RectangleF(x, y - glyph.Ascent * scale, glyph.Width * scale, height * scale);
            _renderer.DrawImage(_texture, _palette, _size, source, target, new Vector4(1f, 1f, 1f, alpha));
            x += glyph.Width * scale;
        }

        return x;
    }
}
