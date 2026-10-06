using System.Numerics;
using Mdk.Game.Collision;

namespace Mdk.Game.Kurt;

/// <summary>Ledges (damp_ledge_grab 0x469868, kurt.gd _grab_ledge, _update_climb; godot-mdk
/// docs/gameplay.md "Vertical"): falling while running forward, Kurt grabs a flat top his hands
/// cross and climbs onto it.
/// <code>
///        hands' path (4.6 above the feet, 1-3 ahead)
///   ─ ─ ─ ─ ─ ─●─ ─ ─►   crosses the top ──► its edge towards Kurt, faced within 30°,
///          ┌────┴──────   room above it ──► HANG: 1 before the edge, 4.6 below it
///   Kurt ► │  wall          K_HANG, 2 ticks a frame, climbs by the tables (≈ 4.3 up, 1.35 on)
/// </code></summary>
public sealed partial class Kurt
{
    /// <summary>The hands' height above the feet (0x49798c), and how far ahead they reach.</summary>
    private const float LedgeHeight = 4.6041665f;
    private const int LedgeReach = 3;
    /// <summary>Kurt grabs while falling at least this fast (u/s).</summary>
    private const float LedgeSpeed = -0.25f;
    /// <summary>Only flat tops (|nz| ≥ 0.85), their edge faced within 30°.</summary>
    private const float LedgeFlat = 0.85f;
    private const float LedgeAngle = 30f;
    /// <summary>The yaw is the edge's direction − 90° (0x49799c).</summary>
    private const float EdgeToYaw = -90f;
    /// <summary>Room above the edge: a box of half size (0.5, 0.5, 2), 2.5 above it, swept from 1
    /// before it to 0.5 past it.</summary>
    private static readonly Vector3 LedgeRoom = new(0.5f, 0.5f, 2f);
    private const float LedgeRoomLift = 2.5f;
    private const float LedgeRoomPast = 0.5f;
    /// <summary>Kurt hangs this far before the edge.</summary>
    private const float HangBack = 1f;

    /// <summary>Climbing (K_HANG, 2 ticks a frame): the height and the backward offset of each frame
    /// pair (0x491f34, 0x491f74), scaled by 0.708333 × 0.5 per tick.</summary>
    private static readonly float[] ClimbHeights =
        [0.374f, 0.326f, 0.292f, 0.311f, 0.677f, 1.263f, 2.754f, 4.172f, 4.903f, 5.343f, 5.711f, 6.001f, 6.212f, 6.345f, 6.406f, 6.417f];
    private static readonly float[] ClimbBack =
        [0.685f, 0.383f, 0.167f, 0.254f, 0.635f, 1.053f, 0.847f, 0.514f, 0.17f, -0.125f, -0.501f, -0.825f, -1.066f, -1.221f, -1.293f, -1.305f];
    private const float ClimbScale = 0.708333f * 0.5f;
    private const int ClimbSteps = 15;
    private const int TicksPerClimbFrame = 2;

    private int _climbTicks;

    /// <summary>Grabs a ledge after a step that started at <paramref name="before"/> with the forward
    /// key <paramref name="forward"/>: the hands' path crosses a flat top, Kurt faces its edge and
    /// there's room above it. Returns whether he hangs.</summary>
    private bool GrabLedge(Vector3 before, float forward)
    {
        if (forward <= 0f || VerticalSpeed > LedgeSpeed || Health == 0 || Current is State.Dead or State.Knocked or State.GetUp)
        {
            return false;
        }

        var facing = Facing;
        var hands = new Vector3(0f, 0f, LedgeHeight);
        for (var i = 1; i <= LedgeReach; i++)
        {
            var ahead = facing * i;
            if (space.FloorCrossing(before + hands + ahead, Feet + hands + ahead) is not { } top)
            {
                continue;
            }

            return MathF.Abs(top.Normal.Z) >= LedgeFlat && HangFrom(top, ahead);
        }

        return false;
    }

    /// <summary>Hangs from the edge of <paramref name="top"/> towards Kurt (<paramref name="ahead"/> back).</summary>
    private bool HangFrom(ArenaSpace.Surface top, Vector3 ahead)
    {
        if (ArenaSpace.Edge(top, top.Point - ahead) is not var (edge, direction))
        {
            return false;
        }

        var yaw = float.RadiansToDegrees(MathF.Atan2(direction.Y, direction.X)) + EdgeToYaw;
        var turn = MathF.Abs(Wrap180(yaw - Yaw));
        var facing = Facing;
        var room = edge + new Vector3(0f, 0f, LedgeRoomLift);
        if (turn > LedgeAngle || !space.Free(room - facing * HangBack, room + facing * LedgeRoomPast, LedgeRoom))
        {
            return false;
        }

        Yaw = yaw;
        Feet = edge - facing * HangBack - new Vector3(0f, 0f, LedgeHeight);
        ForwardSpeed = 0f;
        StrafeSpeed = 0f;
        VerticalSpeed = 0f;
        OnFloor = false;
        StopFiring();
        mixer.Stop(ChuteOnSound);
        ChuteOpen = false;
        _climbTicks = 0;
        SetState(State.Hang);
        return true;
    }

    /// <summary>Climbing (state 800): K_HANG at 2 ticks a frame, moved by the tables, no gravity.</summary>
    private void UpdateClimb(float delta)
    {
        StateTime += delta;
        var ticks = (int)(StateTime * Ticks) - _climbTicks;
        var facing = Facing;
        for (var i = 0; i < ticks; i++)
        {
            _climbTicks++;
            var k = (_climbTicks + 1) >> 1;
            if (k < ClimbSteps)
            {
                Feet += new Vector3(0f, 0f, (ClimbHeights[k] - ClimbHeights[k - 1]) * ClimbScale);
                Feet -= facing * ((ClimbBack[k] - ClimbBack[k - 1]) * ClimbScale);
            }
        }

        VerticalSpeed = 0f;
        var frames = frameCount(State.Hang);
        if (_climbTicks >= frames * TicksPerClimbFrame - 1)
        {
            SetState(State.Still);
            return;
        }

        AnimationFrame = MathF.Min(_climbTicks / TicksPerClimbFrame, frames - 1);
    }

    private static float Wrap180(float degrees) => degrees - 360f * MathF.Floor((degrees + 180f) / 360f);
}
