using System.Numerics;
using Mdk.Game.Audio;
using Mdk.Game.Objects;

namespace Mdk.Game.Scripts;

/// <summary>Bones' air strike (sniper ammo type 5, SW_BONES; 0x4641ac, 0x43fa0c, 0x43deac; a port of
/// godot-mdk's air_strike.gd, docs/gameplay.md "Sniper mode").
/// <code>
///   start (50 behind Kurt, arena top + 30) ─► before ─► over the target (+32) ─► after ─► end
///                                             teeth: 9 X_TOOTH grenades, one every 15 units
///   last two levels: one strike only, diving into the target (450 damage within 80)
/// </code>
/// The point in the crosshair (within 5000 units, with open sky 1000 units above it) is the target.
/// X_STRIKE flies a 5-point curve at 150 units/s.</summary>
public sealed class AirStrike(ScriptRuntime runtime)
{
    /// <summary>Over the target dropping teeth, or diving into it.</summary>
    private enum Flight { Over, Dive }

    private const float Speed = 150f;
    private const float Range = 5000f;
    private const float Sky = 1000f;
    private const float Height = 30f;
    private const float OverTarget = 32f;
    private const float Behind = 50f;
    private const int Teeth = 9;
    private const float ToothSpacing = 15f;
    /// <summary>The middle tooth falls over the target.</summary>
    private const float ToothBias = 5f;
    private const int ToothTicks = 900;
    private const float ToothFall = -5f;
    private const int DiveDamage = 450;
    private const int DiveKurtDamage = 67;
    private const float DiveRadius = 80f;
    private const int HitDive = -6;
    private const float ExplosionScale = 2f;
    private const int TargetsAll = 6;
    private const int TargetKurt = 1;
    private const int StrikeHealth = 65000;
    private const int StrikeFlags = 0x85e20;
    /// <summary>Thrown items' flags (Items.ThrownFlags): the teeth fall as hand grenades.</summary>
    private const int ThrownFlags = 0x818a6;
    /// <summary>Levels after the fourth in the order of play (LEVEL8, LEVEL5) have the dive.</summary>
    private const int LastStrikeLevel = 3;
    /// <summary>Points towards the target are tried in steps of 9% of the way.</summary>
    private const float ClearStep = 0.09f;
    private const int ClearSteps = 10;
    private const float ClearFallback = 0.9f;
    /// <summary>The target sees the path from 2 units above it.</summary>
    private const float TargetEye = 2f;

    /// <summary>The one strike of the last two levels was used (0x57440b).</summary>
    public bool UsedUp;

    private MdkObject? _strike;
    private BezierPath? _curve;
    private float _offset;
    private float _targetOffset;
    private float _diveOffset = float.PositiveInfinity;
    private int _teeth;
    private bool _diving;

    public bool IsActive() => _strike is { Dead: false };

    /// <summary>The point in the view from <paramref name="eye"/> a strike can hit (0x4641ac): within
    /// 5000 units, with open sky 1000 units above it; null if there's none.</summary>
    public Vector3? FindTarget(Vector3 eye, Vector3 forward)
    {
        if (runtime.Raycast(eye, eye + forward * Range) is not { } hit)
        {
            return null;
        }

        var target = hit.Point;
        return runtime.Raycast(target + Vector3.UnitZ, target + new Vector3(0f, 0f, Sky)) == null ? target : null;
    }

    /// <summary>Calls the strike on the point in the view. Returns false (and Kurt blows a raspberry)
    /// without a target, while a strike is out or when it's used up.</summary>
    public bool Call(Vector3 eye, Vector3 forward)
    {
        if (IsActive())
        {
            return false;
        }

        if (FindTarget(eye, forward) is not { } target || UsedUp)
        {
            runtime.Mixer.Play("RASPBER", SoundMixer.Start.Restart);
            return false;
        }

        var flight = GameStats.IndexOf(runtime.Level.Number) > LastStrikeLevel ? Flight.Dive : Flight.Over;
        UsedUp |= flight == Flight.Dive;

        // Bones in his plane, full screen (without him when he dives).
        runtime.PlayStrikeScene(StrikeScene.Kind.Bones, flight == Flight.Dive ? StrikeScene.Plane.Only : StrikeScene.Plane.WithPilot);
        Start(target, flight);
        return true;
    }

