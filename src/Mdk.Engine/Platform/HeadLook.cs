using System.Numerics;

namespace Mdk.Engine.Platform;

/// <summary>Which way up a landscape phone is: its right side up (<see cref="Landscape"/>) or its
/// left side up.</summary>
public enum ScreenTurn { Landscape, LandscapeFlipped }

/// <summary>The head's turn from a phone's gyroscope (in a VR viewer): its rates about the phone's
/// portrait axes become the look's degrees, as a mouse's would turn the view.
/// <code>
///   landscape (right side up):        flipped (left side up):
///        x ↑  (the world's up)             x ↓
///   y ←──┘    (the world's left)           └──→ y
/// </code></summary>
public static class HeadLook
{
    /// <summary>Slower than this (radians a second) is the gyroscope's noise on a still phone.</summary>
    private const float Noise = 0.02f;

    /// <summary>The look's turn (degrees: x right, y down, as the mouse's) over <paramref name="seconds"/>
    /// at <paramref name="rate"/> (radians a second about the phone's x, y and z). Tilting sideways
    /// (about z) turns nothing: the game's views don't roll.</summary>
    public static Vector2 FromGyro(Vector3 rate, float seconds, ScreenTurn turn)
    {
        // About up (positive: to the left) and about right (positive: nodding up).
        var sign = turn == ScreenTurn.Landscape ? 1f : -1f;
        var aboutUp = sign * rate.X;
        var aboutRight = -sign * rate.Y;

        var right = MathF.Abs(aboutUp) < Noise ? 0f : -aboutUp;
        var down = MathF.Abs(aboutRight) < Noise ? 0f : -aboutRight;
        return new Vector2(right, down) * float.RadiansToDegrees(seconds);
    }
}
