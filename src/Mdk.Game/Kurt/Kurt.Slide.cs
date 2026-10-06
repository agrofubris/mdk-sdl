using System.Numerics;
using Mdk.Engine.Platform;
using Mdk.Game.Collision;

namespace Mdk.Game.Kurt;

/// <summary>Sliding on his back down level 6's wind tunnels (damp_buttslide 0x468db8, kurt.gd
/// start_slide, _update_slide; godot-mdk docs/gameplay.md "Sliding"). The wind zones (opcode 224)
/// start it and push him.
/// <code>
///   wind zone ──► SLIP (K_SLIP) ──► SLIDE │ SLIDE_FAST (forward) │ SLIDE_BRAKE (back)
///   velocity += slope² + wind² (slide_accel), heading = velocity's, speed ≤ cap (15-80)
///   ends: 20 ticks in the air ──► FALL │ at rest on the floor ──► gets up (K_BFLIP)
/// </code></summary>
public sealed partial class Kurt
{
    /// <summary>Extra gravity of the wind zones and the slide (u/s²; damp_gravity's adds to it).</summary>
    public const float SlideGravity = 128f;

    /// <summary>The slope pushes by 10 × the smoothed floor normal; pushes within ±0.1 brake by 2 u/s².</summary>
    private const float SlideSlope = 10f;
    private const float SlidePushDead = 0.1f;
    private const float SlideFriction = 2f;
    /// <summary>The floor normal is smoothed by 0.8·old + 0.2·new horizontally, half and half in z.</summary>
    private const float SlideNormalKeep = 0.8f;
    private const float SlideNormalKeepZ = 0.5f;
    /// <summary>Speed cap: 50, raised by 10/s up to 80 going forward, lowered by 25/s down to 15
    /// braking, back towards 50 by 20/s.</summary>
    private const float SlideCap = 50f;
    private const float SlideCapMin = 15f;
    private const float SlideCapMax = 80f;
    private const float SlideCapRise = 10f;
    private const float SlideCapFall = 25f;
    private const float SlideCapRelax = 20f;
    /// <summary>Forward accelerates by 35 u/s² (above 15 u/s), back brakes by 15 u/s² (down to
    /// 15 u/s), turning turns by 45°/s.</summary>
    private const float SlideAcceleration = 35f;
    private const float SlideBrake = 15f;
    private const float SlideMinSpeed = 15f;
    private const float SlideTurn = 45f;
    /// <summary>Kurt faces the slide once |vx| + |vy| is above this.</summary>
    private const float SlideHeading = 0.5f;
    /// <summary>A wall stops him when he moved less than half of a move over 0.5 units on an axis:
    /// the slide follows what he moved, its speed averaged with it.</summary>
    private const float SlideWallMove = 0.5f;
    private const float SlideWallShare = 0.5f;
    private const float SlideAirTicks = 20f;
    /// <summary>At rest: no speed and no push left (within this).</summary>
    private const float SlideRest = 1e-5f;
    /// <summary>BUTSLIDE plays at 11025 Hz, at 15000 Hz going forward; BUTBRAKE while braking.</summary>
    private const float SlideFastPitch = 15000f / 11025f;
    private const string SlideSound = "BUTSLIDE";
    private const string BrakeSound = "BUTBRAKE";

    /// <summary>Sliding (0x573be8).</summary>
    public bool Sliding { get; private set; }

    private Vector2 _slideVelocity;
    private float _slideCap = SlideCap;
    private Vector3 _slideNormal = Vector3.UnitZ;
    private Vector2 _slidePush;
    private float _slideAir;
    private int _slideVoice;
    private string _slideSound = "";

    /// <summary>The slide's velocity (u/s; 0x573bf0).</summary>
    public Vector2 SlideVelocity => _slideVelocity;

    /// <summary>Starts the slide (0x468b64): the wind zones call this while Kurt is in their box.</summary>
    public void StartSlide()
    {
        if (Sliding || Health == 0)
        {
            return;
        }

        Sliding = true;
        _slideVelocity = Vector2.Zero;
        _slideCap = SlideCap;
        _slideNormal = Vector3.UnitZ;
        _slidePush = Vector2.Zero;
        _slideAir = 0f;
        StopFiring();
        ChuteOpen = false;
        SetState(State.Slip);
    }

    public void StopSlide()
    {
        Sliding = false;
        _slideVelocity = Vector2.Zero;
        mixer.StopVoice(_slideVoice);
        _slideVoice = 0;
        _slideSound = "";
    }

    /// <summary>Adds to the slide velocity (slide_accel 0x468be0): a push above 0.1 accelerates by its
    /// square, below −0.1 it slows the same way, in between the velocity brakes towards 0.</summary>
    public void SlideAccel(Vector2 push, float dt)
    {
        _slideVelocity = new Vector2(Accel(_slideVelocity.X, push.X, dt), Accel(_slideVelocity.Y, push.Y, dt));
    }

    private static float Accel(float speed, float push, float dt)
    {
        if (push > SlidePushDead)
        {
            return speed + push * push * dt;
        }

        if (push < -SlidePushDead)
        {
            return speed - push * push * dt;
        }

        return MoveToward(speed, 0f, SlideFriction * dt);
    }

