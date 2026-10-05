using System.Numerics;
using Mdk.Formats;

namespace Mdk.Game.Level;

/// <summary>The corners of an arena's triangles, three per triangle, with details lifted off the
/// walls they lie on. The original draws without a depth buffer, back to front, so a poster in its
/// wall's plane simply covers it; with a depth buffer it would flicker. A triangle is lifted towards
/// its front by <see cref="Lift"/> for each bigger triangle of the same plane covering its centre.
/// <code>
///   wall ──────────────  layer 0
///   poster   ────        layer 1 (+0.03 towards the viewer)
/// </code></summary>
public static class Layers
{
    private const float Lift = 0.03f;
    /// <summary>Planes are told apart to 1/64 of a unit.</summary>
    private const float PlaneSteps = 64f;

    public static Vector3[] Positions(Arena arena)
    {
        var positions = arena.TriangleIndices.Select(i => arena.Vertices[i]).ToArray();
        LiftLayers(positions);
        return positions;
    }

    private static void LiftLayers(Vector3[] positions)
    {
        var count = positions.Length / 3;
        var normals = new Vector3[count];
        var areas = new float[count];
        var planes = new Dictionary<(int, int, int, int), List<int>>();
        for (var t = 0; t < count; t++)
        {
            var cross = Vector3.Cross(positions[t * 3 + 2] - positions[t * 3], positions[t * 3 + 1] - positions[t * 3]);
            areas[t] = cross.Length();
            if (areas[t] == 0f)
            {
                continue;
            }

            normals[t] = cross / areas[t];
            var key = (Step(normals[t].X), Step(normals[t].Y), Step(normals[t].Z), Step(Vector3.Dot(normals[t], positions[t * 3])));
            if (!planes.TryGetValue(key, out var list))
            {
                planes[key] = list = [];
            }

            list.Add(t);
        }

        var lifts = new float[count];
        foreach (var triangles in planes.Values)
        {
            foreach (var a in triangles)
            {
                foreach (var b in triangles)
                {
                    // Equal ones: the later in the data goes on top.
                    var bigger = areas[b] > areas[a] || (areas[b] == areas[a] && b < a);
                    if (bigger && Covers(positions, b, a, normals[b]))
                    {
                        lifts[a] += Lift;
                    }
                }
            }
        }

        for (var t = 0; t < count; t++)
        {
            for (var k = 0; k < 3; k++)
            {
                positions[t * 3 + k] += normals[t] * lifts[t];
            }
        }
    }

    private static int Step(float value) => (int)MathF.Round(value * PlaneSteps);

    /// <summary>Whether triangle <paramref name="cover"/> holds the centre of triangle <paramref name="t"/>.</summary>
    private static bool Covers(Vector3[] positions, int cover, int t, Vector3 normal)
    {
        var centre = (positions[t * 3] + positions[t * 3 + 1] + positions[t * 3 + 2]) / 3f;
        for (var i = 0; i < 3; i++)
        {
            var a = positions[cover * 3 + i];
            var b = positions[cover * 3 + (i + 1) % 3];
            var c = positions[cover * 3 + (i + 2) % 3];
            if (Vector3.Dot(normal, Vector3.Cross(b - a, centre - a)) * Vector3.Dot(normal, Vector3.Cross(b - a, c - a)) < 0f)
            {
                return false;
            }
        }

        return true;
    }
}
