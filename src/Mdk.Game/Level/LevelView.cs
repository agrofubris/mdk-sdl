using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Formats;

namespace Mdk.Game.Level;

/// <summary>The level's arenas on the GPU: a vertex buffer per triangle group, its triangles in
/// batches by surface. Hidden groups and unreachable arenas aren't drawn; a group that changes
/// texture is rebuilt.
/// <code>
///   arena ─┬─ group 0 (the rest)   ─► mesh, batches
///          └─ group n (scripted)   ─► mesh, batches   (hidden? retextured?)
/// </code></summary>
public sealed class LevelView
{
    private readonly record struct Batch(int First, int Count, Material Material);

    private sealed record GroupMesh(int Mesh, List<Batch> Batches);

    private sealed class ArenaView(Arena arena, Vector3[] positions, Palette palette, List<TextureArchive> archives, bool reachable)
    {
        public readonly Arena Arena = arena;
        public readonly Vector3[] Positions = positions;
        public readonly Palette Palette = palette;
        public readonly List<TextureArchive> Archives = archives;
        public bool Reachable = reachable;
        public readonly Dictionary<int, GroupMesh?> Groups = [];
    }

    private readonly Renderer _renderer;
    private readonly MaterialResolver _resolver;
    private readonly TriangleGroups _groups;
    private readonly Dictionary<string, ArenaView> _arenas = [];

    public LevelView(Renderer renderer, LevelData level, TriangleGroups groups)
    {
        _renderer = renderer;
        _groups = groups;
        _resolver = new MaterialResolver(renderer, level.Dti);
        foreach (var arena in level.Arenas)
        {
            groups.Add(arena);
            var view = new ArenaView(arena, Layers.Positions(arena), level.PaletteOf(arena), level.ArchivesOf(arena), level.IsReachable(arena.Name));
            _arenas[arena.Name] = view;
            foreach (var group in groups.Of(arena.Name))
            {
                view.Groups[group.Number] = Build(view, group);
            }
        }

        groups.Changed += Rebuild;
    }

    public int TriangleCount { get; private set; }

    /// <summary>Shows an arena Kurt can only reach by a teleport.</summary>
    public void Enter(string arena)
    {
        if (_arenas.TryGetValue(arena, out var view))
        {
            view.Reachable = true;
        }
    }

    /// <summary>Queues the visible groups of <paramref name="arenas"/> (the original draws Kurt's
    /// arena and the active second one, 0x41e344), or of every reachable arena when empty.</summary>
    public void Draw(IReadOnlyCollection<string> arenas)
    {
        var shown = arenas.Count == 0 ? _arenas.Values.Where(a => a.Reachable) : arenas.Where(_arenas.ContainsKey).Select(a => _arenas[a]);
        foreach (var view in shown)
        {
            foreach (var (number, mesh) in view.Groups)
            {
                if (mesh == null || (_groups.Get(view.Arena.Name, number)!.State & TriangleGroups.State.Hidden) != 0)
                {
                    continue;
                }

                foreach (var batch in mesh.Batches)
                {
                    _renderer.Draw(mesh.Mesh, batch.First, batch.Count, batch.Material);
                }
            }
        }
    }

    /// <summary>A group took another texture: its mesh is built again (the old buffer stays until the level ends).</summary>
    private void Rebuild(string arena, int number)
    {
        var view = _arenas[arena];
        var group = _groups.Get(arena, number)!;
        if (group.Material is not null)
        {
            view.Groups[number] = Build(view, group);
        }
    }

    private GroupMesh? Build(ArenaView view, TriangleGroups.Group group)
    {
        var arena = view.Arena;

        // Triangles by surface, so each batch is one draw.
        var bySurface = new Dictionary<Material, (Texture? Texture, List<int> Triangles)>();
        foreach (var t in group.Triangles)
        {
            var value = group.Material ?? arena.TriangleMaterials[t];
            var surface = _resolver.Resolve(value, arena.Materials, view.Palette, view.Archives, Pass.Solid);
            if (surface is not { } s)
            {
                continue;
            }

            if (!bySurface.TryGetValue(s.Material, out var entry))
            {
                bySurface[s.Material] = entry = (s.Texture, []);
            }

            entry.Triangles.Add(t);
        }

        var vertices = new List<Vertex>();
        var batches = new List<Batch>();
        foreach (var (material, (texture, triangles)) in bySurface)
        {
            // UVs are in texels of one frame.
            var scale = texture == null ? Vector2.Zero : new Vector2(1f / texture.Width, 1f / texture.Height);
            var first = vertices.Count;
            foreach (var t in triangles)
            {
                for (var k = 0; k < 3; k++)
                {
                    vertices.Add(new Vertex(view.Positions[t * 3 + k], arena.TriangleUvs[t * 3 + k] * scale));
                }
            }

            batches.Add(new Batch(first, vertices.Count - first, material));
        }

        if (vertices.Count == 0)
        {
            return null;
        }

        TriangleCount += vertices.Count / 3;
        return new GroupMesh(_renderer.CreateMesh([.. vertices]), batches);
    }
}
