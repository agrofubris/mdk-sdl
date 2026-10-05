using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Formats;
using Mdk.Game.Level;

namespace Mdk.Game.Objects;

/// <summary>Draws objects' models: a mesh per pose (model, animation, frame, hidden parts), built
/// once and shared, placed by each object's transform. Models show both faces.
/// <code>
///   object ──pose key──► cached mesh + batches ──(transform)──► renderer
/// </code></summary>
public sealed class ObjectView(Renderer renderer, MaterialResolver resolver)
{
    private readonly record struct Batch(int First, int Count, Material Material);

    private sealed record PoseMesh(int Mesh, List<Batch> Batches);

    /// <summary>Palette and texture archives an arena's objects draw with.</summary>
    public sealed record Look(Palette Palette, IReadOnlyList<TextureArchive> Archives);

    private readonly Dictionary<(Model, ModelAnimation?, int, int, Palette), PoseMesh?> _meshes = [];

    public void Draw(MdkObject obj, Look look)
    {
        if (obj.Model == null || obj.Dead)
        {
            return;
        }

        var key = (obj.Model, obj.Animation, obj.AnimationFrame, obj.HiddenParts, look.Palette);
        if (!_meshes.TryGetValue(key, out var mesh))
        {
            mesh = _meshes[key] = Build(obj.Model, obj.Pose(), look);
        }

        if (mesh == null)
        {
            return;
        }

        var transform = obj.Transform;
        foreach (var batch in mesh.Batches)
        {
            var frame = obj.TextureFrame >= 0 ? obj.TextureFrame : 0;
            renderer.Draw(mesh.Mesh, batch.First, batch.Count, batch.Material, frame, transform);
        }
    }

    private PoseMesh? Build(Model model, Vector3[][] pose, Look look)
    {
        var bySurface = new Dictionary<Material, (Texture? Texture, List<(int Part, int Triangle)> Triangles)>();
        for (var p = 0; p < model.PartList.Count; p++)
        {
            if (pose[p].Length == 0)
            {
                continue;
            }

            var part = model.PartList[p];
            for (var t = 0; t < part.TriangleMaterials.Length; t++)
            {
                var surface = resolver.Resolve(part.TriangleMaterials[t], model.Materials, look.Palette, look.Archives, Pass.DoubleSided);
                if (surface is not { } s)
                {
                    continue;
                }

                if (!bySurface.TryGetValue(s.Material, out var entry))
                {
                    bySurface[s.Material] = entry = (s.Texture, []);
                }

                entry.Triangles.Add((p, t));
            }
        }

        var vertices = new List<Vertex>();
        var batches = new List<Batch>();
        foreach (var (material, (texture, triangles)) in bySurface)
        {
            var scale = texture == null ? Vector2.Zero : new Vector2(1f / texture.Width, 1f / texture.Height);
            var first = vertices.Count;
            foreach (var (p, t) in triangles)
            {
                var part = model.PartList[p];
                for (var k = 0; k < 3; k++)
                {
                    vertices.Add(new Vertex(pose[p][part.TriangleIndices[t * 3 + k]], part.TriangleUvs[t * 3 + k] * scale));
                }
            }

            batches.Add(new Batch(first, vertices.Count - first, material));
        }

        return vertices.Count == 0 ? null : new PoseMesh(renderer.CreateMesh([.. vertices]), batches);
    }
}
