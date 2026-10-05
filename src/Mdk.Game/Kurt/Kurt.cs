using System.Numerics;
using Mdk.Engine.Platform;
using Mdk.Game.Audio;
using Mdk.Game.Collision;

namespace Mdk.Game.Kurt;

/// <summary>Kurt Hectic ("Damp" in the original code): walking, turning, jumping, falling and the
/// chute, through the arenas' BSPs. The original runs at 30 ticks per second; its constants are
/// converted to seconds (see godot-mdk docs/gameplay.md and docs/bsp.md).
/// <code>
///   input ──► speeds (vel_accel, vel_friction) ──► walk move (slide 0.75)
///                                              └─► fall move (slide 0.5) ──► on the floor?
///                                                                         └─► state ──► sprite
/// </code></summary>
public sealed class Kurt(ArenaSpace space, SoundMixer mixer, Func<Kurt.State, int> frameCount)
{
    public enum State { Still, Idle, Run, Side, Turn, Jump, RunJump, Fall, Chute, Land }

    public const float Ticks = 30f;

    // Forward, backward and strafe speeds (u/s) and accelerations (u/s²), normal and turbo.
    private const float MaxSpeed = 0.6667f * Ticks;
    private const float MaxSpeedTurbo = 1.3333f * Ticks;
    private const float Acceleration = 0.04444f * Ticks * Ticks;
    private const float AccelerationTurbo = 0.08889f * Ticks * Ticks;
    /// <summary>Deceleration without input (u/s²): above <see cref="MaxSpeed"/>, and below it.</summary>
    private const float FrictionFast = 0.17778f * Ticks * Ticks;
    private const float FrictionSlow = 0.08889f * Ticks * Ticks;
    /// <summary>Friction and strafe acceleration in the air.</summary>
    private const float AirControl = 0.75f;

    // Keyboard turning (degrees/s, degrees/s²), normal and turbo.
    private const float TurnSpeed = 4f * Ticks;
    private const float TurnSpeedTurbo = 6f * Ticks;
    private const float TurnAcceleration = 0.9f * Ticks * Ticks;
    private const float TurnAccelerationTurbo = 1.3f * Ticks * Ticks;
    private const float TurnFrictionFast = 1.6f * Ticks * Ticks;
    private const float TurnFrictionSlow = 0.55f * Ticks * Ticks;
    private const float MouseDegrees = 0.15f;

    // Vertical movement (u/s, u/s²).
    private const float Gravity = 64f;
    private const float MaxFallSpeed = 250f;
    private const float JumpSpeed = 40f;
    /// <summary>Releasing jump during the first ticks of a jump cuts it short.</summary>
    private const int JumpHoldTicks = 6;
    private const float JumpReleasePenalty = 3.333f;
    /// <summary>Falling faster than this starts the fall (or with jump held, opens the chute).</summary>
    private const float FallStartSpeed = -16f;
    private const float ChuteGravity = 21.33f;
    private const float ChuteFallSpeed = -8f;
    private const float ChuteBrake = 256f;

    // damp_collide_move's slide factors: walking stops within 30° of head-on; falling slides off 45°.
    private const float WalkSlide = 0.75f;
    private const float FallSlide = 0.5f;
    /// <summary>Landing slower than this keeps Kurt from creeping (0.35 u/tick).</summary>
    private const float CreepSpeed = 0.35f * Ticks;
    /// <summary>Downhill glue: on floors flatter than this the feet follow the slope down.</summary>
    private const float GlueNz = 0.25f;

    private const float IdleDelay = 6f;
    private const float MovingSpeed = 1f;
    /// <summary>The turning animation covers 45° of rotation.</summary>
    private const float TurnFrameDegrees = 45f;
    /// <summary>Footsteps on these run frames, alternating two pairs of sounds (FOOT3/4 first).</summary>
    private const int LeftStep = 0;
    private const int RightStep = 13;

    public Vector3 Feet;
    /// <summary>Degrees; 90 faces +Y (MDK).</summary>
    public float Yaw;
    public float ForwardSpeed;
    public float StrafeSpeed;
    public float TurnRate;
    public float VerticalSpeed;
    public bool OnFloor;
    public bool ChuteOpen;
    public State Current = State.Still;
    public float StateTime;
    /// <summary>Animation position in frames.</summary>
    public float AnimationFrame;

    private Vector3 _floorNormal = Vector3.UnitZ;
    private bool _footstepPair = true;
    private int _jumpTicksLeft;
    private bool _jumpReleased = true;

    public Vector3 Facing => new(MathF.Cos(float.DegreesToRadians(Yaw)), MathF.Sin(float.DegreesToRadians(Yaw)), 0f);

    public Vector3 Right => new(Facing.Y, -Facing.X, 0f);

