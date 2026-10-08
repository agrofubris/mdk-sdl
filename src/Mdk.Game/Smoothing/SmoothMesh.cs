using System.Numerics;

namespace Mdk.Game.Smoothing;

/// <summary>Smooth normals of a triangle mesh with hard edges kept: a corner's normal is the mean of
/// the faces around its vertex that meet it at most <c>crease</c> degrees apart, else its face's;
/// faces weigh by area (a big face keeps its light next to a small bevel). Found once from the rest
/// pose (welded by position, either winding: models show both faces); <see cref="Normals"/> then
/// takes any pose of the same vertices.
/// <code>
///      ╱╲ 20°: one normal ↑      ┌──┐ 90°: each face its own
///   ──╱  ╲──                     │  │
/// </code>
/// Each corner gets a slot (the faces sharing its normal) and a sign (its face's winding against
/// the slot's first face's).</summary>
public sealed class SmoothMesh
{
    /// <summary>Faces further apart than this keep a hard edge (degrees).</summary>
    public const float DefaultCrease = 45f;
    private const int TriangleVertices = 3;
    /// <summary>Faces with a smaller normal (twice their area) are lines: no normal of their own.</summary>
    private const float Degenerate = 1e-10f;

    /// <summary>An edge between welded vertices <see cref="A"/> and <see cref="B"/>, and its sides:
    /// per face, the corners at A and at B, and whether the face is wound against the first side's.</summary>
    public sealed class Edge(int a, int b)
    {
        public readonly int A = a;
        public readonly int B = b;
        public readonly List<(int AtA, int AtB, float Sign)> Sides = [];
    }

    private readonly int[] _indices;
    private readonly int[] _slots;
    private readonly float[] _signs;
    private readonly int _slotCount;

    /// <summary>Per corner, the edge to the next corner of its triangle.</summary>
    public int[] CornerEdges { get; }

    public IReadOnlyList<Edge> Edges { get; }

    /// <summary>The mesh's vertex count (positions of any pose).</summary>
    public int VertexCount { get; }

    private SmoothMesh(int[] indices, int vertexCount, int[] slots, float[] signs, int slotCount, int[] cornerEdges, List<Edge> edges)
    {
        _indices = indices;
        VertexCount = vertexCount;
        _slots = slots;
        _signs = signs;
        _slotCount = slotCount;
        CornerEdges = cornerEdges;
        Edges = edges;
    }

    /// <summary>The corners' vertices, three per triangle.</summary>
    public ReadOnlySpan<int> Indices => _indices;

    public static SmoothMesh Build(ReadOnlySpan<Vector3> positions, ReadOnlySpan<int> indices, float crease)
    {
        var weld = Weld(positions);
        var faces = FaceNormals(positions, indices);
        var (edges, cornerEdges) = FindEdges(indices, weld);

        // Corners linked across smooth edges, with the parity of their faces' windings.
        var links = new List<(int To, float Sign)>[indices.Length];
        var limit = MathF.Cos(float.DegreesToRadians(crease));
        foreach (var edge in edges)
        {
            if (edge.Sides.Count != 2)
            {
                continue;
            }

            var (a0, b0, _) = edge.Sides[0];
            var (a1, b1, sign) = edge.Sides[1];
            var n0 = faces[a0 / TriangleVertices];
            var n1 = faces[a1 / TriangleVertices];
            if (n0 == Vector3.Zero || n1 == Vector3.Zero || Vector3.Dot(n0, n1 * sign) < limit)
            {
                continue;
            }

            Link(links, a0, a1, sign);
            Link(links, b0, b1, sign);
        }

        var (slots, signs, count) = Group(links);
        return new SmoothMesh(indices.ToArray(), positions.Length, slots, signs, count, cornerEdges, edges);
    }

    /// <summary>Normals shared by corners (fewer than corners).</summary>
    public int SlotCount => _slotCount;

    /// <summary>A corner's slot.</summary>
    public int SlotOf(int corner) => _slots[corner];

    /// <summary>Each corner's normal for a pose (unit, towards its face's side; a line's is zero).</summary>
    public void Normals(ReadOnlySpan<Vector3> positions, Span<Vector3> corners)
    {
        Span<Vector3> slots = _slotCount <= StackSlots ? stackalloc Vector3[_slotCount] : new Vector3[_slotCount];
        SlotNormals(positions, slots);
        Corners(slots, corners);
    }

