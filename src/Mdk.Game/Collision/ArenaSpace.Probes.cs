using System.Numerics;
using Mdk.Formats;

namespace Mdk.Game.Collision;

/// <summary>The queries of Kurt's ledge grab (damp_ledge_grab 0x469868), the camera's clearance
/// (camera_clearance 0x417ee8) and his fall out of the arena (damp_gravity 0x469efc), in his arena
/// (the first solid one) and, for some, the second.</summary>
public sealed partial class ArenaSpace
{
    /// <summary>An up-facing face a segment crossed: its arena, triangle and the point.</summary>
    public readonly record struct Surface(Bsp Bsp, int Triangle, Vector3 Point)
    {
        public Vector3 Normal => Bsp.PlaneOf(Triangle).Normal;
    }

    /// <summary>The first up-facing face (|nz| ≥ 0.5) the segment crosses (0x421708): in Kurt's arena,
    /// then the second.</summary>
    public Surface? FloorCrossing(Vector3 from, Vector3 to)
    {
        foreach (var bsp in Kurts(from))
        {
            var triangle = bsp.Segment(from, to, Bsp.SegmentMode.Floor, out var point);
            if (triangle != Bsp.None)
            {
                return new Surface(bsp, triangle, point);
            }
        }

        return null;
    }

    /// <summary>The edge of a surface crossed by the line from its point back to <paramref name="back"/>:
    /// the point on it and its direction, or null.</summary>
    public static (Vector3 Point, Vector3 Direction)? Edge(Surface surface, Vector3 back) =>
        surface.Bsp.Edge(surface.Triangle, surface.Point, back);

    /// <summary>Whether a box of half extents <paramref name="half"/> sweeps from <paramref name="from"/>
    /// to <paramref name="to"/> (centres) through Kurt's arena without a hit.</summary>
    public bool Free(Vector3 from, Vector3 to, Vector3 half)
    {
        var own = Kurt(from);
        return own == null || own.SweepBox(from, to, half, 0, 0f, out _, out _) == Bsp.None;
    }

    /// <summary>The first face a box of half extents <paramref name="half"/> meets from
    /// <paramref name="from"/> to <paramref name="to"/>, in Kurt's arena then the second: the box's
    /// centre there and the face's plane.</summary>
    public (Vector3 Point, BspNode Plane)? Sight(Vector3 from, Vector3 to, Vector3 half)
    {
        foreach (var bsp in Kurts(from))
        {
            if (bsp.SweepBox(from, to, half, 0, 0f, out var end, out var node) != Bsp.None && node >= 0)
            {
                return (end, bsp.Arena.Nodes[node]);
            }
        }

        return null;
    }

    /// <summary>Whether the segment crosses a face of Kurt's arena (0x421680).</summary>
    public bool Crosses(Vector3 from, Vector3 to)
    {
        var own = Kurt(from);
        return own != null && own.Segment(from, to, Bsp.SegmentMode.Any, out _) != Bsp.None;
    }

    /// <summary>The lowest point of Kurt's arena (arena+0x44e), or of an arena around the feet (Kurt's
    /// arena is known a step after a teleport), or null.</summary>
    public float? Bottom(Vector3 feet)
    {
        float? bottom = null;
        var own = Kurt(feet);
        foreach (var arena in _arenas)
        {
            if (arena.Bsp == own || Contains(arena.Min, arena.Max, feet))
            {
                bottom = MathF.Min(bottom ?? float.MaxValue, arena.Min.Z);
            }
        }

        return bottom;
    }

    /// <summary>Kurt's arena and the second (or those around the point).</summary>
    private List<Bsp> Kurts(Vector3 point)
    {
        if (_solid.Count > 0)
        {
            return _solid;
        }

        _kurts.Clear();
        AddAt(point, _kurts);
        return _kurts;
    }

    /// <summary>Kurt's arena (or the smallest around the point).</summary>
    private Bsp? Kurt(Vector3 point) => _solid.Count > 0 ? _solid[0] : First(point);
}