    /// <summary>One step of <paramref name="delta"/> seconds.</summary>
    public void Update(Input input, float delta)
    {
        var turbo = input.IsDown(Key.Turbo);
        UpdateTurning(input, turbo, delta);

        var forward = Axis(input, Key.Forward, Key.Back);
        var strafe = Axis(input, Key.StrafeRight, Key.StrafeLeft);
        var air = OnFloor ? 1f : AirControl;
        ForwardSpeed = Accelerate(ForwardSpeed, forward, turbo, 1f, air, delta);
        StrafeSpeed = Accelerate(StrafeSpeed, strafe, turbo, air, air, delta);

        var move = (Facing * ForwardSpeed + Right * StrafeSpeed) * delta;
        if (move != Vector3.Zero)
        {
            Feet = space.Move(Feet, move, ArenaSpace.Motion.Walk, WalkSlide).Feet;
        }

        UpdateVertical(input, move, delta);
        UpdateState(forward, strafe, delta);
    }

    private static float Axis(Input input, Key positive, Key negative) =>
        (input.IsDown(positive) ? 1f : 0f) - (input.IsDown(negative) ? 1f : 0f);

    private void UpdateTurning(Input input, bool turbo, float delta)
    {
        var turn = Axis(input, Key.TurnLeft, Key.TurnRight);
        if (turn == 0f)
        {
            var friction = MathF.Abs(TurnRate) > TurnSpeed ? TurnFrictionFast : TurnFrictionSlow;
            TurnRate = MoveToward(TurnRate, 0f, friction * delta);
        }
        else
        {
            var max = turbo ? TurnSpeedTurbo : TurnSpeed;
            var acceleration = turbo ? TurnAccelerationTurbo : TurnAcceleration;
            TurnRate = Math.Clamp(TurnRate + turn * acceleration * delta, -max, max);
        }

        Yaw += TurnRate * delta - input.MouseX * MouseDegrees;
    }

    /// <summary>Towards <c>input × max speed</c>, or friction without input (vel_accel,
    /// vel_friction). The opposite direction reverses at once.</summary>
    private static float Accelerate(float speed, float input, bool turbo, float accelFactor, float frictionFactor, float delta)
    {
        if (input == 0f)
        {
            var friction = MathF.Abs(speed) > MaxSpeed ? FrictionFast : FrictionSlow;
            return MoveToward(speed, 0f, friction * frictionFactor * delta);
        }

        var acceleration = (turbo ? AccelerationTurbo : Acceleration) * accelFactor;
        var max = turbo ? MaxSpeedTurbo : MaxSpeed;
        if (speed * input < 0f)
        {
            speed = 0f;
        }

        return Math.Clamp(speed + input * acceleration * delta, -max, max);
    }

    private static float MoveToward(float from, float to, float step) =>
        MathF.Abs(to - from) <= step ? to : from + MathF.Sign(to - from) * step;

    /// <summary>Jumping, the chute, gravity and the fall move (damp_vertical 0x4694bc, damp_gravity 0x469efc).</summary>
    private void UpdateVertical(Input input, Vector3 move, float delta)
    {
        var jumpHeld = input.IsDown(Key.Jump);
        if (!jumpHeld)
        {
            _jumpReleased = true;
            if (_jumpTicksLeft > 0)
            {
                VerticalSpeed -= _jumpTicksLeft * JumpReleasePenalty;
                _jumpTicksLeft = 0;
            }
        }

        if (OnFloor)
        {
            ChuteOpen = false;
            if (jumpHeld && _jumpReleased)
            {
                VerticalSpeed = JumpSpeed;
                _jumpTicksLeft = JumpHoldTicks;
                _jumpReleased = false;
            }
            else
            {
                GlueDownhill(move, delta);
            }
        }
        else
        {
            UpdateAir(jumpHeld, delta);
        }

        Fall(delta);
    }

    /// <summary>In the air: the jump's hold time runs out; held jump opens the chute when falling
    /// fast, letting go closes it.</summary>
    private void UpdateAir(bool jumpHeld, float delta)
    {
        if (_jumpTicksLeft > 0)
        {
            _jumpTicksLeft = Math.Max(0, _jumpTicksLeft - (int)MathF.Ceiling(delta * Ticks));
        }

        if (!ChuteOpen && jumpHeld && VerticalSpeed < FallStartSpeed)
        {
            ChuteOpen = true;
            mixer.Play("CHUTEOUT");
        }
        else if (ChuteOpen && !jumpHeld)
        {
            ChuteOpen = false;
        }

        if (!ChuteOpen)
        {
            VerticalSpeed = MathF.Max(VerticalSpeed - Gravity * delta, -MaxFallSpeed);
            return;
        }

        VerticalSpeed -= ChuteGravity * delta;
        if (VerticalSpeed < ChuteFallSpeed)
        {
            VerticalSpeed = MathF.Min(VerticalSpeed + ChuteBrake * delta, ChuteFallSpeed);
        }
    }

