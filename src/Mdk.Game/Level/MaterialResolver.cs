using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Formats;

namespace Mdk.Game.Level;

/// <summary>Turns MDK material references (texture names, palette colours, special values) into
/// renderer materials, creating each texture and palette on the GPU once.
/// <code>
///   value &lt; 0          palette colour -value
///   value &gt;= 0         material name: texture, archive colour, PEN_n, NONE
///   colour 256-1028    special: glass (GLASS1-4), mirrors, NONE, PEN_ENV, RIPPLE
/// </code></summary>
public sealed class MaterialResolver(Renderer renderer, Dti dti)
{
    private const int SpecialFirst = 256;
    private const int MirrorFirst = 990;
    private const int MirrorLast = 1010;
    private const int GlassFirst = 1024;
    private const int GlassLast = 1027;
    private const string PenPrefix = "PEN_";
    private const float ByteToUnit = 1f / 255f;
    /// <summary>A mirror's panorama row is 8 rows per value from MIRRMED (1000): MIRRLOW (990) 80
    /// lower, MIRRHIGH (1010) 80 higher.</summary>
    private const int MirrorMiddle = 1000;
    private const float MirrorRowsPerValue = 8f;

    private readonly Dictionary<Texture, int> _textures = [];
    private readonly Dictionary<Palette, int> _palettes = [];

    /// <summary>A resolved surface and the texture whose texels its UVs count (null when flat).</summary>
    public readonly record struct Surface(Material Material, Texture? Texture);

    /// <summary>The surface of a triangle's material value, or null when it isn't drawn.</summary>
    public Surface? Resolve(int value, IReadOnlyList<string> names, Palette palette, IReadOnlyList<TextureArchive> archives, Pass pass)
    {
        if (value < 0)
        {
            return Colour(-value, palette, pass);
        }

        var name = names[value];
        foreach (var archive in archives)
        {
            if (archive.Textures.TryGetValue(name, out var texture))
            {
                var material = new Material(TextureId(texture), PaletteId(palette), Vector4.One, texture.FrameCount, pass);
                return new Surface(material, texture);
            }
        }

        foreach (var archive in archives)
        {
            if (archive.Colors.TryGetValue(name, out var index))
            {
                return Colour(index, palette, pass);
            }
        }

        if (name.StartsWith(PenPrefix) && int.TryParse(name.AsSpan(PenPrefix.Length), out var pen))
        {
            return Colour(pen, palette, pass);
        }

        return null;
    }

    private Surface? Colour(int index, Palette palette, Pass pass)
    {
        if (index < SpecialFirst)
        {
            var rgba = palette.Rgba;
            var colour = new Vector4(rgba[index * 4], rgba[index * 4 + 1], rgba[index * 4 + 2], byte.MaxValue) * ByteToUnit;
            return new Surface(Material.Flat(colour, pass), null);
        }

        // Glass: the level's colour blended by its alpha, both faces (0x471290).
        if (index is >= GlassFirst and <= GlassLast)
        {
            var glass = dti.Glass[index - GlassFirst];
            var colour = new Vector4(glass[0], glass[1], glass[2], glass[3]) * ByteToUnit;
            return new Surface(Material.Flat(colour, Pass.Blended), null);
        }

        if (index is >= MirrorFirst and <= MirrorLast)
        {
            return new Surface(Material.Mirror((MirrorMiddle - index) * MirrorRowsPerValue), null);
        }

        // NONE, PEN_ENV and RIPPLE aren't drawn (the Direct3D renderer skips RIPPLE).
        return null;
    }

    private int TextureId(Texture texture)
    {
        if (!_textures.TryGetValue(texture, out var id))
        {
            id = _textures[texture] = renderer.CreateIndexTexture(texture.Width, texture.Height * texture.FrameCount, texture.Indices);
        }

        return id;
    }

    private int PaletteId(Palette palette)
    {
        if (!_palettes.TryGetValue(palette, out var id))
        {
            id = _palettes[palette] = renderer.CreatePalette(palette.Rgba);
        }

        return id;
    }
}
