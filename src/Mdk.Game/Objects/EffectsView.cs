using System.Numerics;
using System.Runtime.InteropServices;
using Mdk.Engine.Render;
using Mdk.Formats;
using Mdk.Game.Level;
using Mdk.Game.Scripts;

namespace Mdk.Game.Objects;

/// <summary>Draws the flying pieces, sparks and sprite effects of the scripts, rebuilt every frame
/// into one dynamic mesh: pieces (both faces) batched by surface, sparks as flat triangles in palette
/// colours by how much they face the camera (0x406e24), sprites (effects, spawn_box objects) as
/// camera-facing quads showing one frame of their animated texture.
/// <code>
///   Debris.Pieces ──► textured triangles │ spark triangles ─┐
///   Effects.All, ScriptRuntime.Boxes ──► billboard quads ───┤
///   twisters' ribbons ──► WMIBTEX triangles ────────────────┴─► dynamic mesh ──► renderer
/// </code></summary>
public sealed class EffectsView(Renderer renderer, MaterialResolver resolver, LevelData level, Shading sprites)
{
    /// <summary>Vertices drawn at most per frame (the rest is dropped).</summary>
    private const int MaxVertices = 1 << 16;
    private const int QuadVertices = 6;
    /// <summary>A spark's colour ranges over the palette.</summary>
    private const int LastColour = 255;
    private const float ByteToUnit = 1f / 255f;

    private readonly int _mesh = renderer.CreateDynamicMesh(MaxVertices);
    private readonly Dictionary<string, ObjectView.Look> _looks = [];
    private readonly List<Vertex> _vertices = [];
    private readonly List<(int First, int Count, Material Material, int Frame)> _draws = [];

    /// <summary>Queues everything for <paramref name="camera"/>.</summary>
    public void Draw(ScriptRuntime scripts, View camera)
    {
        _vertices.Clear();
        _draws.Clear();

        // A look-at view times a perspective: its columns hold the camera's right, up and forward.
        var m = camera.ViewProjection;
        var forward = Vector3.Normalize(new Vector3(m.M14, m.M24, m.M34));
        var right = Vector3.Normalize(new Vector3(m.M11, m.M21, m.M31));
        var billboardUp = Vector3.Cross(right, forward);

        AddPieces(scripts.Debris.Pieces, forward);
        foreach (var effect in scripts.Effects.All)
        {
            var sprite = Sprite(effect.Arena, effect.Texture);
            if (sprite is { } s)
            {
                var texel = effect.Scale / Effects.TexelsPerUnit;
                AddQuad(effect.Position, right * texel, billboardUp * texel, s, effect.Frame);
            }
        }

        // spawn_box sprites: as wide as the box, playing at 30 frames per second.
        foreach (var box in scripts.Boxes)
        {
            if (!box.Visible || box.Model == null || Sprite(box.Arena, box.Model.Name) is not { } s)
            {
                continue;
            }

            var size = box.Model.Bounds.Size();
            var texel = MathF.Max(size.X, size.Z) / Math.Max(s.Texture!.Width, 1);
            AddQuad(box.Position, right * texel, billboardUp * texel, s, scripts.TickCount % s.Material.FrameCount);
        }

        foreach (var twister in scripts.Items.Twisters)
        {
            AddRibbon(twister);
        }

        if (_vertices.Count == 0)
        {
            return;
        }

        renderer.UpdateMesh(_mesh, CollectionsMarshal.AsSpan(_vertices));
        foreach (var (first, count, material, frame) in _draws)
        {
            renderer.Draw(_mesh, first, count, material, frame);
        }
    }

    /// <summary>Pieces' triangles batched by surface; sparks' by palette colour.</summary>
    private void AddPieces(IReadOnlyList<Debris.Piece> pieces, Vector3 forward)
    {
        var batches = new Dictionary<Material, List<Vertex>>();
        foreach (var piece in pieces)
        {
            var look = LookOf(piece.Arena);
            for (var t = 0; t < piece.Corners.Count / 3; t++)
            {
                var a = Vector3.Transform(piece.Corners[t * 3], piece.Orientation);
                var b = Vector3.Transform(piece.Corners[t * 3 + 1], piece.Orientation);
                var c = Vector3.Transform(piece.Corners[t * 3 + 2], piece.Orientation);
                var surface = piece.IsSpark ? SparkSurface(piece, look.Palette, a, b, c, forward) : PieceSurface(piece, t, look);
                if (surface is not { } s)
                {
                    continue;
                }

                if (!batches.TryGetValue(s.Material, out var vertices))
                {
                    batches[s.Material] = vertices = [];
                }

                // UVs are in texels of the texture.
                var scale = s.Texture == null ? Vector2.Zero : new Vector2(1f / s.Texture.Width, 1f / s.Texture.Height);
                Vector2 Uv(int k) => piece.IsSpark ? Vector2.Zero : piece.Uvs[t * 3 + k] * scale;
                vertices.Add(new Vertex(piece.Center + a, Uv(0)));
                vertices.Add(new Vertex(piece.Center + b, Uv(1)));
                vertices.Add(new Vertex(piece.Center + c, Uv(2)));
            }
        }

        foreach (var (material, vertices) in batches)
        {
            var count = Math.Min(vertices.Count, MaxVertices - _vertices.Count) / 3 * 3;
            if (count == 0)
            {
                return;
            }

            _draws.Add((_vertices.Count, count, material, 0));
            _vertices.AddRange(vertices.Take(count));
        }
    }

