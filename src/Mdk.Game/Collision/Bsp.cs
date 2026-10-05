using System.Numerics;
using Mdk.Formats;

namespace Mdk.Game.Collision;

/// <summary>Collisions against an arena's BSP, as the original does them (godot-mdk docs/bsp.md):
/// boxes swept with sliding (<c>bsp_sweep_box</c> 0x4093e0) and segments (0x421680, 0x421708).
/// Triangles flagged 0x20 (not solid) are skipped.
/// <code>
///   node: plane n·p + d ── front list (faces d ≥ 0), back list (faces d &lt; 0)
///         ├─ negative child (d &lt; 0)
///         └─ positive child (d ≥ 0)          -1 = empty leaf
/// </code></summary>
public sealed class Bsp(Arena arena)
{
    public const uint NotSolid = 0x20;
    public const int None = -1;

    /// <summary>Which planes and faces a segment test looks at.</summary>
    public enum SegmentMode
    {
        /// <summary>Every plane, both faces (0x421680).</summary>
        Any,
        /// <summary>Planes with |nz| ≥ 0.5, up-facing triangles only (0x421708).</summary>
        Floor,
    }

    /// <summary>A sweep's contact: the node (its plane) and triangle hit, the box centre there,
    /// and whether the sweep went on sliding from it.</summary>
    public readonly record struct Contact(int Node, int Triangle, Vector3 Point, bool Final);

    /// <summary>Beyond this the pass found nothing (5000 at the start, free under 100).</summary>
    private const float NoHit = 5000f;
    private const float FreeTime = 100f;
    private const float MarginFactor = 2f;
    /// <summary>Faces flatter than this are climbed by a horizontal slide; steeper ones are walls.</summary>
    private const float SteepNz = 0.75f;
    private const float PushOut = 0.01f;
    /// <summary>Coordinate planes the box-triangle test projects on: normal components at least this.</summary>
    private const float ProjectionMin = 0.1f;
    private const float FloorNz = 0.5f;

    // Sweep state (the original keeps it in globals 0x4d4e30-0x4d4eb4).
    private Vector3 _a, _b, _d, _d0, _h, _p, _s;
    private float _margin, _slideLimit, _tBest;
    private bool _canSlide, _slide;
    private int _hitTriangle, _hitNode;

    public Arena Arena { get; } = arena;

    /// <summary>Sweeps a box of half extents <paramref name="h"/> from <paramref name="a"/> to
    /// <paramref name="b"/> (centres) in up to <paramref name="iterations"/> + 1 passes, sliding
    /// along faces met at least so obliquely that <c>(D·n)² ≤ slideK·|D|²</c>. Returns the triangle
    /// of the last pass's contact (<see cref="None"/> when it ended free) and where the box ends.</summary>
    public int SweepBox(Vector3 a, Vector3 b, Vector3 h, int iterations, float slideK, out Vector3 end, out int node,
        Action<Contact>? contacts = null)
    {
        node = None;
        end = a;
        if (Arena.Nodes.Length == 0 || Arena.Vertices.Length == 0)
        {
            return None;
        }

        _a = a;
        _b = b;
        _h = h;
        _d0 = b - a;
        _slideLimit = slideK * _d0.LengthSquared();
        _margin = MarginFactor * (MathF.Abs(h.X) + MathF.Abs(h.Y) + MathF.Abs(h.Z));
        _slide = true;
        while (iterations >= 0 && _slide)
        {
            _canSlide = iterations != 0;
            _slide = false;
            _hitTriangle = None;
            _tBest = NoHit;
            _d = _b - _a;
            SweepNode(0);
            if (_slide)
            {
                // Slid: the next pass goes from the contact to the slid end.
                contacts?.Invoke(new Contact(_hitNode, _hitTriangle, _p, false));
                _a = _p;
                _b = _s;
            }
            else if (_hitTriangle != None)
            {
                contacts?.Invoke(new Contact(_hitNode, _hitTriangle, _p, true));
            }

            iterations--;
        }

        if (_tBest >= FreeTime)
        {
            end = _b;
            return None;
        }

        end = _p;
        node = _hitNode;
        return _hitTriangle;
    }

    /// <summary>One node and what lies behind it (0x409680). Children on the start's side(s) first.</summary>
    private void SweepNode(int index)
    {
        while (true)
        {
            var n = Arena.Nodes[index];
            var r = MathF.Abs(_h.X * n.Normal.X) + MathF.Abs(_h.Y * n.Normal.Y) + MathF.Abs(_h.Z * n.Normal.Z);
            var dA = n.Distance(_a);
            var dB = n.Distance(_b);
            if (dA >= -_margin && n.Positive >= 0)
            {
                SweepNode(n.Positive);
            }

            if (dA <= _margin && n.Negative >= 0)
            {
                SweepNode(n.Negative);
            }

            var sA = Sides(dA);
            var sB = Sides(dB);
            var towards = (dA < 0f && dA <= dB) || (dA >= 0f && dB <= dA);
            if ((sA | sB) == 3 && towards)
            {
                TestPlane(index, n, dA, r);
            }

            // Go on only into the sides the end reaches and the start doesn't.
            var fresh = sB & (sA ^ sB);
            var next = (fresh & 1) != 0 ? n.Positive : (fresh & 2) != 0 ? n.Negative : None;
            if (next < 0)
            {
                return;
            }

            index = next;
        }
    }

