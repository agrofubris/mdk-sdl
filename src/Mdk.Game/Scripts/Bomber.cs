using System.Numerics;
using Mdk.Engine.Platform;
using Mdk.Formats;
using Mdk.Game.Objects;

namespace Mdk.Game.Scripts;

/// <summary>The <c>XE</c> bomber ride of level 7's <c>DANT_5</c> (0x46bf40; bomber.gd, godot-mdk
/// docs/gameplay.md "The XE bomber"): Kurt sits in the flyer while its script flies it along a path;
/// the view looks straight down from it and a cursor aims <c>XBN_BOMB</c>s at the ground.
/// <code>
///   mount ──► view sinks 50 → 0 u above Kurt in 2 s (then the XE isn't drawn)
///         ──► script unlocks (flag 0x4000000 cleared) ──► cursor, bombs, HUD
///         ──► script clears the rideable flag ──► Kurt drops from the XE (Rides)
/// </code></summary>
public sealed class Bomber
{
    /// <summary>The view (0x4183f0): 50 units above Kurt at the mount, sinking 25 units/s.</summary>
    private const float CameraHeight = 50f;
    private const float CameraDescent = 25f;
    /// <summary>The cursor (0x573b0c): starts at the centre, kept in a box; keys accelerate it by 1/3
    /// px/tick² up to 10 px/tick, it brakes by 2/3 px/tick²; the mouse moves it by 1/3 of its motion.</summary>
    public static readonly Vector2 CursorCentre = new(300f, 180f);
    private static readonly Vector2 CursorMin = new(128f, 64f);
    private static readonly Vector2 CursorMax = new(472f, 296f);
    private const float CursorAccel = 1f / 3f;
    private const float CursorSpeed = 10f;
    private const float CursorBrake = 2f / 3f;
    private const float MouseScale = 1f / 3f;
    /// <summary>Bombs (0x573c64): 10, one back per second.</summary>
    private const int Bombs = 10;
    private const float RefillTime = 1f;
    /// <summary>The aim: a ray from Kurt through the cursor with the view's focal length (600 / zoom
    /// 2.4), as long as 1000 times that direction; the bomb starts 5 below Kurt and falls by 32 u/s²
    /// (the XE's gravity) onto the hit point, or for 2.5 s when nothing is hit.</summary>
    private const float Focal = 250f;
    private const float RayLength = 1000f;
    private const float DropBelow = 5f;
    private const float FallGravity = 32f;
    private const float MissTime = 2.5f;
    /// <summary>The bomb: a thrown item of kind 0x81 (0x43deac) living 900 ticks, flags 0x818a6.</summary>
    private const string Bomb = "XBN_BOMB";
    private const int BombTicks = 900;
    private const int BombFlags = 0x818a6;
    private const string DropSound = "DROP";
    /// <summary>The XE's health is kept at 10000; what it loses goes to Kurt (0x46a77c).</summary>
    public const int Health = 10000;
    /// <summary>Objects the aim passes through (not solid, not a target).</summary>
    private const int Unaimed = MdkObject.FlagNotSolid | MdkObject.FlagNotTarget;

    private readonly ScriptRuntime _runtime;
    private readonly MdkObject _xe;
    private Vector2 _speed;
    private float _refill = RefillTime;
    private bool _fireHeld;

    public Bomber(ScriptRuntime runtime, MdkObject xe)
    {
        _runtime = runtime;
        _xe = xe;
    }

    /// <summary>Height of the view above Kurt.</summary>
    public float ViewHeight { get; private set; } = CameraHeight;
    public Vector2 Cursor { get; private set; } = CursorCentre;
    public int BombsLeft { get; private set; } = Bombs;

    /// <summary>Whether the script still holds the controls (and the HUD is off).</summary>
    public bool Locked => (_xe.Flags & Rides.FlagLocked) != 0;

    /// <summary>Whether the view is inside the XE, which then isn't drawn.</summary>
    public bool HidesXe => ViewHeight == 0f;

    /// <summary>The HUD's sight, or null while locked.</summary>
    public Hud.BomberSight? Shown => Locked ? null : new Hud.BomberSight(Cursor, BombsLeft);

    /// <summary>Each step (Kurt's ride): Kurt goes with the XE, the view sinks, the cursor moves and drops bombs.</summary>
    public void Update(Input input, float delta)
    {
        var kurt = _runtime.Kurt;
        kurt.Feet = _xe.Position;
        kurt.Yaw = _xe.Yaw;
        kurt.VerticalSpeed = 0f;
        ViewHeight = MathF.Max(ViewHeight - CameraDescent * delta, 0f);
        kurt.TopDownHeight = ViewHeight;
        if (HidesXe)
        {
            _xe.Flags |= MdkObject.FlagNotSolid;
        }

        var fire = input.IsDown(Key.Fire);
        if (Locked)
        {
            _speed.Y = 0f;
        }
        else
        {
            MoveCursor(input, delta);
            if (fire && !_fireHeld && BombsLeft > 0)
            {
                Drop();
            }

            RefillBombs(delta);
        }

        _fireHeld = fire;
        PassDamage();
    }

    private void MoveCursor(Input input, float delta)
    {
        var ticks = delta * Kurt.Kurt.Ticks;
        var turn = Axis(input, Key.TurnRight, Key.TurnLeft);
        var strafe = Axis(input, Key.StrafeRight, Key.StrafeLeft);
        var axis = new Vector2(MathF.Abs(turn) >= MathF.Abs(strafe) ? turn : strafe, Axis(input, Key.Back, Key.Forward));
        _speed = new Vector2(CursorStep(_speed.X, axis.X, ticks), CursorStep(_speed.Y, axis.Y, ticks));

        // The mouse moves it only while no key does.
        var mouse = new Vector2(input.MouseX, input.MouseY) * MouseScale;
        if (axis == Vector2.Zero && mouse != Vector2.Zero)
        {
            Cursor += mouse;
            _speed = Vector2.Zero;
        }

        Cursor = Vector2.Clamp(Cursor + _speed * ticks, CursorMin, CursorMax);
    }

