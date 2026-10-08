using System.Numerics;
using Mdk.Formats;
using Mdk.Game.Mods;

namespace Mdk.Game.Tests;

/// <summary>Mods' models: parts replaced by name, glTF's axes, a replaced part following its
/// original's pose (<see cref="PartFit"/>), and the export reading back as it was.</summary>
public class ModelSwapTests
{
    private const float Tolerance = 1e-3f;

    /// <summary>A box-ish part: a tetrahedron's corners.</summary>
    private static readonly Vector3[] Tetrahedron = [new(0, 0, 0), new(2, 0, 0), new(0, 3, 0), new(0, 0, 4)];
    private static readonly int[] TetrahedronTriangles = [0, 1, 2, 0, 1, 3, 0, 2, 3, 1, 2, 3];

    private static void Near(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < Tolerance, $"expected {expected}, got {actual}");

    // --- Poses -------------------------------------------------------------------------------

    [Fact]
    public void RestPoseIsIdentity()
    {
        var fit = new PartFit(Tetrahedron, TetrahedronTriangles).Fit(Tetrahedron);

        Near(new Vector3(5, 6, 7), Vector3.Transform(new Vector3(5, 6, 7), fit));
    }

    /// <summary>A rigid move (a matrix track): any point of the replacement moves as the part.</summary>
    [Fact]
    public void RigidMoveIsExact()
    {
        var move = Matrix4x4.CreateRotationZ(0.7f) * Matrix4x4.CreateRotationX(-0.3f) * Matrix4x4.CreateTranslation(10, -4, 2);
        var posed = Tetrahedron.Select(v => Vector3.Transform(v, move)).ToArray();

        var fit = new PartFit(Tetrahedron, TetrahedronTriangles).Fit(posed);

        Near(Vector3.Transform(new Vector3(1, 1, 1), move), Vector3.Transform(new Vector3(1, 1, 1), fit));
        Near(Vector3.Transform(new Vector3(-3, 8, 0), move), Vector3.Transform(new Vector3(-3, 8, 0), fit));
    }

    /// <summary>A flat part (a door panel) still turns: its triangles' apexes leave the plane.</summary>
    [Fact]
    public void FlatPartTurns()
    {
        Vector3[] square = [new(0, 0, 0), new(4, 0, 0), new(4, 4, 0), new(0, 4, 0)];
        int[] triangles = [0, 1, 2, 0, 2, 3];
        var turn = Matrix4x4.CreateRotationY(1.2f) * Matrix4x4.CreateTranslation(0, 0, 5);

        var fit = new PartFit(square, triangles).Fit([.. square.Select(v => Vector3.Transform(v, turn))]);

        Near(Vector3.Transform(new Vector3(2, 2, 1), turn), Vector3.Transform(new Vector3(2, 2, 1), fit));
    }

    /// <summary>A part of one point only moves.</summary>
    [Fact]
    public void PointOnlyMoves()
    {
        var fit = new PartFit([new Vector3(1, 1, 1)], []).Fit([new Vector3(3, 1, 1)]);

        Near(new Vector3(7, 5, 5), Vector3.Transform(new Vector3(5, 5, 5), fit));
    }

    // --- Parts by name -----------------------------------------------------------------------

    private static Model Grunt()
    {
        var model = new Model { Name = "GRUNT", Materials = ["SKIN", "CLOTH"] };
        foreach (var name in new[] { "BODY", "HEAD", "ARM", "ARM" })
        {
            model.PartList.Add(new Model.Part
            {
                Name = name,
                Vertices = Tetrahedron,
                TriangleIndices = TetrahedronTriangles,
                TriangleMaterials = [0, 0, 1, 1],
                TriangleUvs = [.. Enumerable.Range(0, 12).Select(i => new Vector2(i % 4, i / 4))],
            });
        }

        return model;
    }

