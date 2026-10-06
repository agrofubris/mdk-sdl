using System.Numerics;
using Mdk.Engine.Platform;
using Mdk.Game.Collision;

namespace Mdk.Game.Kurt;

/// <summary>Sniper mode (kurt.gd _enter_sniper, leave_sniper, _update_sniper; godot-mdk
/// docs/gameplay.md "Sniper mode"): Kurt stands, sidesteps and looks through the scope; the
/// rounds are the scripts runtime's (<see cref="SniperFire"/>).
/// <code>
///   sniper key ──on the floor──► SNIPERON, BREATH ──► look, zoom, sidestep, clip, fire
///        ▲                                                │
///        └── sniper key (SNIPEROFF), falling, knock-down, death, cutscenes (StopFiring)
/// </code></summary>
public sealed partial class Kurt
{
    /// <summary>Whether leaving sniper mode plays SNIPEROFF.</summary>
    public enum SniperExit { Silent, WithSound }

    /// <summary>Sidestepping only, at a quarter of the running speed.</summary>
    private const float SniperStrafe = 0.25f;
    /// <summary>Falling this fast (or rising) ends sniper mode.</summary>
    private const float SniperFallSpeed = -30f;
    private const float SniperRiseSpeed = 1f;

    /// <summary>Sniper mode is on (0x573a60); Kurt isn't drawn.</summary>
    public bool Sniping { get; private set; }
    public Scope Scope { get; } = new();
    /// <summary>Fires a round of an ammo type from the scope; false when no round slot is free.</summary>
    public Func<int, bool>? SniperFire;

    private int _breathVoice;
    private int _zoomVoice;

    /// <summary>The eye in sniper mode.</summary>
    public Vector3 SniperEye => Scope.Eye(Feet, Facing);

    /// <summary>The line of sight in sniper mode.</summary>
    public Vector3 SniperForward => Scope.Forward(Yaw);

    /// <summary>Enters sniper mode (damp_move 0x46883c): only standing on a floor, not knocked down,
    /// sliding or throwing. Kurt stops firing and shows SNIPERON (state 803).</summary>
    public void EnterSniper()
    {
        if (!CanSnipe)
        {
            return;
        }

        StopFiring();
        Sniping = true;
        ForwardSpeed = 0f;
        StrafeSpeed = 0f;
        TurnRate = 0f;
        Scope.Reset();
        SetState(State.Still);
        mixer.Play("SNIPERON");
        _breathVoice = mixer.PlayLooped("BREATH");
    }

    private bool CanSnipe => OnFloor && !Sliding && Health != 0 && !Knocked && Current is not (State.Dead or State.Throw);

    /// <summary>Leaves sniper mode (0x4645c8).</summary>
    public void LeaveSniper(SniperExit exit = SniperExit.Silent)
    {
        if (!Sniping)
        {
            return;
        }

        Sniping = false;
        StrafeSpeed = 0f;
        mixer.StopVoice(_breathVoice);
        mixer.StopVoice(_zoomVoice);
        _breathVoice = 0;
        _zoomVoice = 0;
        if (exit == SniperExit.WithSound)
        {
            mixer.Play("SNIPEROFF");
        }
    }

    /// <summary>The sniper key, then a sniper step. Returns whether the step was a sniper one.</summary>
    private bool UpdateSniper(Input input, bool turbo, float delta)
    {
        if (Pressed(input, Key.Sniper))
        {
            if (Sniping)
            {
                LeaveSniper(SniperExit.WithSound);
                if (!BetaMoves || frameCount(State.HelmetOff) == 0)
                {
                    return false;
                }

                // The demo's helmet comes off (state 900).
                SetState(State.HelmetOff);
                return true;
            }

            if (BetaMoves && CanSnipe && frameCount(State.HelmetOn) != 0)
            {
                PutHelmetOn();
                return true;
            }

            EnterSniper();
        }

        if (Sniping && (Health == 0 || (!OnFloor && (VerticalSpeed < SniperFallSpeed || VerticalSpeed > SniperRiseSpeed))))
        {
            LeaveSniper();
        }

        if (!Sniping)
        {
            return false;
        }

        SniperControls(input, turbo, delta);
        SniperMove(input, turbo, delta);
        var clip = Scope.UpdateClip(Inventory, input.IsDown(Key.Fire) ? Scope.Trigger.Held : Scope.Trigger.Released,
            SniperFire ?? (_ => true), delta);
        if ((clip & Scope.Clip.Loaded) != 0)
        {
            mixer.Play("SNIPRELD");
        }

        if ((clip & Scope.Clip.Fired) != 0)
        {
            mixer.Play("SNIPERSHOT");
        }

        StateTime += delta;
        return true;
    }

    /// <summary>The turn and forward/back keys (or the mouse) turn and tilt the view; the zoom keys
    /// (or the wheel) zoom; the item keys pick the ammo (0x467384).</summary>
    private void SniperControls(Input input, bool turbo, float delta)
    {
        var turn = Axis(input, Key.TurnRight, Key.TurnLeft);
        var tilt = Axis(input, Key.Back, Key.Forward);
        var mouse = new Vector2(input.MouseX, input.MouseY) * MouseDegrees;
        Yaw += Scope.Look(turn, tilt, mouse, turbo ? Scope.Turbo.On : Scope.Turbo.Off, delta);

        Scope.Wheel(input.Wheel);
        var zooming = Scope.UpdateZoom(Axis(input, Key.ZoomOut, Key.ZoomIn), delta);
        if (zooming == Scope.Zooming.Moving && !mixer.IsVoicePlaying(_zoomVoice))
        {
            _zoomVoice = mixer.PlayLooped("ZOOM");
        }
        else if (zooming == Scope.Zooming.Still && _zoomVoice != 0)
        {
            mixer.StopVoice(_zoomVoice);
            _zoomVoice = 0;
        }

        if (Pressed(input, Key.ItemNext))
        {
            Scope.SelectAmmo(Inventory, 1);
        }

        if (Pressed(input, Key.ItemPrevious))
        {
            Scope.SelectAmmo(Inventory, -1);
        }
    }

    /// <summary>Kurt sidesteps at a quarter of the running speed and still falls.</summary>
    private void SniperMove(Input input, bool turbo, float delta)
    {
        var max = (turbo ? MaxSpeedTurbo : MaxSpeed) * SniperStrafe;
        ForwardSpeed = 0f;
        StrafeSpeed = Math.Clamp(Accelerate(StrafeSpeed, Axis(input, Key.StrafeRight, Key.StrafeLeft), turbo, 1f, 1f, delta), -max, max);
        var move = (Right * StrafeSpeed + new Vector3(_push, 0f)) * delta;
        DrainPush(delta);
        if (move != Vector3.Zero)
        {
            Feet = space.Move(Feet, move, ArenaSpace.Motion.Walk, WalkSlide, Nearby(Feet + move)).Feet;
        }

        VerticalSpeed = MathF.Max(VerticalSpeed - Gravity * delta, -MaxFallSpeed);
        Fall(delta);
    }
}
