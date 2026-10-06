using System.Numerics;
using Mdk.Formats;

namespace Mdk.Game.Level;

/// <summary>Outlined arena triangles (0x40b7f0; godot-mdk mdk_mesh_builder.gd <c>_add_outlines</c>):
/// flag bit 23 draws lines in the triangle's own colour along the edges bits 20-22 pick, so the
/// untextured glass panes get coloured frames.
/// <code>
///        v2
///   bit 22 ╱╲ bit 21
///        ╱    ╲
///      v0──────v1
///        bit 20
/// </code></summary>
/// <summary>Which flagged edges <see cref="Outlines.Of"/> draws.</summary>
public enum OutlineEdges
{
    /// <summary>Every flagged edge, as the original.</summary>
    Flagged,

    /// <summary>Not those two coplanar triangles share (the enhanced look): the editor flagged some
    /// diagonals inside glass panes, e.g. the fans of level 7's DANT_7 columns.</summary>
    Frame,
}

public static class Outlines
{
    public const uint Outlined = 1u << 23;

    private static readonly (uint Bit, int From, int To)[] Edges = [(1u << 20, 0, 1), (1u << 21, 1, 2), (1u << 22, 2, 0)];

    /// <summary>Normals at most ~2.5° apart: one flat surface.</summary>
    private const float CoplanarCos = 0.999f;

    /// <summary>The outlined edges of <paramref name="triangles"/>, two points each.</summary>
    public static List<Vector3> Of(Arena arena, IEnumerable<int> triangles, OutlineEdges edges = OutlineEdges.Flagged)
    {
        var lines = new List<Vector3>();
        var list = triangles.ToList();
        var inner = edges == OutlineEdges.Frame ? InnerEdges(arena, list) : [];
        foreach (var t in list)
        {
            var flags = arena.TriangleFlags[t];
            if ((flags & Outlined) == 0)
            {
                continue;
            }

            foreach (var (bit, from, to) in Edges)
            {
                var a = arena.TriangleIndices[t * 3 + from];
                var b = arena.TriangleIndices[t * 3 + to];
                if ((flags & bit) == 0 || inner.Contains(Key(a, b)))
                {
                    continue;
                }

                lines.Add(arena.Vertices[a]);
                lines.Add(arena.Vertices[b]);
            }
        }

        return lines;
    }

    /// <summary>The edges (vertex index pairs) two coplanar triangles of <paramref name="triangles"/> share.
    /// <code>
    ///   v2 ┌──┐ v3     v1-v2 is inner: both halves lie in one plane
    ///      │╲ │
    ///   v0 └──┘ v1
    /// </code></summary>
    private static HashSet<(int, int)> InnerEdges(Arena arena, List<int> triangles)
    {
        var normals = new Dictionary<(int, int), Vector3>();
        var inner = new HashSet<(int, int)>();
        foreach (var t in triangles)
        {
            var normal = Normal(arena, t);
            foreach (var (_, from, to) in Edges)
            {
                var key = Key(arena.TriangleIndices[t * 3 + from], arena.TriangleIndices[t * 3 + to]);
                if (!normals.TryAdd(key, normal) && MathF.Abs(Vector3.Dot(normals[key], normal)) > CoplanarCos)
                {
                    inner.Add(key);
                }
            }
        }

        return inner;
    }

    private static Vector3 Normal(Arena arena, int t)
    {
        var v0 = arena.Vertices[arena.TriangleIndices[t * 3]];
        var v1 = arena.Vertices[arena.TriangleIndices[t * 3 + 1]];
        var v2 = arena.Vertices[arena.TriangleIndices[t * 3 + 2]];
        return Vector3.Normalize(Vector3.Cross(v1 - v0, v2 - v0));
    }

    private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);
}
