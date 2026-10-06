using System.Numerics;

namespace Mdk.Game.Fall;

/// <summary>A missile of the fall (fall_missile.gd; launch 0x412568, each frame 0x411ee8). It flies
/// free for 60 ticks, then homes on Kurt; once it's more than 5 units above him it has passed
/// (<c>M_PASS</c>): it stops homing, flies on and goes 60 ticks later.
/// <code>
///   Free (60 ticks) ──► Homing ──(5 units above Kurt)──► Passed (60 ticks) ──► gone
/// </code></summary>
public sealed class FallMissile
{
    /// <summary>What happened this frame.</summary>
    public enum Event { None, Passed, Gone }

    private enum Phase { Free, Homing, Passed }

    private const float Speed = 250f;
    private const int FreeTicks = 60;
    private const float PassHeight = 5f;
    private const int GoneTicks = 60;
    public const int TrailPoints = 32;
    /// <summary>Homing: <c>v = 0.8 v + 0.2 × 250 × unit(aim − position)</c>.</summary>
    private const float Homing = 0.2f;
    /// <summary>Below 0.75 × Kurt's height it climbs 3 times as fast.</summary>
    private const float BoostHeight = 0.75f;
    private const float Boost = 3f;
    /// <summary>The aim is below Kurt by <c>min(depth / 225, 10) × 66.67</c>.</summary>
    private const float AimDepth = 225f;
    private const float AimMax = 10f;
    private const float AimSpeed = 2000f / 30f;

    public Vector3 Position;
    public Vector3 Velocity;
    /// <summary>The aim's offset from Kurt (x, y).</summary>
    private readonly Vector2 _offset;
    /// <summary>The last points, for the smoke trail.</summary>
    private readonly List<Vector3> _trail = [];
    private Phase _phase = Phase.Free;
    /// <summary>Ticks left free, or since it passed.</summary>
    private float _ticks = FreeTicks;

    public IReadOnlyList<Vector3> Trail => _trail;

    /// <summary>Launched from the origin in a random direction, climbing, with a random aim offset
    /// of ±<paramref name="spread"/>.</summary>
    public FallMissile(float spread, Random random)
    {
        var a = random.NextSingle() * MathF.Tau;
        Velocity = new Vector3(MathF.Sin(a), MathF.Cos(a), 1f) * Speed;
        _offset = new Vector2(Spread(random, spread), Spread(random, spread));
    }

    private static float Spread(Random random, float spread) => (random.NextSingle() * 2f - 1f) * spread;

    /// <summary>Moves the missile for a frame, Kurt being at <paramref name="kurt"/>.</summary>
    public Event Update(float dt, int ticks, Vector3 kurt)
    {
        Position += Velocity * dt;
        if (Position.Z < BoostHeight * kurt.Z)
        {
            Position.Z += Boost * Velocity.Z * dt;
        }

        for (var i = 0; i < ticks; i++)
        {
            _trail.Add(Position);
        }

        if (_trail.Count > TrailPoints)
        {
            _trail.RemoveRange(0, _trail.Count - TrailPoints);
        }

        switch (_phase)
        {
            case Phase.Free:
                _ticks -= ticks;
                if (_ticks <= 0f)
                {
                    _phase = Phase.Homing;
                }

                return Event.None;
            case Phase.Homing:
                return Home(kurt);
            default:
                _ticks += ticks;
                return _ticks > GoneTicks ? Event.Gone : Event.None;
        }
    }

    /// <summary>Steers towards a point below Kurt (deeper the farther below him it is), until it's 5
    /// units above him.</summary>
    private Event Home(Vector3 kurt)
    {
        var dz = kurt.Z - Position.Z;
        if (dz <= -PassHeight)
        {
            _phase = Phase.Passed;
            _ticks = 0f;
            return Event.Passed;
        }

        var aim = kurt + new Vector3(_offset.X, _offset.Y, -MathF.Min(dz / AimDepth, AimMax) * AimSpeed);
        var towards = aim - Position;
        if (towards != Vector3.Zero)
        {
            towards = Vector3.Normalize(towards);
        }

        Velocity = (1f - Homing) * Velocity + Homing * Speed * towards;
        return Event.None;
    }
}