    /// <summary>On a floor flatter than 75°, walking downhill keeps the feet on it.</summary>
    private void GlueDownhill(Vector3 move, float delta)
    {
        var n = _floorNormal.Z < 0f ? -_floorNormal : _floorNormal;
        var downhill = move.X * n.X + move.Y * n.Y;
        if (n.Z > GlueNz && downhill > 0f)
        {
            VerticalSpeed = MathF.Min(VerticalSpeed, -downhill / delta);
        }

        VerticalSpeed = MathF.Max(VerticalSpeed - Gravity * delta, -MaxFallSpeed);
    }

    /// <summary>The fall move: a hit going down lands, going up bumps the head.</summary>
    private void Fall(float delta)
    {
        var before = Feet;
        var result = space.Move(Feet, new Vector3(0f, 0f, VerticalSpeed * delta), ArenaSpace.Motion.Fall, FallSlide);
        Feet = result.Feet;
        if (!result.Hit)
        {
            // Sliding down a steep face: the real fall rate.
            if (Feet.Z < before.Z)
            {
                VerticalSpeed = (Feet.Z - before.Z) / delta;
            }

            OnFloor = false;
            return;
        }

        if (VerticalSpeed > 0f)
        {
            VerticalSpeed = 0f;
            OnFloor = false;
            return;
        }

        // No creeping on a landing.
        if (Vector3.DistanceSquared(Feet, before) < CreepSpeed * delta * (CreepSpeed * delta))
        {
            Feet = before;
        }

        VerticalSpeed = 0f;
        OnFloor = true;
        _floorNormal = result.Normal;
    }

    private void UpdateState(float forward, float strafe, float delta)
    {
        StateTime += delta;
        var done = AnimationFrame >= frameCount(Current) - 1;
        if (!OnFloor)
        {
            if (ChuteOpen)
            {
                SetState(State.Chute);
            }
            else if (VerticalSpeed > 0f && Current is not (State.Jump or State.RunJump or State.Fall))
            {
                SetState(MathF.Abs(ForwardSpeed) > MovingSpeed ? State.RunJump : State.Jump);
            }
            else if (VerticalSpeed < FallStartSpeed && (Current is not (State.Jump or State.RunJump) || done))
            {
                SetState(State.Fall);
            }
        }
        else if (Current is State.Jump or State.RunJump or State.Fall or State.Chute)
        {
            mixer.Play("LAND");
            SetState(State.Land);
        }
        else if (Current == State.Land && !done && forward == 0f && strafe == 0f)
        {
        }
        else if (forward != 0f || MathF.Abs(ForwardSpeed) > MovingSpeed)
        {
            SetState(State.Run);
        }
        else if (strafe != 0f || MathF.Abs(StrafeSpeed) > MovingSpeed)
        {
            SetState(State.Side);
        }
        else if (MathF.Abs(TurnRate) > MovingSpeed)
        {
            SetState(State.Turn);
        }
        else if (Current == State.Idle && !done)
        {
        }
        else if (Current == State.Still && StateTime > IdleDelay)
        {
            SetState(State.Idle);
        }
        else if (Current != State.Still)
        {
            SetState(State.Still);
        }

        Animate(delta);
    }

    private void Animate(float delta)
    {
        switch (Current)
        {
            case State.Run:
                var count = frameCount(State.Run);
                var previous = Wrap((int)MathF.Floor(AnimationFrame), count);
                AnimationFrame += RunRate() * Ticks * delta;
                var current = Wrap((int)MathF.Floor(AnimationFrame), count);
                Footstep(previous, current);
                break;
            case State.Turn:
                AnimationFrame = Yaw / TurnFrameDegrees * frameCount(State.Turn);
                break;
            default:
                AnimationFrame += Ticks * delta;
                break;
        }
    }

    private static int Wrap(int frame, int count) => ((frame % count) + count) % count;

    private void Footstep(int previous, int current)
    {
        if (current == previous)
        {
            return;
        }

        if (current == LeftStep)
        {
            mixer.Play(_footstepPair ? "FOOT3" : "FOOT1");
        }
        else if (current == RightStep)
        {
            mixer.Play(_footstepPair ? "FOOT4" : "FOOT2");
            _footstepPair = !_footstepPair;
        }
    }

    /// <summary>damp_run_anim_frame: the run animation follows the speed, backwards when backing up.</summary>
    private float RunRate()
    {
        var u = MathF.Abs(ForwardSpeed) / Ticks * 1.5f;
        var rate = u <= 1f ? 0.75f * u + 0.25f : 0.25f * u + 0.75f;
        return ForwardSpeed < 0f ? -rate : rate;
    }

    private void SetState(State state)
    {
        if (state == Current && StateTime > 0f)
        {
            return;
        }

        Current = state;
        StateTime = 0f;
        AnimationFrame = 0f;
    }
}
