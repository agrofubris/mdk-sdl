using System.Numerics;

namespace Mdk.Game.Kurt;

/// <summary>Sniper mode's scope (0x573a60, controls 0x467384; godot-mdk kurt.gd and docs/gameplay.md
/// "Sniper mode"): looking around, the zoom, the clip of 3 rounds and the scope's projection.
/// <code>
///   turn/tilt keys ──► look speeds ──► yaw, pitch (±50°)     slower when zoomed in
///   zoom keys, wheel ──► zoom speed ──► zoom (1 ... limit)    focal length 384 / zoom
///   clip timer: 3 ──(4/s)──► 0: loaded, fire ──► +1 (a quarter of a second) ... empty ──► 3
/// </code></summary>
public sealed class Scope
{
    public enum Turbo { Off, On }

    /// <summary>Whether the zoom moves this step (the ZOOM sound loops meanwhile).</summary>
    public enum Zooming { Still, Moving }

    /// <summary>What the clip did this step.</summary>
    [Flags]
    public enum Clip { None = 0, Loaded = 1, Fired = 2 }

    /// <summary>The zoom key's state, and fire's.</summary>
    public enum Trigger { Released, Held }

    /// <summary>Zoom (0x57391c, the inverse of the magnification): 1 on entering, down to 0.25 (4x).</summary>
    public const float ZoomMin = 0.25f;
    public const float PitchLimit = 50f;
    public const int ClipSize = 3;
    /// <summary>Normal bullets: as many as wanted.</summary>
    public const int Bullets = 0;
    /// <summary>Bones' air strike, the last ammo type.</summary>
    public const int Strike = 5;

    // Looking around: degrees per second (0.4/0.6 per tick², at most 4/6 per tick), scaled by
    // zoom x 0.416667.
    private const float LookAcceleration = 0.4f * Kurt.Ticks * Kurt.Ticks;
    private const float LookAccelerationTurbo = 0.6f * Kurt.Ticks * Kurt.Ticks;
    private const float LookSpeed = 4f * Kurt.Ticks;
    private const float LookSpeedTurbo = 6f * Kurt.Ticks;
    private const float LookFrictionSlow = 1.0667f * Kurt.Ticks * Kurt.Ticks;
    private const float LookFrictionFast = 1.6f * Kurt.Ticks * Kurt.Ticks;
    private const float LookScale = 0.416667f;

    /// <summary>The zoom speed grows by 0.01 per tick up to 0.15 while a zoom key is held and decays by 0.015.</summary>
    private const float ZoomAcceleration = 0.01f;
    private const float ZoomMaxSpeed = 0.15f;
    private const float ZoomDecay = 0.015f;
    /// <summary>A wheel notch holds the zoom key for this many ticks (the Godot port's).</summary>
    private const float WheelTicks = 6f;

    /// <summary>The clip timer (0x5743eb) drops by 4 per second; a shot adds 1, an empty clip sets 3.</summary>
    private const float ClipDecay = 4f;
    private const float ShotTime = 1f;
    private const float ReloadTime = 3f;
    /// <summary>A shot needs 5 ticks since the last one.</summary>
    private const float ShotGap = 5f / Kurt.Ticks;
    /// <summary>Normal bullets never run out.</summary>
    private const int EndlessStock = 999;

    // The scope's projection (0x57428c): a 384x280 viewport at (127, 139) of the 640x480 screen,
    // centred on (319, 279); the focal length is 384 / zoom pixels.
    public const float Focal = 384f;
    public static readonly Vector2 Centre = new(319f, 279f);
    public const float ScreenHeight = 480f;
    /// <summary>The target lock's square around the crosshair (0x43b65c), pixels.</summary>
    private const float LockHalf = 32f;
    /// <summary>The zoom limit on a locked target: 0.375 x its height (at least 10) / distance (0x4678b0).</summary>
    private const float LockZoomFactor = 0.375f;
    private const float LockMinHeight = 10f;

    /// <summary>The eye: 4 above the feet, and forward by 5 (1 - cos pitch) looking down.</summary>
    private const float EyeHeight = 4f;
    private const float EyeForward = 5f;

