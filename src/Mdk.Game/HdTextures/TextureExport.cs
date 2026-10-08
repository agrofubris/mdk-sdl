using Mdk.Engine.Render;
using Mdk.Formats;
using Mdk.Game.Kurt;
using Mdk.Game.Level;

namespace Mdk.Game.HdTextures;

/// <summary>A texture as a level shows it: through an arena's palette; its HD image's cover.</summary>
public sealed record HdSource(int Level, string Name, string Key, Texture Texture, Palette Palette, HdAlpha Alpha = HdAlpha.Hard);

/// <summary>The textures the enhanced look draws lit, as the game resolves them (<see cref="MaterialResolver"/>):
/// each arena's and corridor's surfaces and models through its palette, and the level's models
/// (the scripts' objects) through every arena's palette, each distinct image once (<see cref="HdKey"/>);
/// and Kurt's sprite frames through the level's palette (<see cref="Kurt"/>; cut-outs: index 0 clear).
/// Not exported: the sky, the HUD, the fonts, the 2D screens, the fall's and the stream's Kurt (the
/// original look only).
/// <code>
///   arena ─► its materials, its models' ─┐
///   level models (CMI) ──────────────────┴─► names ─► archives (arena, level, other arenas) ─► texture × palette
/// </code></summary>
public static class TextureExport
{
    public static List<HdSource> Collect(LevelData level, Cmi cmi)
    {
        var sources = new Dictionary<string, HdSource>();
        var levelModels = cmi.ModelOffsets.Keys.Select(cmi.GetModel).OfType<Model>().ToList();
        foreach (var arena in level.Arenas)
        {
            var palette = level.PaletteOf(arena);
            var archives = level.ArchivesOf(arena);
            var names = arena.Materials
                .Concat(arena.Models.Values.SelectMany(m => m.Materials))
                .Concat(levelModels.SelectMany(m => m.Materials));
            foreach (var name in names.Distinct())
            {
                if (Find(name, archives) is not { } texture)
                {
                    continue;
                }

                var key = HdKey.Of(texture, palette);
                sources.TryAdd(key, new HdSource(level.Number, name, key, texture, palette));
            }
        }

        return [.. sources.Values];
    }

    /// <summary>Kurt's frames as a level draws them (<see cref="KurtSprite"/>): the level's own first
    /// (<paramref name="levelAnimation"/>: the snowboard's, the slide's), else TRAVSPRT.BNI's; each distinct image once.</summary>
    public static List<HdSource> Kurt(LevelData level, Bni sprites, Func<string, SpriteAnimation?> levelAnimation)
    {
        var sources = new Dictionary<string, HdSource>();
        var palette = level.Dti.Palette;
        foreach (var name in KurtSprite.Drawn)
        {
            var own = KurtSprite.LevelAnimations.Contains(name) ? levelAnimation(name) : null;
            var animation = own ?? (sprites.Has(name) ? sprites.GetAnimation(name) : null);
            for (var frame = 0; frame < (animation?.FrameCount ?? 0); frame++)
            {
                var texture = animation!.GetFrame(frame).Image;
                var key = HdKey.Of(texture, palette);
                sources.TryAdd(key, new HdSource(level.Number, $"{name}_{frame}", key, texture, palette, HdAlpha.Soft));
            }
        }

        return [.. sources.Values];
    }

    /// <summary>The first archive's texture of that name (the resolver's order).</summary>
    private static Texture? Find(string name, IReadOnlyList<TextureArchive> archives)
    {
        foreach (var archive in archives)
        {
            if (archive.Textures.TryGetValue(name, out var texture))
            {
                return texture;
            }
        }

        return null;
    }

    /// <summary>A frame's RGBA8 as the enhanced look expands it: index 0 clear (0, 0, 0, 0), the rest opaque.</summary>
    public static byte[] Frame(Texture texture, Palette palette, int frame)
    {
        var size = texture.Width * texture.Height;
        return ColourMips.Expand(texture.Indices.AsSpan(frame * size, size), palette.Rgba);
    }
}
