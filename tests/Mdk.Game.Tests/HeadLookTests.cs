using System.Numerics;
using Mdk.Engine.Platform;

namespace Mdk.Game.Tests;

/// <summary>The gyroscope's rates (the phone's portrait axes: x right, y up, z out of the screen)
/// become the look's turn in a landscape phone: the head turning right looks right, nodding up
/// looks up, whichever way up the phone is.</summary>
public class HeadLookTests
{
    private const float Tolerance = 1e-4f;
    private const float Rate = 1f;
    private const float Seconds = 0.5f;
    private static readonly float Degrees = float.RadiansToDegrees(Rate * Seconds);

    /// <summary>The world's up and right axes in the phone's axes: landscape is its right side up
    /// (x up, y to the left), flipped its left side up.</summary>
    private static (Vector3 Up, Vector3 Right) Axes(ScreenTurn turn) => turn == ScreenTurn.Landscape
        ? (Vector3.UnitX, -Vector3.UnitY)
        : (-Vector3.UnitX, Vector3.UnitY);

    [Theory]
    [InlineData(ScreenTurn.Landscape)]
    [InlineData(ScreenTurn.LandscapeFlipped)]
    public void TurningTheHeadRightLooksRight(ScreenTurn turn)
    {
        // Turning right is a clockwise turn seen from above: negative about up.
        var look = HeadLook.FromGyro(-Axes(turn).Up * Rate, Seconds, turn);

        Assert.Equal(Degrees, look.X, Tolerance);
        Assert.Equal(0f, look.Y, Tolerance);
    }

    [Theory]
    [InlineData(ScreenTurn.Landscape)]
    [InlineData(ScreenTurn.LandscapeFlipped)]
    public void NoddingUpLooksUp(ScreenTurn turn)
    {
        // Nodding up turns the view about the right axis; the look's Y is down, as the mouse's.
        var look = HeadLook.FromGyro(Axes(turn).Right * Rate, Seconds, turn);

        Assert.Equal(0f, look.X, Tolerance);
        Assert.Equal(-Degrees, look.Y, Tolerance);
    }

    [Fact]
    public void TiltingSidewaysDoesNotLook()
    {
        var look = HeadLook.FromGyro(Vector3.UnitZ * Rate, Seconds, ScreenTurn.Landscape);

        Assert.Equal(Vector2.Zero, look);
    }

    [Fact]
    public void ANoiseLevelRateDoesNotLook()
    {
        // A still phone's gyroscope reads a little: the view must not creep.
        var look = HeadLook.FromGyro(new Vector3(0.005f, -0.005f, 0f), Seconds, ScreenTurn.Landscape);

        Assert.Equal(Vector2.Zero, look);
    }
}