    /// <summary>Degrees, positive looks down (0x573918).</summary>
    public float Pitch;
    public float Zoom = 1f;
    /// <summary>How far the zoom goes: <see cref="ZoomMin"/>, or less on a far locked target.</summary>
    public float ZoomLimit = ZoomMin;
    public int ClipRounds;
    public float ClipTime;

    private Vector2 _lookSpeed;
    private float _zoomSpeed;
    private float _wheelTicks;
    private float _wheelDirection;
    private float _sinceShot = float.MaxValue;

    /// <summary>Entering sniper mode: level, zoomed out, an empty clip.</summary>
    public void Reset()
    {
        Pitch = 0f;
        Zoom = 1f;
        ClipRounds = 0;
        ClipTime = 0f;
        _lookSpeed = Vector2.Zero;
        _zoomSpeed = 0f;
        _wheelTicks = 0f;
        _sinceShot = float.MaxValue;
    }

    /// <summary>The turn (right positive) and tilt (down positive) keys, and the mouse (degrees at
    /// zoom 1), turn the view. Returns the yaw change in degrees.</summary>
    public float Look(float turn, float tilt, Vector2 mouse, Turbo turbo, float delta)
    {
        _lookSpeed.X = Accelerate(_lookSpeed.X, turn, turbo, delta);
        _lookSpeed.Y = Accelerate(_lookSpeed.Y, tilt, turbo, delta);
        var scale = Zoom * LookScale * delta;
        Pitch = Math.Clamp(Pitch + _lookSpeed.Y * scale + mouse.Y * Zoom, -PitchLimit, PitchLimit);
        return -(_lookSpeed.X * scale + mouse.X * Zoom);
    }

    private static float Accelerate(float speed, float input, Turbo turbo, float delta)
    {
        if (input == 0f)
        {
            var friction = MathF.Abs(speed) > LookSpeed ? LookFrictionFast : LookFrictionSlow;
            return MoveToward(speed, 0f, friction * delta);
        }

        var max = turbo == Turbo.On ? LookSpeedTurbo : LookSpeed;
        if (speed * input < 0f)
        {
            speed = 0f;
        }

        var acceleration = turbo == Turbo.On ? LookAccelerationTurbo : LookAcceleration;
        return Math.Clamp(speed + input * acceleration * delta, -max, max);
    }

    /// <summary>Wheel notches (positive away from the user: zoom in) hold a zoom key for a while.</summary>
    public void Wheel(float notches)
    {
        if (notches == 0f)
        {
            return;
        }

        var direction = notches > 0f ? -1f : 1f;
        var ticks = WheelTicks * MathF.Abs(notches);
        _wheelTicks = direction != _wheelDirection ? ticks : _wheelTicks + ticks;
        _wheelDirection = direction;
    }

    /// <summary>The zoom (0x4687a4): <paramref name="direction"/> -1 zooms in, 1 out. Zooming out
    /// multiplies it by <c>1 + v</c> each tick, zooming in divides it, between 1 and the limit.</summary>
    public Zooming UpdateZoom(float direction, float delta)
    {
        var ticks = delta * Kurt.Ticks;
        if (_wheelTicks > 0f)
        {
            _wheelTicks -= ticks;
            if (direction == 0f)
            {
                direction = _wheelDirection;
            }
        }

        if (direction != 0f)
        {
            _zoomSpeed = Math.Clamp(_zoomSpeed + direction * ZoomAcceleration * ticks, -ZoomMaxSpeed, ZoomMaxSpeed);
        }
        else
        {
            _zoomSpeed = MoveToward(_zoomSpeed, 0f, ZoomDecay * ticks);
        }

        if (_zoomSpeed > 0f)
        {
            Zoom = MathF.Min(Zoom * MathF.Pow(1f + _zoomSpeed, ticks), 1f);
        }
        else if (_zoomSpeed < 0f)
        {
            Zoom = MathF.Max(Zoom / MathF.Pow(1f - _zoomSpeed, ticks), ZoomLimit);
        }

        var moving = direction != 0f && (Zoom < 1f || direction < 0f) && (Zoom > ZoomLimit || direction > 0f);
        return moving ? Zooming.Moving : Zooming.Still;
    }

