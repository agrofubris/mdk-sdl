using System.Numerics;
using Mdk.Formats;

namespace Mdk.Game.Level;

/// <summary>Whether an arena is under a roof (indoors) or under the sky.</summary>
public enum Cover { Open, Covered }

/// <summary>An arena's faces as the enhanced look's light sees them: how much ceiling there is over
/// its floors, and the faces' mean way. Faces count by area; their outward normal is the negated
/// cross product of their edges (MDK winds a floor's corners clockwise seen from above).
/// <code>
///   ceilings (normal down) ─┐
///                           ├─ ceiling / floor ≥ 1/2: covered
///   floors   (normal up)   ─┘
/// </code></summary>
public static class ArenaShape
{
    /// <summary>Faces turned up (or down) more than this are floors (or ceilings).</summary>
    private const float Horizontal = 0.5f;
    /// <summary>Ceilings over this share of the floors' area make a roof.</summary>
    private const float CoveredShare = 0.5f;

    public static Cover CoverOf(Arena arena)
    {
        var floors = 0f;
        var ceilings = 0f;
        for (var t = 0; t < arena.TriangleCount; t++)
        {
            if (Face(arena, t) is not { } face)
            {
                continue;
            }

            floors += face.Normal.Z > Horizontal ? face.Area : 0f;
            ceilings += face.Normal.Z < -Horizontal ? face.Area : 0f;
        }

        return ceilings > 0f && ceilings >= floors * CoveredShare ? Cover.Covered : Cover.Open;
    }

    /// <summary>The faces' mean up component (-1 all ceilings, 1 all floors).</summary>
    public static float MeanUp(Arena arena)
    {
        var sum = 0f;
        var total = 0f;
        for (var t = 0; t < arena.TriangleCount; t++)
        {
            if (Face(arena, t) is { } face)
            {
                sum += face.Normal.Z * face.Area;
                total += face.Area;
            }
        }

        return total > 0f ? sum / total : 0f;
    }

    /// <summary>How much the faces turn to a light shining along <paramref name="light"/>, on mean
    /// (0 to 1; faces turned away count 0).</summary>
    public static float MeanFacing(Arena arena, Vector3 light)
    {
        var towards = -Vector3.Normalize(light);
        var sum = 0f;
        var total = 0f;
        for (var t = 0; t < arena.TriangleCount; t++)
        {
            if (Face(arena, t) is { } face)
            {
                sum += MathF.Max(Vector3.Dot(face.Normal, towards), 0f) * face.Area;
                total += face.Area;
            }
        }

        return total > 0f ? sum / total : 0f;
    }

    /// <summary>A drawn triangle's outward normal and area; null when it has none or only blocks Kurt.</summary>
    private static (Vector3 Normal, float Area)? Face(Arena arena, int triangle)
    {
        if (arena.ClipFlag != 0 && (arena.TriangleFlags[triangle] & arena.ClipFlag) != 0)
        {
            return null;
        }

        var a = arena.Vertices[arena.TriangleIndices[triangle * 3]];
        var b = arena.Vertices[arena.TriangleIndices[triangle * 3 + 1]];
        var c = arena.Vertices[arena.TriangleIndices[triangle * 3 + 2]];
        var cross = Vector3.Cross(b - a, c - a);
        var length = cross.Length();
        return length > 0f ? (-cross / length, length * 0.5f) : null;
    }
}
