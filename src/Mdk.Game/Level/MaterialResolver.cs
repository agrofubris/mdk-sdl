using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Formats;

namespace Mdk.Game.Level;

/// <summary>Turns MDK material references (texture names, palette colours, special values) into
/// renderer materials, creating each texture and palette on the GPU once. Opaque surfaces take
/// <paramref name="shading"/> (the look); glass and mirrors keep the original's.
/// <code>
///   value &lt; 0          palette colour -value
///   value &gt;= 0         material name: texture, archive colour, PEN_n, NONE
///   colour 256-1028    special: glass (GLASS1-4), mirrors, NONE, PEN_ENV, RIPPLE
/// </code></summary>
public sealed class MaterialResolver(Renderer renderer, Dti dti, Shading shading = Shading.Original)
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

        // Index loops: a foreach over the interfaces would make an enumerator per call (every frame's pieces).
        var name = names[value];
        for (var i = 0; i < archives.Count; i++)
        {
            if (archives[i].Textures.TryGetValue(name, out var texture))
            {
                var material = new Material(TextureId(texture), PaletteId(palette), Vector4.One, texture.FrameCount, pass, Shading: ShadingOf(pass));
                renderer.Prepare(material);
                return new Surface(material, texture);
            }
        }

        for (var i = 0; i < archives.Count; i++)
        {
            if (archives[i].Colors.TryGetValue(name, out var index))
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

    /// <summary>Whether a material value is drawn (<see cref="Resolve"/> isn't null), without the GPU.</summary>
    public static bool IsDrawn(int value, IReadOnlyList<string> names, IReadOnlyList<TextureArchive> archives)
    {
        if (value < 0)
        {
            return IsDrawnColour(-value);
        }

        var name = names[value];
        if (archives.Any(a => a.Textures.ContainsKey(name)))
        {
            return true;
        }

        foreach (var archive in archives)
        {
            if (archive.Colors.TryGetValue(name, out var index))
            {
                return IsDrawnColour(index);
            }
        }

        return name.StartsWith(PenPrefix) && int.TryParse(name.AsSpan(PenPrefix.Length), out var pen) && IsDrawnColour(pen);
    }

    /// <summary>Palette colours, glass and mirrors are drawn; other specials aren't.</summary>
    private static bool IsDrawnColour(int index) =>
        index < SpecialFirst || index is >= GlassFirst and <= GlassLast or >= MirrorFirst and <= MirrorLast;

    private Surface? Colour(int index, Palette palette, Pass pass)
    {
        if (index < SpecialFirst)
        {
            var rgba = palette.Rgba;
            var colour = new Vector4(rgba[index * 4], rgba[index * 4 + 1], rgba[index * 4 + 2], byte.MaxValue) * ByteToUnit;
            return new Surface(Material.Flat(colour, pass) with { Shading = ShadingOf(pass) }, null);
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

    private Shading ShadingOf(Pass pass) => pass is Pass.Solid or Pass.DoubleSided ? shading : Shading.Original;

    /// <summary>Puts every texture of <paramref name="archives"/> and <paramref name="palette"/> on
    /// the GPU (a level's load: no frame creates any). The enhanced look's colour textures are made as
    /// surfaces resolve (every texture through every palette would take seconds).</summary>
    public void Preload(Palette palette, IReadOnlyList<TextureArchive> archives)
    {
        PaletteId(palette);
        foreach (var archive in archives)
        {
            foreach (var texture in archive.Textures.Values)
            {
                TextureId(texture);
            }
        }
    }

    /// <summary>Uploads a texture's pixels again if it's on the GPU (a bullet hole changed them).</summary>
    public void Refresh(Texture texture)
    {
        if (_textures.TryGetValue(texture, out var id))
        {
            renderer.UpdateTexture(id, texture.Width, texture.Height * texture.FrameCount, texture.Indices);
        }
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