    /// <summary>The clip (0x41eb10) and firing (0x461e88): while the timer runs, rounds load one per
    /// step up to 3 (only as many as there's ammo for, except normal bullets); a shot needs the timer at
    /// 0, 5 ticks since the last one, and <paramref name="fire"/> to find a free round slot.</summary>
    public Clip UpdateClip(Inventory inventory, Trigger trigger, Func<int, bool> fire, float delta)
    {
        var result = Clip.None;
        _sinceShot += delta;
        var type = inventory.SelectedAmmo;
        var stock = type == Bullets ? EndlessStock : inventory.Ammo[type - 1];
        if (ClipTime > 0f && ClipRounds < Math.Min(ClipSize, stock))
        {
            ClipRounds++;
            result |= Clip.Loaded;
        }

        if (ClipRounds == 0 && ClipTime <= 0f)
        {
            if (type != Bullets && stock <= 0)
            {
                inventory.SelectAmmo(Bullets);
            }

            ClipTime = ReloadTime;
        }

        ClipTime = MathF.Max(ClipTime - ClipDecay * delta, 0f);
        if (trigger == Trigger.Released || ClipTime > 0f || _sinceShot < ShotGap || ClipRounds <= 0 || !fire(type))
        {
            return result;
        }

        _sinceShot = 0f;
        ClipRounds--;
        inventory.UseAmmo(type);
        ClipTime += ShotTime;
        if (ClipRounds == 0)
        {
            ClipTime = ReloadTime;
        }

        return result | Clip.Fired;
    }

    /// <summary>The next (<paramref name="step"/> 1) or previous (-1) ammo type with rounds
    /// (0x46c900); the clip reloads.</summary>
    public void SelectAmmo(Inventory inventory, int step)
    {
        var type = inventory.NextAmmo(step);
        if (type == inventory.SelectedAmmo)
        {
            return;
        }

        inventory.SelectAmmo(type);
        ClipRounds = 0;
        ClipTime = ReloadTime;
    }

    /// <summary>The eye for Kurt's feet and facing.</summary>
    public Vector3 Eye(Vector3 feet, Vector3 facing)
    {
        var eye = feet + Vector3.UnitZ * EyeHeight;
        if (Pitch > 0f)
        {
            eye += facing * EyeForward * (1f - MathF.Cos(float.DegreesToRadians(Pitch)));
        }

        return eye;
    }

    /// <summary>The line of sight for a yaw (degrees) and this pitch.</summary>
    public Vector3 Forward(float yaw) => Direction(yaw, Pitch);

    /// <summary>A direction from a yaw and a pitch (degrees, positive down).</summary>
    public static Vector3 Direction(float yaw, float pitch)
    {
        var y = float.DegreesToRadians(yaw);
        var p = float.DegreesToRadians(pitch);
        return new Vector3(MathF.Cos(y) * MathF.Cos(p), MathF.Sin(y) * MathF.Cos(p), -MathF.Sin(p));
    }

    /// <summary>Where a point shows on the 640x480 screen, seen from <paramref name="eye"/> along
    /// <paramref name="forward"/>; null behind the eye.</summary>
    public static Vector2? ToScreen(Vector3 eye, Vector3 forward, float zoom, Vector3 point)
    {
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
        var up = Vector3.Cross(right, forward);
        var offset = point - eye;
        var depth = Vector3.Dot(offset, forward);
        if (depth <= 0f)
        {
            return null;
        }

        var focal = Focal / zoom;
        return Centre + new Vector2(Vector3.Dot(offset, right), -Vector3.Dot(offset, up)) * focal / depth;
    }

    /// <summary>Whether a screen box overlaps the lock square around the crosshair.</summary>
    public static bool Locks(Vector2 min, Vector2 max) =>
        min.X <= Centre.X + LockHalf && max.X >= Centre.X - LockHalf && min.Y <= Centre.Y + LockHalf && max.Y >= Centre.Y - LockHalf;

    /// <summary>The zoom limit on a locked target of this height at this distance.</summary>
    public static float LockLimit(float height, float distance) =>
        MathF.Min(ZoomMin, LockZoomFactor * MathF.Max(height, LockMinHeight) / MathF.Max(distance, 1f));

    private static float MoveToward(float from, float to, float step) =>
        MathF.Abs(to - from) <= step ? to : from + MathF.Sign(to - from) * step;
}
