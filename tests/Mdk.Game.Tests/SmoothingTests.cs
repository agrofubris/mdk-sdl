using System.Numerics;
using Mdk.Formats;
using Mdk.Game.Flow;
using Mdk.Game.Level;
using Mdk.Game.Objects;
using Mdk.Game.Smoothing;

namespace Mdk.Game.Tests;

/// <summary>Smooth normals with creases, the subdivision of models (PN triangles) and their
/// shapes per baked pose (the enhanced look's).</summary>
public class SmoothingTests
{
    private const float Crease = SmoothMesh.DefaultCrease;
    private const float Tolerance = 1e-4f;

    // --- Meshes ------------------------------------------------------------------------------

    /// <summary>A unit cube (corners ±1), two triangles a face, wound outwards, vertices shared.</summary>
    private static (Vector3[] Positions, int[] Indices) Cube()
    {
        var positions = new Vector3[8];
        for (var i = 0; i < 8; i++)
        {
            positions[i] = new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
        }

        int[][] faces = [[0, 2, 3, 1], [4, 5, 7, 6], [0, 1, 5, 4], [2, 6, 7, 3], [0, 4, 6, 2], [1, 3, 7, 5]];
        var indices = faces.SelectMany(f => new[] { f[0], f[1], f[2], f[0], f[2], f[3] }).ToArray();
        return (positions, indices);
    }

    /// <summary>A regular icosahedron on the unit sphere, wound outwards.</summary>
    private static (Vector3[] Positions, int[] Indices) Icosahedron()
    {
        var t = (1f + MathF.Sqrt(5f)) / 2f;
        Vector3[] raw =
        [
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
            new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
            new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        ];
        int[] indices =
        [
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        ];
        return ([.. raw.Select(Vector3.Normalize)], indices);
    }

    /// <summary>Two triangles hinged on the edge (0,0,0)-(0,1,0), folded by <paramref name="degrees"/>
    /// from flat; the second wound like the first, or the other way.</summary>
    private static (Vector3[] Positions, int[] Indices) Ridge(float degrees, bool mixedWinding = false)
    {
        var fold = float.DegreesToRadians(degrees);
        Vector3[] positions = [new(0, 0, 0), new(0, 1, 0), new(-1, 0, 0), new(MathF.Cos(fold), 0, MathF.Sin(fold))];
        int[] indices = mixedWinding ? [0, 1, 2, 0, 1, 3] : [0, 1, 2, 1, 0, 3];
        return (positions, indices);
    }

    private static Vector3 FaceNormal(Vector3[] positions, int[] indices, int triangle) => Vector3.Normalize(Vector3.Cross(
        positions[indices[triangle * 3 + 1]] - positions[indices[triangle * 3]],
        positions[indices[triangle * 3 + 2]] - positions[indices[triangle * 3]]));

    private static Vector3[] Normals(Vector3[] positions, int[] indices, float crease = Crease)
    {
        var normals = new Vector3[indices.Length];
        SmoothMesh.Build(positions, indices, crease).Normals(positions, normals);
        return normals;
    }

    private static void Parallel(Vector3 expected, Vector3 actual) =>
        Assert.True(MathF.Abs(MathF.Abs(Vector3.Dot(expected, actual)) - 1f) < Tolerance, $"{expected} vs {actual}");

    private static void Same(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < Tolerance, $"{expected} vs {actual}");

    // --- Normals ------------------------------------------------------------------------------

    [Fact]
    public void HardEdgesKeepTheirFaces()
    {
        var (positions, indices) = Cube();

        var normals = Normals(positions, indices);

        for (var c = 0; c < indices.Length; c++)
        {
            Same(FaceNormal(positions, indices, c / 3), normals[c]);
        }
    }

    [Fact]
    public void ShallowEdgesAreAveraged()
    {
        var (positions, indices) = Ridge(20f);

        var normals = Normals(positions, indices);

        // Corners on the hinge take the mean of both faces; the others their own face.
        var mean = Vector3.Normalize(FaceNormal(positions, indices, 0) + FaceNormal(positions, indices, 1));
        Same(mean, normals[0]);
        Same(mean, normals[1]);
        Same(FaceNormal(positions, indices, 0), normals[2]);
        Same(FaceNormal(positions, indices, 1), normals[5]);
    }

    [Fact]
    public void FoldsPastTheCreaseStaySharp()
    {
        var (positions, indices) = Ridge(60f);

        var normals = Normals(positions, indices);

        Same(FaceNormal(positions, indices, 0), normals[0]);
        Same(FaceNormal(positions, indices, 1), normals[4]);
    }