    private void Start(Vector3 target, Flight flight)
    {
        var dive = flight == Flight.Dive;
        var arena = runtime.Level.Arenas.Find(a => a.Name == runtime.CurrentArena);
        var top = arena is { Vertices.Length: > 0 } ? arena.Vertices.Max(v => v.Z) : target.Z;
        var cruise = top + Height;
        var kurt = runtime.KurtPosition;
        var away = Vector2.Normalize(new Vector2(kurt.X - target.X, kurt.Y - target.Y)) * Behind;
        if (float.IsNaN(away.X))
        {
            away = Vector2.Zero;
        }

        var start = new Vector3(kurt.X + away.X, kurt.Y + away.Y, cruise);
        var over = target + new Vector3(0f, 0f, OverTarget);
        var end = over * 2f - start;
        if (!dive)
        {
            end.Z = cruise;
        }

        var before = ClearPoint(start, over, target);
        var after = dive ? (over + end) * 0.5f : ClearPoint(end, over, target);
        _curve = new BezierPath([start, before, over, after, end]);
        _targetOffset = _curve.ClosestOffset(over);
        _diveOffset = dive ? (_curve.ClosestOffset(before) + _targetOffset) * 0.5f : float.PositiveInfinity;
        _offset = 0f;
        _teeth = 0;
        _diving = false;
        var controller = runtime.GetArenaState(runtime.CurrentArena).Controller;
        _strike = runtime.Spawn(controller, "X_STRIKE", start, 0f, -1, 0, ScriptRuntime.Spawning.Plain);
        if (_strike == null)
        {
            return;
        }

        _strike.Health = StrikeHealth;
        _strike.Flags = StrikeFlags;
    }

    /// <summary>The first point from <paramref name="from"/> towards <paramref name="over"/> that the target can see.</summary>
    private Vector3 ClearPoint(Vector3 from, Vector3 over, Vector3 target)
    {
        for (var i = 1; i <= ClearSteps; i++)
        {
            var point = Vector3.Lerp(from, over, ClearStep * i);
            if (runtime.Raycast(target + new Vector3(0f, 0f, TargetEye), point) == null)
            {
                return point;
            }
        }

        return Vector3.Lerp(from, over, ClearFallback);
    }

    /// <summary>Moves the strike by <paramref name="ticks"/>.</summary>
    public void Update(float ticks)
    {
        if (!IsActive() || _curve == null)
        {
            _strike = null;
            return;
        }

        var strike = _strike!;
        if (_diving)
        {
            Crash(strike);
            return;
        }

        var previous = strike.Position;
        _offset += Speed / Kurt.Kurt.Ticks * ticks;
        if (_offset >= _curve.Length)
        {
            runtime.Remove(strike);
            return;
        }

        strike.Position = _curve.Sample(_offset);
        var motion = strike.Position - previous;
        if (motion != Vector3.Zero)
        {
            strike.Yaw = (float.RadiansToDegrees(MathF.Atan2(motion.Y, motion.X)) + 360f) % 360f;
        }

        if (_offset >= _diveOffset)
        {
            // It leaves the path with its velocity, falling, until it hits something.
            _diving = true;
            strike.Velocity = motion * Kurt.Kurt.Ticks / ticks;
            strike.Flags |= MdkObject.FlagGravity | MdkObject.FlagCollides;
            return;
        }

        var tooth = (int)MathF.Round((_offset - _targetOffset) / ToothSpacing + ToothBias);
        while (_teeth < tooth && _teeth < Teeth && float.IsPositiveInfinity(_diveOffset))
        {
            _teeth++;
            DropTooth(strike);
        }
    }

