using Mdk.Formats;

namespace Mdk.Game.Hud;

/// <summary>The mission timer's ring around the health (0x420cb0, 0x420d88): the time gone, as an
/// angle counter-clockwise from the right, stamps the empty panel's pixels (<c>SC_BSTAT</c>, its
/// black ring) over the panel's from the centre outwards. Each half of the panel is a fan of rows
/// from its centre row, the upper one for angles up to π, the lower mirrored.
/// <code>
///        π/2
///    ┌────┼────┐
///  π ┤    ●    ├ 0   time gone 0 → 2π: empties ↺ from the right
///    └────┼────┘
///        3π/2
/// </code></summary>
internal static class TimerRing
{
    /// <summary>The split at π (0x420d5a: 0x40490fb1 just below it for the upper half).</summary>
    private const float UpperEnd = 3.141582f;
    private const int Fixed = 16;
    private const float FixedOne = 1 << Fixed;
    // Slopes: near π/2 a row goes straight up; near 0 or π, a whole width per row.
    private const float NearUpLow = 1.570482f;
    private const float NearUpHigh = 1.571110f;
    private const float NearZero = 3.14159e-5f;
    private const float NearPi = 3.141561f;

    /// <summary>Stamps the ring of <paramref name="empty"/> into <paramref name="panel"/> (indices
    /// of the same size) from angle <paramref name="from"/> to <paramref name="to"/> (0x420cb0).</summary>
    public static void Wipe(byte[] panel, Texture empty, float from, float to)
    {
        from = Wrap(from);
        to = Wrap(to);
        if (from >= to)
        {
            return;
        }

        if (to <= MathF.PI || from >= MathF.PI)
        {
            Fan(panel, empty, to, from);
            return;
        }

        Fan(panel, empty, UpperEnd, from);
        Fan(panel, empty, to, MathF.PI);
    }

    /// <summary>An angle within 0-2π.</summary>
    private static float Wrap(float angle)
    {
        while (angle < 0f)
        {
            angle += MathF.Tau;
        }

        while (angle > MathF.Tau)
        {
            angle -= MathF.Tau;
        }

        return angle;
    }

    /// <summary>The rows between angles <paramref name="high"/> and <paramref name="low"/> of one half
    /// (0x420d88): every other row from the centre, x moving by the cotangent per row.</summary>
    private static void Fan(byte[] panel, Texture empty, float high, float low)
    {
        var width = empty.Width;
        var height = empty.Height;
        var row = -width;

        // The lower half: the angles mirrored, rows going down.
        if (high > MathF.PI || low > MathF.PI)
        {
            (high, low) = (MathF.Tau - low, MathF.Tau - high);
            row = width;
        }

        var leftStep = Slope(high, width);
        var rightStep = Slope(low, width);
        var limit = width << Fixed;
        var left = (width >> 1) << Fixed;
        var right = left;
        var at = (height >> 1) * width;
        for (var y = 1; y < height; y += 2)
        {
            left = Math.Max(left + leftStep, 0);
            right += rightStep;
            if (left >= limit || right < 0)
            {
                return;
            }

            right = Math.Min(right, limit - 1);
            for (var x = left >> Fixed; x <= right >> Fixed; x++)
            {
                var pixel = empty.Indices[at + x];
                if (pixel != 0)
                {
                    panel[at + x] = pixel;
                }
            }

            at += row;
        }
    }

    /// <summary>x moved per row, 16.16 (truncated like the original's FPU setting).</summary>
    private static int Slope(float angle, int width)
    {
        if (angle >= NearUpLow && angle <= NearUpHigh)
        {
            return 0;
        }

        if (angle < NearZero)
        {
            return width << Fixed;
        }

        if (angle > NearPi)
        {
            return -(width << Fixed);
        }

        return (int)(FixedOne / MathF.Tan(angle));
    }
}