    /// <summary>Each slot's normal for a pose (unit, towards its first corner's face; or zero).</summary>
    public void SlotNormals(ReadOnlySpan<Vector3> positions, Span<Vector3> slots)
    {
        slots.Clear();
        for (var c = 0; c < _indices.Length; c += TriangleVertices)
        {
            // Twice the face's area along its normal.
            var a = positions[_indices[c]];
            var weighted = Vector3.Cross(positions[_indices[c + 1]] - a, positions[_indices[c + 2]] - a);
            for (var k = 0; k < TriangleVertices; k++)
            {
                slots[_slots[c + k]] += weighted * _signs[c + k];
            }
        }

        for (var s = 0; s < _slotCount; s++)
        {
            slots[s] = slots[s].LengthSquared() > Degenerate ? Vector3.Normalize(slots[s]) : Vector3.Zero;
        }
    }

    /// <summary>Each corner's normal from its slot's, turned to its face's side.</summary>
    public void Corners(ReadOnlySpan<Vector3> slots, Span<Vector3> corners)
    {
        for (var c = 0; c < _indices.Length; c++)
        {
            corners[c] = slots[_slots[c]] * _signs[c];
        }
    }

    private const int StackSlots = 1024;

    /// <summary>Each vertex's first vertex at the same position.</summary>
    private static int[] Weld(ReadOnlySpan<Vector3> positions)
    {
        var first = new Dictionary<Vector3, int>();
        var weld = new int[positions.Length];
        for (var v = 0; v < positions.Length; v++)
        {
            weld[v] = first.TryGetValue(positions[v], out var w) ? w : first[positions[v]] = v;
        }

        return weld;
    }

    private static Vector3[] FaceNormals(ReadOnlySpan<Vector3> positions, ReadOnlySpan<int> indices)
    {
        var normals = new Vector3[indices.Length / TriangleVertices];
        for (var t = 0; t < normals.Length; t++)
        {
            var a = positions[indices[t * TriangleVertices]];
            var normal = Vector3.Cross(positions[indices[t * TriangleVertices + 1]] - a, positions[indices[t * TriangleVertices + 2]] - a);
            normals[t] = normal.LengthSquared() > Degenerate ? Vector3.Normalize(normal) : Vector3.Zero;
        }

        return normals;
    }

    /// <summary>The welded edges and their sides; a side wound A to B like the first is wound against it.</summary>
    private static (List<Edge> Edges, int[] CornerEdges) FindEdges(ReadOnlySpan<int> indices, int[] weld)
    {
        var edges = new List<Edge>();
        var byEnds = new Dictionary<(int, int), int>();
        var cornerEdges = new int[indices.Length];
        for (var c = 0; c < indices.Length; c++)
        {
            var first = c - c % TriangleVertices;
            var next = first + (c - first + 1) % TriangleVertices;
            var (u, v) = (weld[indices[c]], weld[indices[next]]);
            var key = (Math.Min(u, v), Math.Max(u, v));
            if (!byEnds.TryGetValue(key, out var e))
            {
                e = byEnds[key] = edges.Count;
                edges.Add(new Edge(u, v));
            }

            var edge = edges[e];
            var forward = edge.A == u;
            // The first side goes A to B; another going A to B too is wound the other way.
            var sign = edge.Sides.Count == 0 || !forward ? 1f : -1f;
            edge.Sides.Add(forward ? (c, next, sign) : (next, c, sign));
            cornerEdges[c] = e;
        }

        return (edges, cornerEdges);
    }

    private static void Link(List<(int To, float Sign)>[] links, int from, int to, float sign)
    {
        (links[from] ??= []).Add((to, sign));
        (links[to] ??= []).Add((from, sign));
    }

    /// <summary>Linked corners into slots (a walk from each unvisited corner), signs relative to its first.</summary>
    private static (int[] Slots, float[] Signs, int Count) Group(List<(int To, float Sign)>[] links)
    {
        var slots = new int[links.Length];
        var signs = new float[links.Length];
        Array.Fill(slots, -1);
        var count = 0;
        var pending = new Stack<int>();
        for (var start = 0; start < links.Length; start++)
        {
            if (slots[start] >= 0)
            {
                continue;
            }

            slots[start] = count;
            signs[start] = 1f;
            pending.Push(start);
            while (pending.TryPop(out var corner))
            {
                foreach (var (to, sign) in links[corner] ?? [])
                {
                    if (slots[to] >= 0)
                    {
                        continue;
                    }

                    slots[to] = count;
                    signs[to] = signs[corner] * sign;
                    pending.Push(to);
                }
            }

            count++;
        }

        return (slots, signs, count);
    }
}