    /// <summary>Models are double-sided: neighbours wound the other way still smooth.</summary>
    [Fact]
    public void MixedWindingStillSmooths()
    {
        var (positions, indices) = Ridge(20f, mixedWinding: true);

        var normals = Normals(positions, indices);

        var (same, sameIndices) = Ridge(20f);
        var mean = Vector3.Normalize(FaceNormal(same, sameIndices, 0) + FaceNormal(same, sameIndices, 1));
        Parallel(mean, normals[0]);
        Parallel(mean, normals[3]);
    }

    [Fact]
    public void DuplicateVerticesAreWelded()
    {
        var (positions, indices) = Ridge(20f);
        Vector3[] split = [.. positions, positions[0], positions[1]];
        int[] apart = [0, 1, 2, 5, 4, 3];

        var normals = Normals(split, apart);

        Same(Normals(positions, indices)[0], normals[0]);
        Same(Normals(positions, indices)[0], normals[4]);
    }

    /// <summary>Creases are found once (the rest pose); the normals follow each pose.</summary>
    [Fact]
    public void NormalsFollowThePose()
    {
        var (rest, indices) = Ridge(20f);
        var mesh = SmoothMesh.Build(rest, indices, Crease);
        var turn = Matrix4x4.CreateRotationX(MathF.PI / 2f);
        var posed = rest.Select(p => Vector3.Transform(p, turn)).ToArray();

        var normals = new Vector3[indices.Length];
        mesh.Normals(posed, normals);

        var expected = Normals(rest, indices);
        for (var c = 0; c < indices.Length; c++)
        {
            Same(Vector3.TransformNormal(expected[c], turn), normals[c]);
        }
    }

    // --- Subdivision --------------------------------------------------------------------------

    private static Subdivision Subdivide(Vector3[] positions, int[] indices, Vector2[]? uvs = null) =>
        Subdivision.Build(SmoothMesh.Build(positions, indices, Crease), uvs ?? new Vector2[indices.Length]);

    private static (Vector3[] Positions, Vector3[] Normals) Pose(Subdivision subdivision, Vector3[] positions, int[] indices)
    {
        var corners = Normals(positions, indices);
        var outPositions = new Vector3[subdivision.PositionCount];
        var outNormals = new Vector3[subdivision.Vertices.Length];
        subdivision.Pose(positions, corners, outPositions, outNormals);
        return (outPositions, outNormals);
    }

    [Fact]
    public void EachTriangleBecomesFour()
    {
        var (positions, indices) = Icosahedron();

        var subdivision = Subdivide(positions, indices);
        var (posed, _) = Pose(subdivision, positions, indices);

        Assert.Equal(indices.Length * 4, subdivision.Vertices.Length);
        Assert.Equal(indices.Length / 3 * 4, subdivision.Sources.Length);
        Assert.All(Enumerable.Range(0, indices.Length / 3), t => Assert.Equal(4, subdivision.Sources.Count(s => s == t)));

        // Wound as their triangle.
        for (var t = 0; t < subdivision.Sources.Length; t++)
        {
            var normal = FaceNormal(posed, subdivision.Vertices, t);
            Assert.True(Vector3.Dot(normal, FaceNormal(positions, indices, subdivision.Sources[t])) > 0.5f);
        }
    }

    [Fact]
    public void RoundShapesGetRounder()
    {
        var (positions, indices) = Icosahedron();

        var subdivision = Subdivide(positions, indices);
        var (posed, _) = Pose(subdivision, positions, indices);

        // The corners stay; the edges' midpoints move out towards the sphere (linear: 0.85).
        for (var v = 0; v < positions.Length; v++)
        {
            Same(positions[v], posed[v]);
        }

        var midpoints = posed.Skip(positions.Length).ToArray();
        Assert.Equal(30, midpoints.Length);
        Assert.All(midpoints, m => Assert.InRange(m.Length(), 0.9f, 1.05f));
    }

    [Fact]
    public void BoxesStayBoxes()
    {
        var (positions, indices) = Cube();

        var subdivision = Subdivide(positions, indices);
        var (posed, normals) = Pose(subdivision, positions, indices);

        foreach (var p in posed)
        {
            Assert.Equal(1f, MathF.Max(MathF.Abs(p.X), MathF.Max(MathF.Abs(p.Y), MathF.Abs(p.Z))), Tolerance);
        }

        for (var c = 0; c < normals.Length; c++)
        {
            Same(FaceNormal(positions, indices, subdivision.Sources[c / 3]), normals[c]);
        }
    }

    /// <summary>A 12-sided can: its sides are smooth (30° apart), its rims hard: the rims stay
    /// straight (a turret's barrel keeps its outline), only round edges bow.</summary>
    [Fact]
    public void HardEdgesStayStraight()
    {
        const int Sides = 12;
        var positions = new List<Vector3>();
        for (var i = 0; i < Sides; i++)
        {
            var angle = MathF.Tau * i / Sides;
            positions.Add(new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0f));
            positions.Add(new Vector3(MathF.Cos(angle), MathF.Sin(angle), 1f));
        }

