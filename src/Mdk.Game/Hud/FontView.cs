using System.Drawing;
using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Formats;

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

    public FontView(Renderer renderer, Font font, Palette palette)
    {
        _renderer = renderer;
        _font = font;
        var glyphs = font.Glyphs;
        _maxAscent = glyphs.Max(g => g?.Ascent ?? 0);
        var maxDescent = glyphs.Max(g => g?.Descent ?? 0);
        var width = Math.Max(1, glyphs.Sum(g => g == null ? 0 : g.Width + 1));
        var height = _maxAscent + maxDescent + 1;
        var indices = new byte[width * height];
        var x = 0;
        for (var c = 0; c < glyphs.Length; c++)
        {
            if (glyphs[c] is not { } glyph)
            {
                continue;
            }

            var top = _maxAscent - glyph.Ascent;
            for (var row = 0; row < glyph.Ascent + glyph.Descent + 1; row++)
            {
                glyph.Indices.AsSpan(row * glyph.Width, glyph.Width).CopyTo(indices.AsSpan((top + row) * width + x));
            }

            _x[c] = x;
            x += glyph.Width + 1;
        }

        _size = new Vector2(width, height);
        _texture = renderer.CreateIndexTexture(width, height, indices);
        _palette = renderer.CreatePalette(palette.Rgba);
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