    /// <summary>A cursor speed after a step: accelerating with the key (0x4688d0: the other way
    /// starts over), else braking.</summary>
    public static float CursorStep(float speed, float axis, float ticks)
    {
        if (axis == 0f)
        {
            return MathF.Abs(speed) <= CursorBrake * ticks ? 0f : speed - MathF.Sign(speed) * CursorBrake * ticks;
        }

        var step = axis * CursorAccel * ticks;
        var result = speed == 0f || MathF.Sign(speed) == MathF.Sign(step) ? speed + step : step;
        var limit = MathF.Abs(axis) * CursorSpeed;
        return Math.Clamp(result, -limit, limit);
    }

    private void RefillBombs(float delta)
    {
        if (BombsLeft >= Bombs)
        {
            _refill = RefillTime;
            return;
        }

        _refill -= delta;
        if (_refill <= 0f)
        {
            BombsLeft++;
            _refill = RefillTime;
        }
    }

    /// <summary>A bomb from under the XE, thrown so it falls onto the point under the cursor.
    /// <code>
    ///        Kurt ●───────► (vx, vy, 0)
    ///             ╎  ╲ falls by gravity for t = √(2h / g)
    ///             ╎     ╲
    ///   ──────────┴───────✕ hit point (ray through the cursor)
    /// </code></summary>
    private void Drop()
    {
        BombsLeft--;
        var kurt = _runtime.KurtPosition;
        var start = kurt - new Vector3(0f, 0f, DropBelow);
        var target = Aim(kurt, Cursor, _xe.Yaw, out var hit);
        var time = hit ? FallTime(start.Z - target.Z) : MissTime;

        var controller = _runtime.GetArenaState(_runtime.CurrentArena).Controller;
        if (_runtime.Spawn(controller, Bomb, start, _xe.Yaw, -1, 0, ScriptRuntime.Spawning.Plain) is not { } bomb)
        {
            return;
        }

        bomb.Flags |= BombFlags;
        bomb.ThrownKind = Items.KindBomb;
        bomb.ItemTicks = BombTicks;
        bomb.Friction = 0f;
        bomb.Velocity = new Vector3((target.X - start.X) / time, (target.Y - start.Y) / time, 0f);
        bomb.LoopSound = _runtime.Mixer.PlayOn(DropSound, () => bomb.Position);
    }

    /// <summary>Where the ray through the cursor meets an object of Kurt's arena or the arena.</summary>
    private Vector3 Aim(Vector3 kurt, Vector2 cursor, float yaw, out bool hit)
    {
        var end = kurt + AimDirection(cursor, yaw) * RayLength;
        var target = FirstObject(kurt, end) ?? end;
        hit = target != end;
        if (_runtime.Raycast(kurt, target) is { } ray)
        {
            hit = true;
            return ray.Point;
        }

        return target;
    }

    /// <summary>The view's ray through a cursor pixel: right is (s, −c), the top of the view the heading (c, s).</summary>
    public static Vector3 AimDirection(Vector2 cursor, float yaw)
    {
        var offset = cursor - CursorCentre;
        var s = MathF.Sin(float.DegreesToRadians(yaw));
        var c = MathF.Cos(float.DegreesToRadians(yaw));
        return new Vector3(s * offset.X - c * offset.Y, -c * offset.X - s * offset.Y, -Focal);
    }

    /// <summary>The time to fall <paramref name="height"/> from rest (2.5 s when it isn't below).</summary>
    public static float FallTime(float height)
    {
        var time = MathF.Sqrt(MathF.Max(height, 0f) * 2f / FallGravity);
        return time <= 0f ? MissTime : time;
    }

    /// <summary>Where the segment first meets a part of an object of Kurt's arena (alive, not flagged
    /// 0x30: the hidden XE doesn't count), or null.</summary>
    private Vector3? FirstObject(Vector3 start, Vector3 end)
    {
        Vector3? nearest = null;
        foreach (var obj in _runtime.Objects)
        {
            if (obj.Dead || obj.Health == 0 || obj.Arena != _runtime.CurrentArena || obj.Model == null || (obj.Flags & Unaimed) != 0
                || _runtime.GetWorldBounds(obj).SegmentEntry(start, end) == null)
            {
                continue;
            }

            var parts = obj.PartBounds();
            for (var i = 0; i < parts.Length; i++)
            {
                if ((obj.HiddenParts & (1 << i)) != 0 || parts[i] is not { } part
                    || _runtime.GetWorldBounds(obj, part).SegmentEntry(start, end) is not { } point)
                {
                    continue;
                }

                if (nearest is not { } n || Vector3.Distance(start, point) < Vector3.Distance(start, n))
                {
                    nearest = point;
                }
            }
        }

        return nearest;
    }

    /// <summary>Hits on Kurt went to the XE (<see cref="Rides.TakesHits"/>); Kurt takes them, and if
    /// he dies the XE does too (its death script drops him).</summary>
    private void PassDamage()
    {
        if (_xe.Health == Health)
        {
            return;
        }

        var kurt = _runtime.Kurt;
        kurt.Hurt(Health - _xe.Health);
        _xe.Health = Health;
        if (kurt.Health < 1)
        {
            _xe.Health = 0;
            _runtime.Kill(_xe);
        }
    }

    private static float Axis(Input input, Key positive, Key negative) =>
        (input.IsDown(positive) ? 1f : 0f) - (input.IsDown(negative) ? 1f : 0f);
}
