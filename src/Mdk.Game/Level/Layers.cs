using System.Numerics;
using Mdk.Formats;

namespace Mdk.Game.Level;

/// <summary>The depth layer of each arena triangle. The original draws without a depth buffer, back
/// to front, so a poster in its wall's plane simply covers it; with a depth buffer the two flicker.
/// A triangle overlapping bigger ones of its plane is one layer above the highest of them, and the
/// renderer draws it that much nearer (<see cref="Mdk.Engine.Render.Material.DepthLayer"/>). Its
/// corners stay put: moving it off the wall would open cracks to its neighbours.
/// <code>
///   wall ──────────────  layer 0
///   poster   ────        layer 1    (also when only partly on the wall)
///   sticker   ──         layer 2
/// </code></summary>
public static class Layers
{
    /// <summary>The highest layer: the pull towards the eye stays small (DepthPull).</summary>
    public const int Highest = 3;

    /// <summary>Planes are told apart to 1/64 of a unit.</summary>
    private const float PlaneSteps = 64f;

    /// <summary>Overlaps below this area (square units) are rounding: neighbours sharing an edge.</summary>
    private const float MinOverlap = 1e-3f;

    private const int Corners = 3;

    public static int[] Of(Arena arena)
    {
        var count = arena.TriangleIndices.Length / Corners;
        var layers = new int[count];
        foreach (var plane in Planes(arena, count))
        {
            Stack(arena, plane, layers);
        }

        return layers;
    }

    /// <summary>The triangles of each plane, biggest first; of equal ones, the earlier in the data.</summary>
    private static IEnumerable<List<(int Triangle, float Area, Vector3 Normal)>> Planes(Arena arena, int count)
    {
        var planes = new Dictionary<(int, int, int, int), List<(int, float, Vector3)>>();
        for (var t = 0; t < count; t++)
        {
            var (a, b, c) = (Corner(arena, t, 0), Corner(arena, t, 1), Corner(arena, t, 2));
            var cross = Vector3.Cross(c - a, b - a);
            var area = cross.Length();
            if (area == 0f)
            {
                continue;
            }

            var normal = cross / area;
            var key = (Step(normal.X), Step(normal.Y), Step(normal.Z), Step(Vector3.Dot(normal, a)));
            if (!planes.TryGetValue(key, out var list))
            {
                planes[key] = list = [];
            }

            list.Add((t, area, normal));
        }

        return planes.Values.Select(p => p.OrderByDescending(e => e.Item2).ThenBy(e => e.Item1).ToList());
    }

    /// <summary>Each triangle goes one layer above the highest bigger one it overlaps.</summary>
    private static void Stack(Arena arena, List<(int Triangle, float Area, Vector3 Normal)> plane, int[] layers)
    {
        for (var i = 1; i < plane.Count; i++)
        {
            var t = plane[i].Triangle;
            for (var j = 0; j < i; j++)
            {
                var under = plane[j].Triangle;
                if (layers[under] >= layers[t] && Overlap(arena, t, under, plane[i].Normal) > MinOverlap)
                {
                    layers[t] = Math.Min(layers[under] + 1, Highest);
                }
            }
        }
    }

    private static int Step(float value) => (int)MathF.Round(value * PlaneSteps);

    private static Vector3 Corner(Arena arena, int t, int k) => arena.Vertices[arena.TriangleIndices[t * Corners + k]];

    /// <summary>The area two triangles of a plane share: one clipped by the other's edges
    /// (Sutherland-Hodgman), in the plane's 2D coordinates around the first corner (small numbers).</summary>
    private static float Overlap(Arena arena, int t, int other, Vector3 normal)
    {
        var origin = Corner(arena, t, 0);
        var u = Vector3.Normalize(Vector3.Cross(normal, MathF.Abs(normal.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY));
        var v = Vector3.Cross(normal, u);
        Vector2 Flat(Vector3 p) => new(Vector3.Dot(p - origin, u), Vector3.Dot(p - origin, v));

        var polygon = new List<Vector2> { Flat(origin), Flat(Corner(arena, t, 1)), Flat(Corner(arena, t, 2)) };
        var clip = new[] { Flat(Corner(arena, other, 0)), Flat(Corner(arena, other, 1)), Flat(Corner(arena, other, 2)) };
        var winding = MathF.Sign(Cross(clip[1] - clip[0], clip[2] - clip[0]));
        for (var e = 0; e < Corners && polygon.Count > 0; e++)
        {
            polygon = ClipBy(polygon, clip[e], clip[(e + 1) % Corners], winding);
        }

        var twice = 0f;
        for (var k = 0; k < polygon.Count; k++)
        {
            twice += Cross(polygon[k], polygon[(k + 1) % polygon.Count]);
        }

        return MathF.Abs(twice) / 2f;
    }

    /// <summary>The part of a polygon on the inner side of the edge from <paramref name="a"/> to <paramref name="b"/>.</summary>
    private static List<Vector2> ClipBy(List<Vector2> polygon, Vector2 a, Vector2 b, float winding)
    {
        float Side(Vector2 p) => winding * Cross(b - a, p - a);

        var kept = new List<Vector2>();
        for (var k = 0; k < polygon.Count; k++)
        {
            var (p, q) = (polygon[k], polygon[(k + 1) % polygon.Count]);
            var (sp, sq) = (Side(p), Side(q));
            if (sp >= 0f)
            {
                kept.Add(p);
            }

            if (sp >= 0f != sq >= 0f)
            {
                kept.Add(p + (q - p) * (sp / (sp - sq)));
            }
        }

        return kept;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
}