    /// <summary>Bands within the margin of a plane: 1 not far behind, 2 not far in front.</summary>
    private int Sides(float distance) => (distance >= -_margin ? 1 : 0) | (distance <= _margin ? 2 : 0);

    /// <summary>Where the box touches the plane, the triangles there facing the start, and the slide.</summary>
    private void TestPlane(int index, BspNode n, float dA, float r)
    {
        var offset = dA >= 0f ? MathF.Min(dA, r) : MathF.Max(dA, -r);
        var den = Vector3.Dot(_d, n.Normal);
        if (den == 0f)
        {
            return;
        }

        var t = -(dA - offset) / den;
        if (t > _tBest || t > 1f)
        {
            return;
        }

        _p = _a + t * _d;
        var (first, count) = dA < 0f ? (n.BackFirst, n.BackCount) : (n.FrontFirst, n.FrontCount);
        var triangle = TestTriangles(_p, n, first, count);
        if (triangle == None)
        {
            // Retry where the centre crosses the plane; the contact point stays (a quirk of the original).
            var t2 = MathF.Min(-dA / den, 1f);
            triangle = TestTriangles(_a + t2 * _d, n, first, count);
            if (triangle != None)
            {
                t = t2;
            }
        }

        if (triangle == None)
        {
            return;
        }

        _hitTriangle = triangle;
        _hitNode = index;
        if (_canSlide)
        {
            Slide(n, t, den, r);
        }

        _tBest = t;
    }

    /// <summary>Not too head-on: the end moves along the face. Walls and steep up-slopes slide in XY
    /// only; floors and down-facing faces are projected in 3D. Too head-on: it stops at the contact.</summary>
    private void Slide(BspNode n, float t, float den, float r)
    {
        var along = Vector3.Dot(_d0, n.Normal);
        if (along * along > _slideLimit)
        {
            _slide = false;
            _s = _p;
            return;
        }

        var s = (1f - t) * den;
        var nz = n.Normal.Z;
        _slide = true;
        _s = new Vector3(_b.X - s * n.Normal.X, _b.Y - s * n.Normal.Y, _b.Z);
        var steep = (nz >= 0f && nz < SteepNz && den <= 0f) || (nz > -SteepNz && nz <= 0f && den >= 0f);
        if (steep)
        {
            _s = PushOut3(_s, n, r + PushOut, Axes.Xy);
            return;
        }

        _s.Z = _b.Z - s * nz;
        _s = PushOut3(_s, n, r + PushOut, Axes.Xyz);
    }

    private enum Axes { Xy, Xyz }

    /// <summary>Within <paramref name="e"/> of the plane: moved along the normal to exactly ±e on its side.</summary>
    private static Vector3 PushOut3(Vector3 p, BspNode n, float e, Axes axes)
    {
        var d = n.Distance(p);
        if (MathF.Abs(d) > e)
        {
            return p;
        }

        var k = d >= 0f ? e - d : -d - e;
        var push = k * n.Normal;
        return axes == Axes.Xy ? new Vector3(p.X + push.X, p.Y + push.Y, p.Z) : p + push;
    }

    /// <summary>The first solid triangle of a node list the box at <paramref name="p"/> overlaps (0x409c40).</summary>
    private int TestTriangles(Vector3 p, BspNode n, int first, int count)
    {
        for (var t = first; t < first + count; t++)
        {
            if ((Arena.TriangleFlags[t] & NotSolid) != 0)
            {
                continue;
            }

            var v0 = Arena.Vertices[Arena.TriangleIndices[t * 3]] - p;
            var v1 = Arena.Vertices[Arena.TriangleIndices[t * 3 + 1]] - p;
            var v2 = Arena.Vertices[Arena.TriangleIndices[t * 3 + 2]] - p;
            if (BoxOverlaps(v0, v1, v2, _h, n.Normal))
            {
                return t;
            }
        }

        return None;
    }

