using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Formats;
using Mdk.Game.Level;

namespace Mdk.Game.Objects;

/// <summary>Draws objects' models: each model's triangles laid out once by surface (per palette),
/// then every frame posed (animation frame, hidden parts) into the renderer's vertex stream and placed
/// by the object's transform. No mesh is built while playing. Models show both faces.
/// <code>
///   model + palette ──once──► layout: batches by surface (part, vertex, UV per corner)
///   object ──pose (shared frames)──► stream vertices ──(transform)──► renderer
/// </code></summary>
public sealed class ObjectView(Renderer renderer, MaterialResolver resolver)
{
    private const int TriangleVertices = 3;
    /// <summary>A batch with no visible triangle.</summary>
    private const int Unseen = int.MaxValue;

    /// <summary>A surface's corners in the model's order: their part, vertex and UV; per part, how
    /// many corners and where its first triangle comes in the model (the batch's place when parts hide).</summary>
    private sealed class Batch(Material material, int parts)
    {
        public readonly Material Material = material;
        public readonly List<int> Parts = [];
        public readonly List<int> Indices = [];
        public readonly List<Vector2> Uvs = [];
        public readonly int[] PartCorners = new int[parts];
        public readonly int[] FirstTriangle = Enumerable.Repeat(Unseen, parts).ToArray();
    }

    /// <summary>A model's batches, in the order of their first triangles, and the frame's scratch.</summary>
    private sealed class Layout(List<Batch> batches)
    {
        public readonly List<Batch> Batches = batches;
        public readonly int[] Order = new int[batches.Count];
        public readonly int[] Keys = new int[batches.Count];
    }

    /// <summary>Palette and texture archives an arena's objects draw with.</summary>
    public sealed record Look(Palette Palette, IReadOnlyList<TextureArchive> Archives);

    private readonly Dictionary<(Model, Palette), Layout?> _layouts = [];

    /// <summary>Lays out a model with a look before it's drawn (a level's load).</summary>
    public void Preload(Model model, Look look) => LayoutOf(model, look);

    public void Draw(MdkObject obj, Look look)
    {
        if (obj.Model == null || obj.Dead || LayoutOf(obj.Model, look) is not { } layout)
        {
            return;
        }

        var pose = obj.PoseParts();
        var hidden = obj.HiddenParts;
        var transform = obj.Transform;
        var frame = obj.TextureFrame >= 0 ? obj.TextureFrame : 0;
        var count = Arrange(layout, hidden);
        var glows = obj.EffectFrames > 0 ? Glow.Shines : Glow.Lit;
        for (var i = 0; i < count; i++)
        {
            var batch = layout.Batches[layout.Order[i]];
            var corners = Visible(batch, hidden);
            var vertices = renderer.Stream(corners, out var mesh, out var first);
            if (vertices.IsEmpty)
            {
                continue;
            }

            var at = 0;
            for (var c = 0; c < batch.Parts.Count; c++)
            {
                var part = batch.Parts[c];
                if (IsHidden(hidden, part))
                {
                    continue;
                }

                vertices[at++] = new Vertex(pose[part][batch.Indices[c]], batch.Uvs[c]);
            }

            renderer.Draw(mesh, first, corners, Shine(batch.Material, glows), frame, transform);
        }
    }

    /// <summary>Whether an object shines by itself (an explosion) or is lit.</summary>
    private enum Glow { Lit, Shines }

    /// <summary>An explosion's surfaces in the enhanced look shine unlit (their own point light
    /// would wash them out) and cast no shadow.</summary>
    private static Material Shine(Material material, Glow glow) =>
        glow == Glow.Shines && material.Shading == Shading.Lit ? material with { Shading = Shading.Sprite } : material;

    private static bool IsHidden(int hidden, int part) => (hidden & (1 << part)) != 0;

    /// <summary>Orders the batches with a visible triangle by their first one (as a mesh built from
    /// the visible parts would); returns how many there are.</summary>
    private static int Arrange(Layout layout, int hidden)
    {
        var count = 0;
        for (var b = 0; b < layout.Batches.Count; b++)
        {
            var first = Unseen;
            var starts = layout.Batches[b].FirstTriangle;
            for (var p = 0; p < starts.Length; p++)
            {
                if (!IsHidden(hidden, p))
                {
                    first = Math.Min(first, starts[p]);
                }
            }

            if (first == Unseen)
            {
                continue;
            }

            // Insertion sort: a model has a few surfaces.
            var at = count++;
            while (at > 0 && layout.Keys[at - 1] > first)
            {
                layout.Keys[at] = layout.Keys[at - 1];
                layout.Order[at] = layout.Order[at - 1];
                at--;
            }

            layout.Keys[at] = first;
            layout.Order[at] = b;
        }

        return count;
    }

    private static int Visible(Batch batch, int hidden)
    {
        var corners = 0;
        for (var p = 0; p < batch.PartCorners.Length; p++)
        {
            if (!IsHidden(hidden, p))
            {
                corners += batch.PartCorners[p];
            }
        }

        return corners;
    }

    private Layout? LayoutOf(Model model, Look look)
    {
        if (!_layouts.TryGetValue((model, look.Palette), out var layout))
        {
            layout = _layouts[(model, look.Palette)] = Build(model, look);
        }

        return layout;
    }

    private Layout? Build(Model model, Look look)
    {
        var parts = model.PartList.Count;
        var bySurface = new Dictionary<Material, Batch>();
        var batches = new List<Batch>();
        var sequence = 0;
        for (var p = 0; p < parts; p++)
        {
            var part = model.PartList[p];
            for (var t = 0; t < part.TriangleMaterials.Length; t++, sequence++)
            {
                var surface = resolver.Resolve(part.TriangleMaterials[t], model.Materials, look.Palette, look.Archives, Pass.DoubleSided);
                if (surface is not { } s)
                {
                    continue;
                }

                if (!bySurface.TryGetValue(s.Material, out var batch))
                {
                    batches.Add(bySurface[s.Material] = batch = new Batch(s.Material, parts));
                }

                // UVs are in texels of one frame.
                var scale = s.Texture == null ? Vector2.Zero : new Vector2(1f / s.Texture.Width, 1f / s.Texture.Height);
                batch.FirstTriangle[p] = Math.Min(batch.FirstTriangle[p], sequence);
                batch.PartCorners[p] += TriangleVertices;
                for (var k = 0; k < TriangleVertices; k++)
                {
                    batch.Parts.Add(p);
                    batch.Indices.Add(part.TriangleIndices[t * TriangleVertices + k]);
                    batch.Uvs.Add(part.TriangleUvs[t * TriangleVertices + k] * scale);
                }
            }
        }

        return batches.Count == 0 ? null : new Layout(batches);
    }
}
