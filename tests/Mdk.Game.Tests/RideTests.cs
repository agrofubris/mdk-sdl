using System.Numerics;
using Mdk.Game.Scripts;

namespace Mdk.Game.Tests;

/// <summary>The rides' physics (snowboard.gd, bomber.gd; godot-mdk docs/gameplay.md "The snowboard",
/// "The XE bomber") and the end of a level's timing (end_level.gd).</summary>
public class RideTests
{
    private const float Tick = 1f;

    [Fact]
    public void SteeringRampsUpOnTheGroundAndSnapsBack()
    {
        // Right turns lower S: 4°/tick × (ticks × 2 / 30) during the first 15 ticks on the ground.
        var turnTicks = 0f;
        var steer = Snowboard.Steer(0f, 1f, true, ref turnTicks, Tick);
        Assert.Equal(-4f * 2f / 30f, steer, 4);

        // Full rate at once in the air, clamped to ±30.
        turnTicks = 0f;
        steer = 0f;
        for (var i = 0; i < 20; i++)
        {
            steer = Snowboard.Steer(steer, 1f, false, ref turnTicks, Tick);
        }

        Assert.Equal(-30f, steer);

        // The other way snaps it straight; no key brings it back by 3°/tick.
        Assert.Equal(0f, Snowboard.Steer(-30f, -1f, true, ref turnTicks, Tick));
        Assert.Equal(-27f, Snowboard.Steer(-30f, 0f, true, ref turnTicks, Tick));
    }

    [Theory]
    [InlineData(1f, 1f, 1.05f)]
    [InlineData(1f, 2.49f, 2.5f)]
    [InlineData(-1f, 2f, 1.95f)]
    [InlineData(-1f, 1.16667f, 1.16667f)]
    [InlineData(0f, 1f, 1.05f)]
    [InlineData(0f, 2f, 1.9972222f)]
    public void SpeedFollowsTheKeys(float forward, float speed, float expected)
    {
        Assert.Equal(expected, Snowboard.Accelerate(speed, forward, Tick), 4);
    }

    [Fact]
    public void SlopesSpeedUpAndSlowDown()
    {
        // Nose down 30° (pitch 330): +0.0333 a tick, up to 2.6667.
        Assert.Equal(1.5f + 0.5f * 0.066667f, Snowboard.SlopeSpeed(1.5f, 330f, Tick), 4);
        Assert.Equal(2.66667f, Snowboard.SlopeSpeed(2.66f, 330f, Tick), 4);
        // Nose up: down to 1.1667.
        Assert.Equal(1.16667f, Snowboard.SlopeSpeed(1.17f, 30f, Tick), 4);
    }

    [Fact]
    public void JumpPivotDipsThenComesBack()
    {
        Assert.Equal(4.5f - 3 * 0.2f, Snowboard.JumpPivot(3.5f, 4.5f, 0f), 4);
        Assert.Equal(4.0f, Snowboard.JumpPivot(6f, 3.5f, 0.5f), 4);
        Assert.Equal(4.5f, Snowboard.JumpPivot(6f, 4.4f, 1f), 4);
    }

    [Fact]
    public void CursorAcceleratesBrakesAndReverses()
    {
        // 1/3 px/tick² up to 10 px/tick.
        var speed = 0f;
        for (var i = 0; i < 40; i++)
        {
            speed = Bomber.CursorStep(speed, 1f, Tick);
        }

        Assert.Equal(10f, speed, 4);
        // The other way starts over; no key brakes by 2/3.
        Assert.Equal(-1f / 3f, Bomber.CursorStep(10f, -1f, Tick), 4);
        Assert.Equal(10f - 2f / 3f, Bomber.CursorStep(10f, 0f, Tick), 4);
    }

    [Fact]
    public void AimLooksDownThroughTheCursor()
    {
        // The centre looks straight down; above the centre is the heading (yaw 90: +Y).
        Assert.Equal(new Vector3(0f, 0f, -250f), Bomber.AimDirection(Bomber.CursorCentre, 90f));
        var ahead = Bomber.AimDirection(Bomber.CursorCentre - new Vector2(0f, 100f), 90f);
        Assert.Equal(100f, ahead.Y, 3);
        Assert.Equal(0f, ahead.X, 3);
        var right = Bomber.AimDirection(Bomber.CursorCentre + new Vector2(100f, 0f), 90f);
        Assert.Equal(100f, right.X, 3);
    }

    [Fact]
    public void BombFallsOntoTheTarget()
    {
        // 64 units under 32 u/s²: 2 s; nothing below: 2.5 s.
        Assert.Equal(2f, Bomber.FallTime(64f), 4);
        Assert.Equal(2.5f, Bomber.FallTime(-5f));
    }

    [Fact]
    public void KurtRisesFasterAfterThreeUnitsATick()
    {
        // 0.025 per tick²: past 3 on the 121st tick, then 0.075 per tick².
        var speed = 0f;
        var ticks = 0;
        while (speed <= 3f)
        {
            speed = EndLevel.RiseSpeed(speed, Tick);
            ticks++;
        }

        // 121 ticks in exact arithmetic; float sums may cross a tick early.
        Assert.InRange(ticks, 120, 121);
        Assert.Equal(speed + 0.075f, EndLevel.RiseSpeed(speed, Tick), 4);
    }
}