    /// <summary>Box (centred at the origin) against a triangle, projected on each coordinate plane the
    /// node's normal isn't almost parallel to (0x409de0).</summary>
    private static bool BoxOverlaps(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 h, Vector3 normal)
    {
        for (var k = 0; k < 3; k++)
        {
            if (MathF.Abs(normal[k]) < ProjectionMin)
            {
                continue;
            }

            var (a, b) = k switch { 0 => (1, 2), 1 => (2, 0), _ => (0, 1) };
            var q0 = new Vector2(v0[a], v0[b]);
            var q1 = new Vector2(v1[a], v1[b]);
            var q2 = new Vector2(v2[a], v2[b]);
            if (!RectOverlaps(q0, q1, q2, new Vector2(h[a], h[b])))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Exact overlap of the rectangle ±half with a triangle: outcodes, then the triangle's v
    /// range within the strip |u| ≤ half.u.</summary>
    private static bool RectOverlaps(Vector2 q0, Vector2 q1, Vector2 q2, Vector2 half)
    {
        int Code(Vector2 q) => (q.X < -half.X ? 1 : 0) | (q.X > half.X ? 2 : 0) | (q.Y < -half.Y ? 4 : 0) | (q.Y > half.Y ? 8 : 0);
        int c0 = Code(q0), c1 = Code(q1), c2 = Code(q2);
        if (c0 == 0 || c1 == 0 || c2 == 0)
        {
            return true;
        }

        if ((c0 & c1 & c2) != 0)
        {
            return false;
        }

        var low = float.MaxValue;
        var high = float.MinValue;
        void Add(float v)
        {
            low = MathF.Min(low, v);
            high = MathF.Max(high, v);
        }

        Span<Vector2> corners = [q0, q1, q2];
        for (var i = 0; i < 3; i++)
        {
            var p = corners[i];
            var q = corners[(i + 1) % 3];
            if (MathF.Abs(p.X) <= half.X)
            {
                Add(p.Y);
            }

            // Edge crossings of u = -half.u and u = +half.u.
            foreach (var u in (ReadOnlySpan<float>)[-half.X, half.X])
            {
                if ((p.X - u) * (q.X - u) < 0f)
                {
                    Add(p.Y + (q.Y - p.Y) * (u - p.X) / (q.X - p.X));
                }
            }
        }

        return low <= high && low <= half.Y && high >= -half.Y;
    }

    /// <summary>The nearest triangle a segment crosses (front-to-back walk 0x421470), or <see cref="None"/>.</summary>
    public int Segment(Vector3 a, Vector3 b, SegmentMode mode, out Vector3 point)
    {
        point = b;
        if (Arena.Nodes.Length == 0)
        {
            return None;
        }

        var (triangle, hit) = Walk(0, a, b, mode);
        if (triangle != None)
        {
            point = hit;
        }

        return triangle;
    }

    private (int Triangle, Vector3 Point) Walk(int index, Vector3 a, Vector3 b, SegmentMode mode)
    {
        while (true)
        {
            var n = Arena.Nodes[index];
            var dA = n.Distance(a);
            var dB = n.Distance(b);
            var near = dA < 0f ? n.Negative : n.Positive;
            if (near >= 0)
            {
                var found = Walk(near, a, b, mode);
                if (found.Triangle != None)
                {
                    return found;
                }
            }

            // No strict crossing: the far side isn't visited.
            if (dA * dB >= 0f)
            {
                return (None, default);
            }

            var hit = CrossingTriangle(n, a, b, dA, mode);
            if (hit.Triangle != None)
            {
                return hit;
            }

            var far = dA < 0f ? n.Positive : n.Negative;
            if (far < 0)
            {
                return (None, default);
            }

            index = far;
        }
    }

    private (int Triangle, Vector3 Point) CrossingTriangle(BspNode n, Vector3 a, Vector3 b, float dA, SegmentMode mode)
    {
        var nz = n.Normal.Z;
        if (mode == SegmentMode.Floor && MathF.Abs(nz) < FloorNz)
        {
            return (None, default);
        }

        var d = b - a;
        var dot = Vector3.Dot(d, n.Normal);
        var x = a + d * (dot == 0f ? 1f : -dA / dot);
        if (mode == SegmentMode.Floor)
        {
            var (first, count) = nz >= FloorNz ? (n.FrontFirst, n.FrontCount) : (n.BackFirst, n.BackCount);
            return (PointTriangle(x, n, first, count), x);
        }

        var front = PointTriangle(x, n, n.FrontFirst, n.FrontCount);
        return (front != None ? front : PointTriangle(x, n, n.BackFirst, n.BackCount), x);
    }

    /// <summary>The first solid triangle of a list holding a point of its plane (0x421350, 0x42de60):
    /// crossings of a ray along +u, with the normal's largest axis dropped.</summary>
    private int PointTriangle(Vector3 x, BspNode n, int first, int count)
    {
        var abs = Vector3.Abs(n.Normal);
        var (ua, va) = abs.X >= abs.Y && abs.X >= abs.Z ? (1, 2) : abs.Y >= abs.Z ? (0, 2) : (0, 1);
        for (var t = first; t < first + count; t++)
        {
            if ((Arena.TriangleFlags[t] & NotSolid) != 0)
            {
                continue;
            }

            var inside = false;
            for (var k = 0; k < 3; k++)
            {
                var p = Arena.Vertices[Arena.TriangleIndices[t * 3 + k]] - x;
                var q = Arena.Vertices[Arena.TriangleIndices[t * 3 + (k + 1) % 3]] - x;
                var pAbove = p[va] >= 0f;
                var qAbove = q[va] >= 0f;
                if (pAbove == qAbove)
                {
                    continue;
                }

                // Where the edge crosses v = 0, on the +u side?
                var u = p[ua] + (q[ua] - p[ua]) * (-p[va]) / (q[va] - p[va]);
                if (u > 0f)
                {
                    inside = !inside;
                }
            }

            if (inside)
            {
                return t;
            }
        }

        return None;
    }
}
