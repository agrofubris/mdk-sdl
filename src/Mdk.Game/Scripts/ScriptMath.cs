using System.Numerics;
using Mdk.Formats;

namespace Mdk.Game.Scripts;

/// <summary>Angles in degrees (MDK: yaw 0 = +X, 90 = +Y), boxes and operand conversions shared by
/// the script runtime, the VM and the object motion.</summary>
internal static class ScriptMath
{
    public const float FullTurn = 360f;
    public const float HalfTurn = 180f;

    /// <summary>An angle in [0, 360).</summary>
    public static float Wrap360(float degrees) => degrees - FullTurn * MathF.Floor(degrees / FullTurn);

    /// <summary>An angle in [-180, 180).</summary>
    public static float WrapAngle(float degrees) => degrees - FullTurn * MathF.Floor((degrees + HalfTurn) / FullTurn);

    public static float MoveToward(float from, float to, float delta) =>
        MathF.Abs(to - from) <= delta ? to : from + MathF.Sign(to - from) * delta;

    /// <summary>The unit vector of a heading.</summary>
    public static Vector2 FromAngle(float degrees)
    {
        var radians = float.DegreesToRadians(degrees);
        return new Vector2(MathF.Cos(radians), MathF.Sin(radians));
    }

    public static Vector2 Rotated(Vector2 v, float degrees)
    {
        var (s, c) = MathF.SinCos(float.DegreesToRadians(degrees));
        return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    /// <summary>Turned around Z (the yaw).</summary>
    public static Vector3 RotatedZ(Vector3 v, float degrees)
    {
        var xy = Rotated(new Vector2(v.X, v.Y), degrees);
        return new Vector3(xy.X, xy.Y, v.Z);
    }

    /// <summary>Degrees of the heading from a to b (XY).</summary>
    public static float Heading(Vector3 from, Vector3 to) => float.RadiansToDegrees(MathF.Atan2(to.Y - from.Y, to.X - from.X));

    public static float Distance2D(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Y - b.Y).Length();

    /// <summary>Rounds halves away from zero, like the Godot port.</summary>
    public static int Round(float value) => (int)MathF.Round(value, MidpointRounding.AwayFromZero);

    /// <summary>A numeric operand (int or float) as a float.</summary>
    public static float F(object? operand) => operand switch
    {
        float f => f,
        int i => i,
        _ => 0f,
    };

    /// <summary>A numeric operand (int or float) as an int.</summary>
    public static int I(object? operand) => operand switch
    {
        int i => i,
        float f => (int)f,
        _ => 0,
    };

    /// <summary>A list operand.</summary>
    public static object?[] L(object? operand) => operand as object?[] ?? [];

    /// <summary>Three floats of a list from <paramref name="start"/>.</summary>
    public static Vector3 V(object?[] list, int start = 0) => new(F(list[start]), F(list[start + 1]), F(list[start + 2]));

    // Boxes (Godot's AABB rules: intersections are strict, points inside inclusive).

    public static Vector3 Center(this Box box) => (box.Min + box.Max) * 0.5f;

    public static Vector3 Size(this Box box) => box.Max - box.Min;

    public static bool Intersects(this Box a, Box b) =>
        a.Min.X < b.Max.X && a.Max.X > b.Min.X && a.Min.Y < b.Max.Y && a.Max.Y > b.Min.Y && a.Min.Z < b.Max.Z && a.Max.Z > b.Min.Z;

    public static bool Contains(this Box box, Vector3 p) =>
        p.X >= box.Min.X && p.Y >= box.Min.Y && p.Z >= box.Min.Z && p.X <= box.Max.X && p.Y <= box.Max.Y && p.Z <= box.Max.Z;

    public static Box Grow(this Box box, float amount) => new(box.Min - new Vector3(amount), box.Max + new Vector3(amount));

    public static Box Expand(this Box box, Vector3 p) => new(Vector3.Min(box.Min, p), Vector3.Max(box.Max, p));

    /// <summary>Where a segment enters a box (its start when inside), or null.</summary>
    public static Vector3? SegmentEntry(this Box box, Vector3 from, Vector3 to)
    {
        var d = to - from;
        var enter = 0f;
        var leave = 1f;
        for (var axis = 0; axis < 3; axis++)
        {
            var start = from[axis];
            var step = d[axis];
            if (step == 0f)
            {
                if (start < box.Min[axis] || start > box.Max[axis])
                {
                    return null;
                }

                continue;
            }

            var t0 = (box.Min[axis] - start) / step;
            var t1 = (box.Max[axis] - start) / step;
            enter = MathF.Max(enter, MathF.Min(t0, t1));
            leave = MathF.Min(leave, MathF.Max(t0, t1));
            if (enter > leave)
            {
                return null;
            }
        }

        return from + d * enter;
    }
}
