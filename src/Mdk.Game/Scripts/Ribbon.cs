using System.Numerics;

namespace Mdk.Game.Scripts;

/// <summary>A twister's ribbon (trail 0x4390ec/0x4392a4, drawn by 0x439690): each tick the twister's
/// position joins a ring of the last 5; each one holds a vertical edge 2 units high around it. The
/// edges are drawn full size for the newest two only: older ones are scaled by
/// <c>(i + 1 + 5 − n) / 4</c> in integers, which is 0, so the ribbon narrows to a point behind.
/// Consecutive edges make quads, textured with <c>WMIBTEX</c>'s rows 0-16 (first), 16-32 (middle)
/// and 32-48 (last).
/// <code>
///   oldest ●──────●──────┬──────┬ newest      (n = 5: scales 0, 0, 0, 1, 1)
///                        │      │
///                        ┴──────┴
/// </code></summary>
public sealed class Ribbon
{
    public const int Length = 5;
    public const string Texture = "WMIBTEX";
    /// <summary>The edge's half height (its profile points (0, 0, ±1)).</summary>
    private const float HalfHeight = 1f;
    /// <summary>The texel columns across the edge and rows of each segment (0x491d7c...0x491dd8).</summary>
    private const float Left = 0.5f;
    private const float Right = 15.5f;
    private static readonly (float Top, float Bottom)[] Rows = [(0.5f, 16f), (16f, 32f), (32f, 47.5f)];

    private readonly Queue<Vector3> _centres = new();
    private readonly (Vector3 Bottom, Vector3 Top)[] _edges = new (Vector3, Vector3)[Length];

    /// <summary>The positions kept, oldest first.</summary>
    public IReadOnlyCollection<Vector3> Centres => _centres;

    /// <summary>A tick's position (the oldest goes once 5 are kept).</summary>
    public void Push(Vector3 centre)
    {
        if (_centres.Count == Length)
        {
            _centres.Dequeue();
        }

        _centres.Enqueue(centre);
    }

    /// <summary>The scale of edge <paramref name="index"/> (oldest 0) of <paramref name="count"/>.</summary>
    public static float ScaleOf(int index, int count) =>
        index < count - 2 ? (index + 1 + Length - count) / (Length - 1) : 1f;

    /// <summary>The ribbon's triangles (three corners each), UVs in texels of <see cref="Texture"/>.</summary>
    public List<(Vector3 Position, Vector2 Uv)> Triangles()
    {
        var corners = new List<(Vector3 Position, Vector2 Uv)>();
        Triangles(corners);
        return corners;
    }

    /// <summary><see cref="Triangles()"/> into <paramref name="corners"/> (emptied first).</summary>
    public void Triangles(List<(Vector3 Position, Vector2 Uv)> corners)
    {
        corners.Clear();
        var count = _centres.Count;
        if (count < 2)
        {
            return;
        }

        var edges = _edges.AsSpan(0, count);
        var i = 0;
        foreach (var c in _centres)
        {
            edges[i] = (c - Vector3.UnitZ * HalfHeight * ScaleOf(i, count), c + Vector3.UnitZ * HalfHeight * ScaleOf(i, count));
            i++;
        }

        for (var k = 1; k < edges.Length; k++)
        {
            var (top, bottom) = k == 1 ? Rows[0] : k == edges.Length - 1 ? Rows[2] : Rows[1];
            var uv0 = new Vector2(Left, top);
            var uv1 = new Vector2(Right, top);
            var uv2 = new Vector2(Right, bottom);
            var uv3 = new Vector2(Left, bottom);
            var (previous, current) = (edges[k - 1], edges[k]);
            corners.Add((previous.Bottom, uv0));
            corners.Add((previous.Top, uv1));
            corners.Add((current.Top, uv2));
            corners.Add((previous.Bottom, uv0));
            corners.Add((current.Bottom, uv3));
            corners.Add((current.Top, uv2));
        }
    }
}
