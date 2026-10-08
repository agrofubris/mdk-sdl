using System.Numerics;

namespace Mdk.Game.Smoothing;

/// <summary>One level of PN-triangle subdivision (Vlachos et al. 2001): each triangle becomes four
/// through its edges' midpoints, each midpoint on the cubic curve its edge's end normals make. The
/// corners stay where they are (what the scripts and collisions see).
/// <code>
///         v2                      smooth edge: end normals ⟂ to it, the midpoint stays on it;
///        ╱  ╲                     leaning out, it bows out. Hard edges (creases) stay straight:
///      m20──m12                   a box stays a box. UVs: each face's own corners' mean (texture
///      ╱ ╲  ╱ ╲                   seams kept); normals: each face's own corners' mean.
///    v0───m01───v1
/// </code>
/// A midpoint is shared by every face along its edge (no cracks); an edge of more than two faces, or
/// of a pinned face (glass: its panes stay flat), is split straight.</summary>
public sealed class Subdivision
{
    private const int TriangleVertices = 3;
    private const int Pieces = 4;
    private const float Degenerate = 1e-10f;
    /// <summary>The cubic Bézier's midpoint: (P0 + 3 B1 + 3 B2 + P3) / 8.</summary>
    private const float Ends = 1f / 8f;
    private const float Controls = 3f / 8f;

    private readonly SmoothMesh _mesh;
    /// <summary>Per new corner: the two old corners whose normals it takes the mean of (the same for an old corner).</summary>
    private readonly (int First, int Second)[] _normalOf;
    /// <summary>Per edge: whether it splits straight.</summary>
    private readonly bool[] _straight;

    /// <summary>Per new corner: its position (an old vertex, or the vertex count + its edge).</summary>
    public int[] Vertices { get; }

    /// <summary>Per new corner: its UV.</summary>
    public Vector2[] Uvs { get; }

    /// <summary>Per new triangle: the old triangle it's a piece of.</summary>
    public int[] Sources { get; }

    /// <summary>Positions of a pose: the old vertices, then each edge's midpoint.</summary>
    public int PositionCount => _mesh.VertexCount + _mesh.Edges.Count;

    private Subdivision(SmoothMesh mesh, int[] vertices, Vector2[] uvs, int[] sources, (int, int)[] normalOf, bool[] straight)
    {
        _mesh = mesh;
        Vertices = vertices;
        Uvs = uvs;
        Sources = sources;
        _normalOf = normalOf;
        _straight = straight;
    }

    /// <summary>The pieces of <paramref name="mesh"/>'s triangles, <paramref name="uvs"/> per old corner;
    /// <paramref name="pinned"/> per old triangle (none when empty).</summary>
    public static Subdivision Build(SmoothMesh mesh, ReadOnlySpan<Vector2> uvs, ReadOnlySpan<bool> pinned = default)
    {
        var indices = mesh.Indices;
        var triangles = indices.Length / TriangleVertices;
        var vertices = new int[triangles * Pieces * TriangleVertices];
        var newUvs = new Vector2[vertices.Length];
        var normalOf = new (int, int)[vertices.Length];
        var sources = new int[triangles * Pieces];
        var at = 0;
        Span<int> position = stackalloc int[2 * TriangleVertices];
        for (var t = 0; t < triangles; t++)
        {
            var c = t * TriangleVertices;
            // Old corners 0-2, then the midpoints after each: 3 = 0-1, 4 = 1-2, 5 = 2-0.
            Span<int> corner = [c, c + 1, c + 2, c, c + 1, c + 2];
            Span<int> other = [c, c + 1, c + 2, c + 1, c + 2, c];
            for (var k = 0; k < TriangleVertices; k++)
            {
                position[k] = indices[c + k];
                position[TriangleVertices + k] = mesh.VertexCount + mesh.CornerEdges[c + k];
            }

            // Wound as the old one: v0 m01 m20, m01 v1 m12, m20 m12 v2, m01 m12 m20.
            ReadOnlySpan<int> pieces = [0, 3, 5, 3, 1, 4, 5, 4, 2, 3, 4, 5];
            foreach (var k in pieces)
            {
                vertices[at] = position[k];
                newUvs[at] = (uvs[corner[k]] + uvs[other[k]]) / 2f;
                normalOf[at] = (corner[k], other[k]);
                at++;
            }

            for (var p = 0; p < Pieces; p++)
            {
                sources[t * Pieces + p] = t;
            }
        }

        var straight = new bool[mesh.Edges.Count];
        for (var e = 0; e < straight.Length; e++)
        {
            straight[e] = IsStraight(mesh, mesh.Edges[e], pinned);
        }

        return new Subdivision(mesh, vertices, newUvs, sources, normalOf, straight);
    }

    /// <summary>A pose's positions (<see cref="PositionCount"/>) and new corners' normals, from its old
    /// positions and corner normals (<see cref="SmoothMesh.Normals"/>).</summary>
    public void Pose(ReadOnlySpan<Vector3> positions, ReadOnlySpan<Vector3> corners, Span<Vector3> outPositions, Span<Vector3> outNormals)
    {
        positions.CopyTo(outPositions);
        var edges = _mesh.Edges;
        for (var e = 0; e < edges.Count; e++)
        {
            outPositions[_mesh.VertexCount + e] = Midpoint(edges[e], _straight[e], positions, corners);
        }

        for (var c = 0; c < _normalOf.Length; c++)
        {
            var (first, second) = _normalOf[c];
            var sum = corners[first] + corners[second];
            outNormals[c] = sum.LengthSquared() > Degenerate ? Vector3.Normalize(sum) : Vector3.Zero;
        }
    }

    /// <summary>An edge's midpoint on the curve of its end normals, or straight.</summary>
    private static Vector3 Midpoint(SmoothMesh.Edge edge, bool straight, ReadOnlySpan<Vector3> positions, ReadOnlySpan<Vector3> corners)
    {
        var a = positions[edge.A];
        var b = positions[edge.B];
        var (atA, atB, _) = edge.Sides[0];
        var normalA = corners[atA];
        var normalB = corners[atB];
        if (straight || normalA.LengthSquared() <= Degenerate || normalB.LengthSquared() <= Degenerate)
        {
            return (a + b) / 2f;
        }

        normalA = Vector3.Normalize(normalA);
        normalB = Vector3.Normalize(normalB);
        var controlA = (2f * a + b - Vector3.Dot(b - a, normalA) * normalA) / 3f;
        var controlB = (2f * b + a - Vector3.Dot(a - b, normalB) * normalB) / 3f;
        return (a + b) * Ends + (controlA + controlB) * Controls;
    }

    /// <summary>Whether an edge splits straight: more than two faces meet, one is pinned, or its two
    /// faces don't share their normals at both ends (a hard edge).</summary>
    private static bool IsStraight(SmoothMesh mesh, SmoothMesh.Edge edge, ReadOnlySpan<bool> pinned)
    {
        if (edge.Sides.Count > 2)
        {
            return true;
        }

        foreach (var (atA, _, _) in edge.Sides)
        {
            var triangle = atA / TriangleVertices;
            if (triangle < pinned.Length && pinned[triangle])
            {
                return true;
            }
        }

        if (edge.Sides.Count < 2)
        {
            return false;
        }

        var (a0, b0, _) = edge.Sides[0];
        var (a1, b1, _) = edge.Sides[1];
        return mesh.SlotOf(a0) != mesh.SlotOf(a1) || mesh.SlotOf(b0) != mesh.SlotOf(b1);
    }
}