    private static Glb.Mesh Node(string name) =>
        new(name, [new Glb.Primitive([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [Vector2.Zero, Vector2.UnitX, Vector2.UnitY], [0, 1, 2], 0)]);

    [Fact]
    public void NodesReplaceTheirParts()
    {
        var scene = new Glb.Scene([Node("head"), Node("TAIL"), Node("ARM"), Node("ARM"), Node("ARM")], [new Glb.Material("M", Vector4.One, null)]);
        var unknown = new List<string>();

        var swap = ModelSwap.Of(scene, Grunt(), unknown);

        Assert.NotNull(swap);
        Assert.Null(swap.Parts[0]);
        Assert.NotNull(swap.Parts[1]);
        Assert.NotNull(swap.Parts[2]);
        Assert.NotNull(swap.Parts[3]);
        // No such part, and a third ARM.
        Assert.Equal(["TAIL", "ARM"], unknown);
    }

    [Fact]
    public void NoPartNoSwap()
    {
        Assert.Null(ModelSwap.Of(new Glb.Scene([Node("WING")], []), Grunt(), []));
    }

    /// <summary>A model of one unnamed part takes the node of the model's name.</summary>
    [Fact]
    public void UnnamedPartIsTheModel()
    {
        var model = new Model { Name = "BARREL", PartList = [new Model.Part { Vertices = Tetrahedron, TriangleIndices = TetrahedronTriangles }] };

        Assert.Equal("BARREL", ModelSwap.PartName(model, 0));
        Assert.NotNull(ModelSwap.Of(new Glb.Scene([Node("barrel")], []), model, [])?.Parts[0]);
    }

    /// <summary>glTF is Y up, MDK Z up: Blender shows MDK's up as up.</summary>
    [Fact]
    public void AxesTurn()
    {
        Assert.Equal(new Vector3(1, -3, 2), ModelSwap.ToMdk(new Vector3(1, 2, 3)));
        Assert.Equal(new Vector3(1, 2, 3), ModelSwap.ToMdk(ModelSwap.ToGltf(new Vector3(1, 2, 3))));
    }

    // --- Export round trip -------------------------------------------------------------------

    /// <summary>An exported model read back: every part's triangles (corners, UVs as 0-1), its
    /// texture's image and colour; the replacement at rest is the original.</summary>
    [Fact]
    public void ExportReadsBack()
    {
        var model = Grunt();
        var archive = new TextureArchive();
        archive.Textures["SKIN"] = new Texture { Name = "SKIN", Width = 4, Height = 3, Indices = [.. Enumerable.Range(0, 12).Select(i => (byte)i)] };
        archive.Colors["CLOTH"] = 5;
        var palette = new Palette();
        for (var i = 0; i < Palette.Size * 4; i++)
        {
            palette.Rgba[i] = (byte)(i * 7);
        }

        var bytes = Glb.Write(ModelExport.Scene(model, palette, [archive]));
        var swap = ModelSwap.Of(Glb.Read(bytes), model, []);

        Assert.NotNull(swap);
        for (var p = 0; p < model.PartList.Count; p++)
        {
            var part = model.PartList[p];
            var mesh = swap.Parts[p]!;
            var original = Corners(part.TriangleIndices.Select(i => part.Vertices[i]),
                part.TriangleUvs.Select((uv, i) => part.TriangleMaterials[i / 3] == 0 ? uv / new Vector2(4, 3) : Vector2.Zero));
            var read = Corners(mesh.Triangles.Select(i => mesh.Vertices[i]), mesh.Triangles.Select(i => mesh.Uvs[i]));
            Assert.Equal(original, read);

            swap.Pose(model.RestPose(), 0);
            Near(mesh.Vertices[0], swap.Vertex(p, 0));
        }

        var skin = swap.Surfaces.Single(s => s.Name == "SKIN");
        Assert.Equal((4, 3), (skin.Image!.Width, skin.Image.Height));
        // Index 5 of the palette: 140, 147, 154.
        Assert.Equal(new byte[] { 140, 147, 154, 255 }, skin.Image.Rgba.AsSpan(5 * 4, 4).ToArray());
        var cloth = swap.Surfaces.Single(s => s.Name == "CLOTH");
        Assert.Null(cloth.Image);
        Near(new Vector3(140, 147, 154) / 255f, new Vector3(cloth.Colour.X, cloth.Colour.Y, cloth.Colour.Z));
    }

    /// <summary>Triangles' corners (position, UV) in a comparable order (-0 as 0).</summary>
    private static List<string> Corners(IEnumerable<Vector3> positions, IEnumerable<Vector2> uvs) =>
        [.. positions.Zip(uvs).Chunk(3).Select(t => string.Join(";", t.Select(c => $"{c.First + Vector3.Zero} {c.Second}"))).Order()];
}
