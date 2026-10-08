using System.Numerics;
using Mdk.Formats;
using Mdk.Game.HdTextures;
using Mdk.Game.Level;

namespace Mdk.Game.Mods;

/// <summary>A model as a glTF scene for modders (<see cref="Glb"/>): a node per part named as the
/// loader expects (<see cref="ModelSwap.PartName"/>), in the rest pose, glTF's axes; one primitive
/// per drawn material. Materials keep the original names; a texture's first frame through the
/// palette is embedded, colours are base colours (glTF's linear).
/// <code>
///   part ─► node "HEAD" ─► per material: corners welded by (vertex, UV), UVs in 0-1 of the texture
/// </code></summary>
public static class ModelExport
{
    private const string PenPrefix = "PEN_";
    private const int Corners = 3;
    private const int Channels = 4;
    private const float ByteToUnit = 1f / 255f;
    /// <summary>Special colours (glass, mirrors) have no palette colour: grey.</summary>
    private static readonly Vector4 SpecialColour = new(0.5f, 0.5f, 0.5f, 1f);

    public static Glb.Scene Scene(Model model, Palette palette, IReadOnlyList<TextureArchive> archives)
    {
        var materials = new List<Glb.Material>();
        var materialIndex = new Dictionary<string, int>();
        var meshes = new List<Glb.Mesh>();
        for (var p = 0; p < model.PartList.Count; p++)
        {
            var part = model.PartList[p];
            var primitives = new Dictionary<int, (List<Vector3> Positions, List<Vector2> Uvs, List<int> Indices, Dictionary<(int, Vector2), int> Welded)>();
            for (var t = 0; t < part.TriangleMaterials.Length; t++)
            {
                var value = part.TriangleMaterials[t];
                if (!MaterialResolver.IsDrawn(value, model.Materials, archives))
                {
                    continue;
                }

                var name = value < 0 ? $"{PenPrefix}{-value}" : model.Materials[value];
                if (!materialIndex.TryGetValue(name, out var material))
                {
                    material = materialIndex[name] = materials.Count;
                    materials.Add(Material(name, palette, archives));
                }

                if (!primitives.TryGetValue(material, out var primitive))
                {
                    primitives[material] = primitive = ([], [], [], []);
                }

                var texture = Find(name, archives);
                var scale = texture == null ? Vector2.Zero : new Vector2(1f / texture.Width, 1f / texture.Height);
                for (var k = 0; k < Corners; k++)
                {
                    var vertex = part.TriangleIndices[t * Corners + k];
                    var uv = part.TriangleUvs[t * Corners + k] * scale;
                    if (!primitive.Welded.TryGetValue((vertex, uv), out var index))
                    {
                        index = primitive.Welded[(vertex, uv)] = primitive.Positions.Count;
                        primitive.Positions.Add(ModelSwap.ToGltf(part.Vertices[vertex]));
                        primitive.Uvs.Add(uv);
                    }

                    primitive.Indices.Add(index);
                }
            }

            meshes.Add(new Glb.Mesh(ModelSwap.PartName(model, p),
                [.. primitives.Select(e => new Glb.Primitive([.. e.Value.Positions], [.. e.Value.Uvs], [.. e.Value.Indices], e.Key))]));
        }

        return new Glb.Scene(meshes, materials);
    }

    /// <summary>A texture's first frame embedded; a colour (archive, PEN_n) as the base colour.</summary>
    private static Glb.Material Material(string name, Palette palette, IReadOnlyList<TextureArchive> archives)
    {
        if (Find(name, archives) is { } texture)
        {
            var rgba = TextureExport.Frame(texture, palette, 0);
            return new Glb.Material(name, Vector4.One, Png.Encode(texture.Width, texture.Height, rgba, Png.Channels.Rgba));
        }

        var index = archives.Select(a => a.Colors.TryGetValue(name, out var c) ? c : (int?)null).FirstOrDefault(c => c != null)
            ?? (name.StartsWith(PenPrefix) && int.TryParse(name.AsSpan(PenPrefix.Length), out var pen) ? pen : Palette.Size);
        if (index >= Palette.Size)
        {
            return new Glb.Material(name, SpecialColour, null);
        }

        var colour = new Vector4(palette.Rgba[index * Channels], palette.Rgba[index * Channels + 1], palette.Rgba[index * Channels + 2], byte.MaxValue) * ByteToUnit;
        return new Glb.Material(name, ModelSwap.ToLinear(colour), null);
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
}