    /// <summary>One step of the slide. Returns whether it took the step (false: it ended, walk on).</summary>
    private bool UpdateSlide(Input input, float delta)
    {
        if (!Sliding)
        {
            return false;
        }

        if (Health == 0)
        {
            StopSlide();
            return false;
        }

        if (OnFloor)
        {
            _slideNormal = new Vector3(
                SlideNormalKeep * _slideNormal.X + (1f - SlideNormalKeep) * _floorNormal.X,
                SlideNormalKeep * _slideNormal.Y + (1f - SlideNormalKeep) * _floorNormal.Y,
                SlideNormalKeepZ * _slideNormal.Z + (1f - SlideNormalKeepZ) * _floorNormal.Z);
            _slidePush = new Vector2(_slideNormal.X, _slideNormal.Y) * SlideSlope;
            _slideAir = 0f;
            if (_slidePush.LengthSquared() < SlideRest && _slideVelocity == Vector2.Zero)
            {
                // At rest he gets up (state 901 from K_BFLIP).
                StopSlide();
                SetState(State.Knocked);
                AnimationFrame = frameCount(State.Knocked);
                return true;
            }
        }
        else
        {
            _slideAir += Ticks * delta;
            if (_slideAir > SlideAirTicks)
            {
                StopSlide();
                SetState(State.Fall);
                return false;
            }
        }

        SlideAccel(_slidePush, delta);
        CameraRoll.Slide(_slideNormal, new Vector2(Facing.X, Facing.Y), delta);
        var forward = Axis(input, Key.Forward, Key.Back);
        var speed = SlideSpeed(forward, delta);
        Steer(input, speed, delta);
        SlideMove(delta);
        VerticalSpeed -= SlideGravity * delta;
        VerticalSpeed = MathF.Max(VerticalSpeed - Gravity * delta, -MaxFallSpeed);
        Fall(delta);
        UpdateTouched();
        UpdateSlideState(forward, delta);
        UpdateMuzzle(delta);
        return true;
    }

    /// <summary>The speed within the cap; forward speeds up and raises the cap, back brakes and
    /// lowers it, without input it goes back to 50.</summary>
    private float SlideSpeed(float forward, float delta)
    {
        var speed = MathF.Min(_slideVelocity.Length(), _slideCap);
        if (forward > 0f)
        {
            if (speed >= SlideMinSpeed)
            {
                speed += SlideAcceleration * forward * delta;
            }

            _slideCap = MathF.Min(_slideCap + SlideCapRise * delta, SlideCapMax);
        }
        else if (forward < 0f)
        {
            if (speed > SlideMinSpeed)
            {
                speed = MathF.Max(speed + SlideBrake * forward * delta, SlideMinSpeed);
            }

            _slideCap = MathF.Max(_slideCap - SlideCapFall * delta, SlideCapMin);
        }
        else
        {
            _slideCap = MoveToward(_slideCap, SlideCap, SlideCapRelax * delta);
        }

        return MathF.Min(speed, _slideCap);
    }

    /// <summary>Kurt faces where the slide goes; turning turns it.</summary>
    private void Steer(Input input, float speed, float delta)
    {
        if (MathF.Abs(_slideVelocity.X) + MathF.Abs(_slideVelocity.Y) > SlideHeading)
        {
            Yaw = float.RadiansToDegrees(MathF.Atan2(_slideVelocity.Y, _slideVelocity.X));
        }

        Yaw += Axis(input, Key.TurnLeft, Key.TurnRight) * SlideTurn * delta;
        _slideVelocity = new Vector2(Facing.X, Facing.Y) * speed;
    }

    /// <summary>The slide's move; a wall that stops it turns it along what Kurt moved.</summary>
    private void SlideMove(float delta)
    {
        var planned = new Vector3(_slideVelocity * delta, 0f);
        if (planned == Vector3.Zero)
        {
            return;
        }

        var start = Feet;
        Feet = Contact(space.Move(Feet, planned, ArenaSpace.Motion.Walk, WalkSlide, Nearby(Feet + planned))).Feet;
        var moved = Feet - start;
        if (!Stopped(planned.X, moved.X) && !Stopped(planned.Y, moved.Y))
        {
            return;
        }

        var actual = new Vector2(moved.X, moved.Y);
        var direction = actual == Vector2.Zero ? Vector2.Normalize(_slideVelocity) : Vector2.Normalize(actual);
        var speed = _slideVelocity.Length() * SlideWallShare + actual.Length() / delta * (1f - SlideWallShare);
        _slideVelocity = direction * speed;
    }

    private static bool Stopped(float planned, float moved) =>
        (planned > SlideWallMove && moved < planned * SlideWallShare) || (planned < -SlideWallMove && moved > planned * SlideWallShare);

    /// <summary>K_SLIP once, then K_SLIDE, K_FSLIDE or K_BSLIDE by the forward key; their sounds.</summary>
    private void UpdateSlideState(float forward, float delta)
    {
        StateTime += delta;
        AnimationFrame += Ticks * delta;
        if (Current != State.Slip || AnimationFrame >= frameCount(State.Slip))
        {
            var wanted = forward > 0f ? State.SlideFast : forward < 0f ? State.SlideBrake : State.Slide;
            if (wanted != Current)
            {
                SetState(wanted);
            }
        }

        var sound = Current == State.SlideBrake ? BrakeSound : SlideSound;
        if (sound != _slideSound)
        {
            mixer.StopVoice(_slideVoice);
            _slideVoice = mixer.PlayLooped(sound);
            _slideSound = sound;
        }

        mixer.SetPitch(_slideVoice, Current == State.SlideFast ? SlideFastPitch : 1f);
    }
}