    private MaterialResolver.Surface? PieceSurface(Debris.Piece piece, int triangle, ObjectView.Look look) =>
        resolver.Resolve(piece.Materials[triangle], piece.Names!, look.Palette, look.Archives, Pass.DoubleSided);

    /// <summary>A spark's face: palette colour base + range × |facing|, where facing is how much it
    /// turns to the camera (face-on: base + range, edge-on: base).</summary>
    private static MaterialResolver.Surface SparkSurface(Debris.Piece piece, Palette palette, Vector3 a, Vector3 b, Vector3 c, Vector3 forward)
    {
        var normal = Vector3.Cross(b - a, c - a);
        var facing = normal == Vector3.Zero ? 0f : MathF.Abs(Vector3.Dot(Vector3.Normalize(normal), forward));
        var index = Math.Clamp((int)MathF.Round(piece.ColourBase + piece.ColourRange * facing), 0, LastColour);
        var rgba = palette.Rgba;
        var colour = new Vector4(rgba[index * 4], rgba[index * 4 + 1], rgba[index * 4 + 2], byte.MaxValue) * ByteToUnit;
        return new MaterialResolver.Surface(Material.Flat(colour, Pass.DoubleSided), null);
    }

    /// <summary>An animated texture of an arena (or the level) as a sprite surface (shaded as
    /// <paramref name="sprites"/>), or null.</summary>
    private MaterialResolver.Surface? Sprite(string arena, string texture)
    {
        var look = LookOf(arena);
        var surface = resolver.Resolve(0, [texture], look.Palette, look.Archives, Pass.DoubleSided);
        return surface is { Texture: not null } s ? s with { Material = s.Material with { Shading = sprites } } : null;
    }

    /// <summary>A sprite centred at <paramref name="center"/>, <paramref name="right"/> and
    /// <paramref name="up"/> being one texel.</summary>
    private void AddQuad(Vector3 center, Vector3 right, Vector3 up, MaterialResolver.Surface sprite, int frame)
    {
        if (_vertices.Count + QuadVertices > MaxVertices)
        {
            return;
        }

        var halfWidth = right * (sprite.Texture!.Width * 0.5f);
        var halfHeight = up * (sprite.Texture.Height * 0.5f);
        Vertex Corner(float x, float y) => new(center + halfWidth * (x * 2f - 1f) + halfHeight * (1f - y * 2f), new Vector2(x, y));
        _draws.Add((_vertices.Count, QuadVertices, sprite.Material, frame));
        _vertices.AddRange([Corner(0, 0), Corner(1, 0), Corner(1, 1), Corner(0, 0), Corner(1, 1), Corner(0, 1)]);
    }

    /// <summary>A twister's ribbon, textured with <c>WMIBTEX</c> (UVs in its texels).</summary>
    private void AddRibbon(Twister twister)
    {
        var corners = twister.Ribbon.Triangles();
        if (corners.Count == 0 || _vertices.Count + corners.Count > MaxVertices || Sprite(twister.Arena, Ribbon.Texture) is not { } s)
        {
            return;
        }

        var scale = new Vector2(1f / s.Texture!.Width, 1f / s.Texture.Height);
        _draws.Add((_vertices.Count, corners.Count, s.Material, 0));
        _vertices.AddRange(corners.Select(c => new Vertex(c.Position, c.Uv * scale)));
    }

    /// <summary>The palette and textures an arena's effects draw with.</summary>
    private ObjectView.Look LookOf(string name)
    {
        if (_looks.TryGetValue(name, out var look))
        {
            return look;
        }

        var arena = level.Arenas.Find(a => a.Name == name);
        return _looks[name] = arena != null
            ? new ObjectView.Look(level.PaletteOf(arena), level.ArchivesOf(arena))
            : new ObjectView.Look(level.Dti.Palette, [level.LevelTextures]);
    }
}