    /// <summary>The diving strike blows up when it hits something.</summary>
    private void Crash(MdkObject strike)
    {
        if ((strike.ContactFlags & (MdkObject.ContactCollided | MdkObject.ContactFloor)) == 0)
        {
            return;
        }

        var center = strike.Position;
        runtime.Items.Blast(center, DiveDamage, DiveRadius, TargetsAll, HitDive, strike);
        runtime.Items.Blast(center, DiveKurtDamage, DiveRadius, TargetKurt, HitDive, strike);
        runtime.SpawnExplosion(strike.Arena, center, ExplosionScale);
        runtime.PlaySoundAt(ScriptRuntime.ExplodeSound, center);
        runtime.Remove(strike);
    }

    /// <summary>A tooth falls as a hand grenade.</summary>
    private void DropTooth(MdkObject strike)
    {
        var controller = runtime.GetArenaState(runtime.CurrentArena).Controller;
        var tooth = runtime.Spawn(controller, "X_TOOTH", strike.Position, strike.Yaw, -1, 0, ScriptRuntime.Spawning.Plain);
        if (tooth == null)
        {
            return;
        }

        tooth.Flags |= ThrownFlags;
        tooth.ThrownKind = (int)Kurt.Inventory.Item.Grenade;
        tooth.ItemTicks = ToothTicks;
        tooth.Friction = 0f;
        tooth.Velocity = new Vector3(0f, 0f, ToothFall);
        tooth.LoopSound = runtime.Mixer.PlayOn("DROP", () => tooth.Position);
    }
}

/// <summary>A path through points with Bézier handles of a quarter of the neighbours' distance
/// (Godot's Curve3D as the Godot port builds it), sampled by length.</summary>
public sealed class BezierPath
{
    private const float HandleFactor = 0.25f;
    private const int SamplesPerSegment = 64;

    private readonly List<Vector3> _points = [];
    private readonly List<float> _lengths = [];

    public float Length => _lengths[^1];

    public BezierPath(IReadOnlyList<Vector3> points)
    {
        _points.Add(points[0]);
        _lengths.Add(0f);
        for (var i = 0; i + 1 < points.Count; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            var outHandle = (b - points[Math.Max(i - 1, 0)]) * HandleFactor;
            var inHandle = -(points[Math.Min(i + 2, points.Count - 1)] - a) * HandleFactor;
            for (var s = 1; s <= SamplesPerSegment; s++)
            {
                var point = Cubic(a, a + outHandle, b + inHandle, b, (float)s / SamplesPerSegment);
                _lengths.Add(_lengths[^1] + Vector3.Distance(_points[^1], point));
                _points.Add(point);
            }
        }
    }

    private static Vector3 Cubic(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        var u = 1f - t;
        return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
    }

    /// <summary>The point <paramref name="offset"/> along the path.</summary>
    public Vector3 Sample(float offset)
    {
        var index = _lengths.BinarySearch(Math.Clamp(offset, 0f, Length));
        if (index >= 0)
        {
            return _points[index];
        }

        var next = Math.Min(~index, _points.Count - 1);
        var previous = Math.Max(next - 1, 0);
        var span = _lengths[next] - _lengths[previous];
        var t = span > 0f ? (offset - _lengths[previous]) / span : 0f;
        return Vector3.Lerp(_points[previous], _points[next], t);
    }

    /// <summary>The offset along the path of its point nearest to <paramref name="point"/>.</summary>
    public float ClosestOffset(Vector3 point)
    {
        var best = 0f;
        var bestDistance = float.MaxValue;
        for (var i = 0; i + 1 < _points.Count; i++)
        {
            var a = _points[i];
            var segment = _points[i + 1] - a;
            var squared = segment.LengthSquared();
            var t = squared > 0f ? Math.Clamp(Vector3.Dot(point - a, segment) / squared, 0f, 1f) : 0f;
            var distance = Vector3.DistanceSquared(point, a + segment * t);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = _lengths[i] + (_lengths[i + 1] - _lengths[i]) * t;
            }
        }

        return best;
    }
}