        positions.Add(new Vector3(0f, 0f, 1f));
        var top = positions.Count - 1;
        var indices = new List<int>();
        for (var i = 0; i < Sides; i++)
        {
            var (b0, t0, b1, t1) = (2 * i, 2 * i + 1, 2 * ((i + 1) % Sides), 2 * ((i + 1) % Sides) + 1);
            indices.AddRange([b0, b1, t1, b0, t1, t0, t0, t1, top]);
        }

        var subdivision = Subdivide([.. positions], [.. indices]);
        var (posed, _) = Pose(subdivision, [.. positions], [.. indices]);

        // Every rim's midpoint: the straight one (on the top, inside the unit circle).
        var rim = posed.Skip(positions.Count).Where(p => MathF.Abs(p.Z - 1f) < Tolerance && new Vector2(p.X, p.Y).Length() > 0.9f).ToList();
        Assert.Equal(Sides, rim.Count);
        Assert.All(rim, p => Assert.Equal(MathF.Cos(MathF.PI / Sides), new Vector2(p.X, p.Y).Length(), Tolerance));
    }

    /// <summary>Glass keeps its flat panes: a pinned triangle's edges stay straight, the rest bows.</summary>
    [Fact]
    public void PinnedTrianglesStayFlat()
    {
        var (positions, indices) = Icosahedron();
        var pinned = new bool[indices.Length / 3];
        pinned[0] = true;

        var subdivision = Subdivision.Build(SmoothMesh.Build(positions, indices, Crease), new Vector2[indices.Length], pinned);
        var (posed, _) = Pose(subdivision, positions, indices);

        var straight = MathF.Sqrt(0.5f + 0.5f * Vector3.Dot(positions[0], positions[11]));
        var pieces = subdivision.Vertices.Take(12).Where(v => v >= positions.Length).Distinct().ToList();
        Assert.Equal(3, pieces.Count);
        Assert.All(pieces, v => Assert.Equal(straight, posed[v].Length(), Tolerance));
        Assert.True(posed.Skip(positions.Length).Count(p => p.Length() > straight + 0.05f) == 30 - 3);
    }

    /// <summary>Glass and mirrors (special colours) are pinned; textures and palette colours aren't.</summary>
    [Fact]
    public void GlassIsSpecial()
    {
        var archive = new TextureArchive();
        archive.Colors["GLASS1"] = 1024;
        archive.Colors["RED"] = 40;
        archive.Textures["WALL"] = new Texture { Name = "WALL", Width = 1, Height = 1, FrameCount = 1, Indices = [1] };
        string[] names = ["GLASS1", "RED", "WALL", "PEN_1000", "PEN_3"];

        var special = Enumerable.Range(0, names.Length).Select(v => MaterialResolver.IsSpecial(v, names, [archive])).ToArray();

        Assert.Equal([true, false, false, true, false], special);
        Assert.True(MaterialResolver.IsSpecial(-1025, names, [archive]));
        Assert.False(MaterialResolver.IsSpecial(-12, names, [archive]));
    }

    /// <summary>Faces apart in the file (no vertex shared) still meet at the same midpoints: no cracks.</summary>
    [Fact]
    public void NeighboursShareMidpoints()
    {
        var (shared, sharedIndices) = Icosahedron();
        var positions = sharedIndices.Select(i => shared[i]).ToArray();
        var indices = Enumerable.Range(0, positions.Length).ToArray();

        var subdivision = Subdivide(positions, indices);
        var (posed, _) = Pose(subdivision, positions, indices);

        var distinct = subdivision.Vertices.Select(v => posed[v]).Distinct().Count();
        Assert.Equal(12 + 30, distinct);
    }

    /// <summary>A texture seam: the same edge, other UVs on each side; each side keeps its own.</summary>
    [Fact]
    public void SeamsKeepEachSidesUvs()
    {
        var (positions, indices) = Ridge(20f);
        Vector2[] uvs = [new(0, 0), new(0, 8), new(-8, 0), new(16, 8), new(16, 0), new(24, 0)];

        var subdivision = Subdivide(positions, indices, uvs);
        var (posed, _) = Pose(subdivision, positions, indices);

        // The hinge's midpoint: one position, each face's mean UV.
        var hinge = Enumerable.Range(0, subdivision.Vertices.Length).Where(c => subdivision.Vertices[c] >= positions.Length
            && Vector3.Distance(posed[subdivision.Vertices[c]], new Vector3(0, 0.5f, 0)) < 0.2f).ToList();
        Assert.Single(hinge.Select(c => subdivision.Vertices[c]).Distinct());
        Assert.Contains(new Vector2(0, 4), hinge.Select(c => subdivision.Uvs[c]));
        Assert.Contains(new Vector2(16, 4), hinge.Select(c => subdivision.Uvs[c]));
    }

    // --- Models -------------------------------------------------------------------------------

    private static Model RidgeModel()
    {
        var (positions, indices) = Ridge(20f);
        var part = new Model.Part
        {
            Vertices = positions,
            TriangleIndices = indices,
            TriangleMaterials = [0, 0],
            TriangleUvs = new Vector2[indices.Length],
        };
        return new Model { Name = "RIDGE", Materials = ["WALL"], PartList = [part] };
    }

    [Fact]
    public void EachPoseHasItsShape()
    {
        var model = RidgeModel();
        var rest = new[] { model.PartList[0].Vertices };
        var turn = Matrix4x4.CreateRotationZ(MathF.PI / 2f);
        var turned = new[] { rest[0].Select(p => Vector3.Transform(p, turn)).ToArray() };
        var shapes = new ModelShapes(ModelShape.Smooth);

        var first = shapes.Pose(model, rest, 0)[0].Normals.ToArray();
        var second = shapes.Pose(model, turned, 0)[0];

        Assert.Same(turned[0], second.Positions);
        var expected = Normals(rest[0], model.PartList[0].TriangleIndices);
        for (var c = 0; c < first.Length; c++)
        {
            Near(expected[c], Unpack(first[c]));
            Near(Vector3.TransformNormal(Unpack(first[c]), turn), Unpack(second.Normals[c]));
        }
    }

    [Fact]
    public void SubdividedModelsKeepTheirMaterials()
    {
        var model = RidgeModel();
        model.PartList[0].TriangleMaterials = [0, 1];
        var shapes = new ModelShapes(ModelShape.Subdivided);

        var part = shapes.Pose(model, [model.PartList[0].Vertices], 0)[0];

        Assert.Equal([0, 0, 0, 0, 1, 1, 1, 1], part.Sources);
        Assert.Equal(part.Vertices.Length, part.Normals.Length);
        Assert.Equal(4 + 5, part.Positions.Length);
    }

    /// <summary>A model's pose drawn subdivided: the subdivision of that pose.</summary>
    [Fact]
    public void SubdividedPoseFollowsThePose()
    {
        var (positions, indices) = Icosahedron();
        var model = new Model
        {
            Materials = ["WALL"],
            PartList = [new Model.Part { Vertices = positions, TriangleIndices = indices, TriangleMaterials = new int[indices.Length / 3], TriangleUvs = new Vector2[indices.Length] }],
        };
        var posed = positions.Select(p => p * 2f + Vector3.UnitZ).ToArray();
        var shapes = new ModelShapes(ModelShape.Subdivided);

        var part = shapes.Pose(model, [posed], 0)[0];

        var (expected, normals) = Pose(Subdivide(posed, indices), posed, indices);
        for (var v = 0; v < expected.Length; v++)
        {
            Assert.True(Vector3.Distance(expected[v], part.Positions[v]) < PackedTolerance * 2f, $"{v}: {expected[v]} vs {part.Positions[v]}");
        }

        for (var c = 0; c < normals.Length; c++)
        {
            Near(normals[c], Unpack(part.Normals[c]));
        }
    }

    /// <summary>A hidden part isn't shaped (it isn't drawn).</summary>
    [Fact]
    public void HiddenPartsAreSkipped()
    {
        var model = RidgeModel();
        var shapes = new ModelShapes(ModelShape.Smooth);

        var part = shapes.Pose(model, [model.PartList[0].Vertices], 1)[0];

        Assert.Empty(part.Positions);
    }

    /// <summary>A packed normal (<see cref="Mdk.Engine.Render.Vertex.PackNormal"/>), unit again.</summary>
    private static Vector3 Unpack(uint packed) =>
        Vector3.Normalize(new Vector3((sbyte)packed, (sbyte)(packed >> 8), (sbyte)(packed >> 16)) / sbyte.MaxValue);

    /// <summary>Packed normals: 8 bits a component.</summary>
    private static void Near(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < PackedTolerance, $"{expected} vs {actual}");

    private const float PackedTolerance = 0.03f;

    // --- Settings -----------------------------------------------------------------------------

    [Fact]
    public void SmoothModelsSettingRoundTrips()
    {
        Assert.Equal(SmoothModels.Off, new Settings().SmoothModels);
        Assert.Equal(SmoothModels.On, Settings.Parse(new Settings { SmoothModels = SmoothModels.On }.Format()).SmoothModels);
        Assert.Equal(SmoothModels.On, Settings.Parse("smooth_models=On").SmoothModels);
        Assert.Equal(SmoothModels.Off, Settings.Parse("smooth_models=Bad").SmoothModels);
        // Files from before the setting.
        Assert.Equal(SmoothModels.Off, Settings.Parse("graphics=Enhanced").SmoothModels);
    }
}
