using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Formats;

namespace Mdk.Game.Fall;

/// <summary>The fall's models (<c>FALL3D.BNI</c>, without the arena models' flags word) in the
/// textures of <c>FALL3D_n.MTI</c> and the <c>FALLPn</c> palette, both faces drawn: a mesh per
/// pose (model, animation, frame), built once.
/// <code>
///   (model, animation, frame) ──► cached mesh + batches per surface ──(world matrix)──► renderer
/// </code></summary>
public sealed class FallModels(Renderer renderer, Bni bni, TextureArchive mti, Palette palette, int paletteId)
{
    /// <summary>Models with named parts (bit 7 of the table 0x490ca4).</summary>
    private static readonly HashSet<string> NamedParts = ["KURT", "MISSILE", "CHUTE", "BONES", "SW_DUMMY", "SW_H150", "SW_THUMP", "SW_TWIST", "SW_INTER"];
    /// <summary>Material values from 256 are special (glass, mirrors): not in the fall.</summary>
    private const int SpecialFirst = 256;
    private const string PenPrefix = "PEN_";

    private readonly record struct Batch(int First, int Count, Material Material);

    private sealed record PoseMesh(int Mesh, List<Batch> Batches);

    private readonly Dictionary<string, Model?> _models = [];
    private readonly Dictionary<string, ModelAnimation> _animations = [];
    private readonly Dictionary<string, Vector3[][][]> _baked = [];
    private readonly Dictionary<(string, string, int), PoseMesh?> _meshes = [];
    private readonly Dictionary<Texture, int> _textures = [];

    /// <summary>A model animation of the BNI.</summary>
    public ModelAnimation Animation(string name)
    {
        if (!_animations.TryGetValue(name, out var animation))
        {
            animation = _animations[name] = ModelAnimation.Parse(name, bni.Bytes, bni.Entries[name].Offset);
        }

        return animation;
    }

    /// <summary>Draws a model at rest, or in a frame of an animation; <paramref name="textureFrame"/>
    /// picks an animated texture's frame.</summary>
    public void Draw(string name, Matrix4x4 world, string animation = "", int frame = 0, int textureFrame = 0)
    {
        var key = (name, animation, frame);
        if (!_meshes.TryGetValue(key, out var mesh))
        {
            mesh = _meshes[key] = Build(name, animation, frame);
        }

        if (mesh == null)
        {
            return;
        }

        foreach (var batch in mesh.Batches)
        {
            renderer.Draw(mesh.Mesh, batch.First, batch.Count, batch.Material, textureFrame, world);
        }
    }

    private Model? Get(string name)
    {
        if (_models.TryGetValue(name, out var model))
        {
            return model;
        }

        var parts = NamedParts.Contains(name) ? Model.Parts.Named : Model.Parts.Single;
        return _models[name] = bni.Has(name) ? Model.Parse(name, bni.Bytes, bni.Entries[name].Offset, Model.Header.NoFlags, parts) : null;
    }

    private PoseMesh? Build(string name, string animation, int frame)
    {
        if (Get(name) is not { } model)
        {
            return null;
        }

        var pose = model.RestPose();
        if (animation.Length != 0)
        {
            if (!_baked.TryGetValue(animation, out var frames))
            {
                frames = _baked[animation] = Animation(animation).Bake(model);
            }

            pose = frames[frame];
        }

        var bySurface = new Dictionary<Material, (Texture? Texture, List<Vertex> Vertices)>();
        for (var p = 0; p < model.PartList.Count; p++)
        {
            var part = model.PartList[p];
            for (var t = 0; t < part.TriangleMaterials.Length && pose[p].Length != 0; t++)
            {
                if (Resolve(part.TriangleMaterials[t], model.Materials) is not { } surface)
                {
                    continue;
                }

                if (!bySurface.TryGetValue(surface.Material, out var entry))
                {
                    bySurface[surface.Material] = entry = (surface.Texture, []);
                }

                var scale = entry.Texture == null ? Vector2.Zero : new Vector2(1f / entry.Texture.Width, 1f / entry.Texture.Height);
                for (var k = 0; k < 3; k++)
                {
                    entry.Vertices.Add(new Vertex(pose[p][part.TriangleIndices[t * 3 + k]], part.TriangleUvs[t * 3 + k] * scale));
                }
            }
        }

        var vertices = new List<Vertex>();
        var batches = new List<Batch>();
        foreach (var (material, (_, triangles)) in bySurface)
        {
            batches.Add(new Batch(vertices.Count, triangles.Count, material));
            vertices.AddRange(triangles);
        }

        return vertices.Count == 0 ? null : new PoseMesh(renderer.CreateMesh([.. vertices]), batches);
    }

    /// <summary>A triangle's surface: a texture of the MTI, or a palette colour (negative values, the
    /// MTI's colours, <c>PEN_n</c>); null when it isn't drawn.</summary>
    private (Material Material, Texture? Texture)? Resolve(int value, IReadOnlyList<string> names)
    {
        if (value < 0)
        {
            return Colour(-value);
        }

        var name = names[value];
        if (mti.Textures.TryGetValue(name, out var texture))
        {
            return (new Material(TextureId(texture), paletteId, Vector4.One, texture.FrameCount, Pass.DoubleSided), texture);
        }

        if (mti.Colors.TryGetValue(name, out var index))
        {
            return Colour(index);
        }

        return name.StartsWith(PenPrefix) && int.TryParse(name.AsSpan(PenPrefix.Length), out var pen) ? Colour(pen) : null;
    }

    private (Material Material, Texture? Texture)? Colour(int index)
    {
        if (index >= SpecialFirst)
        {
            return null;
        }

        var rgba = palette.Rgba;
        var colour = new Vector4(rgba[index * 4], rgba[index * 4 + 1], rgba[index * 4 + 2], byte.MaxValue) / byte.MaxValue;
        return (Material.Flat(colour, Pass.DoubleSided), null);
    }

    private int TextureId(Texture texture)
    {
        if (!_textures.TryGetValue(texture, out var id))
        {
            id = _textures[texture] = renderer.CreateIndexTexture(texture.Width, texture.Height * texture.FrameCount, texture.Indices);
        }

        return id;
    }
}
