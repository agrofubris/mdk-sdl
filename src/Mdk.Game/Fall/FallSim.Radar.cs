using System.Numerics;

namespace Mdk.Game.Fall;

/// <summary>The radars (0x4130ac create, 0x412af8 update): one at a time, its beam rises from the
/// ground to just below Kurt, wanders between targets (Kurt, a pickup, random points) and, when its
/// spot comes within 15 units of him, he's seen: missiles are queued and it retracts.
/// <code>
///   wait ──► Extend (3000 u/s up) ──► Track (wanders) ──seen──► Retract (1500 u/s down) ──► wait
/// </code></summary>
public sealed partial class FallSim
{
    public enum RadarState { Extend, Track, Retract }

    private const float RadarExtendSpeed = 3000f;
    private const float RadarRetractSpeed = 1500f;
    public const float RadarRadius = 10f;
    private const float RadarSee = 15f;
    /// <summary>The beam's spot stays 3 units below Kurt.</summary>
    private const float RadarBelow = 3f;
    /// <summary>Tracking: <c>v = 0.75 v + 0.25 × beam speed × unit(target − spot)</c>.</summary>
    private const float RadarInertia = 0.75f;
    /// <summary>A new target after 30 ticks, or once within 2.94 units (on both axes).</summary>
    private const int RadarTargetTicks = 30;
    private const float RadarNear = 2.94f;
    /// <summary>Choices of the next target: at most 9 of Kurt and the pickups, and 3 random points.</summary>
    private const int RadarChoices = 9;
    private const int RadarRandomChoices = 3;
    // The first radar comes 7-22 ticks after the intro, later ones the gap + 0-63 ticks after.
    private const int RadarFirstWait = 7;
    private const int RadarFirstRandom = 16;
    private const int RadarGapRandom = 64;

    private int _radarWait;
    private float _radarHeight;
    private Vector2 _radarTarget;
    private Vector2 _radarVelocity;
    private int _radarTicks;

    public bool RadarActive { get; private set; }
    public RadarState Radar { get; private set; }
    /// <summary>The beam's spot and radius.</summary>
    public Vector3 RadarSpot { get; private set; }
    public float RadarSpotRadius { get; private set; }

    private void StartRadars() => _radarWait = _random.Next(RadarFirstRandom) + RadarFirstWait;

    private void UpdateRadar(float dt, int ticks)
    {
        if (!RadarActive && !StartRadar(ticks))
        {
            return;
        }

        switch (Radar)
        {
            case RadarState.Extend:
                Extend(dt);
                break;
            case RadarState.Track:
                Track(dt, ticks);
                break;
            default:
                Retract(dt);
                break;
        }
    }

    /// <summary>The next radar once its wait is over (none after 30 s).</summary>
    private bool StartRadar(int ticks)
    {
        if (Time > SteerTime)
        {
            return false;
        }

        _radarWait -= ticks;
        if (_radarWait > 0)
        {
            return false;
        }

        RadarActive = true;
        Radar = RadarState.Extend;
        _radarTarget = RandomPoint();
        _radarHeight = KurtPosition.Z - RadarBelow;
        RadarSpot = Vector3.Zero;
        _radarVelocity = Vector2.Zero;
        Sound?.Invoke("R_START");
        return true;
    }

    /// <summary>The spot rises towards Kurt, its xy and radius growing with it.</summary>
    private void Extend(float dt)
    {
        _radarHeight = KurtPosition.Z - RadarBelow;
        var z = MathF.Min(RadarSpot.Z + RadarExtendSpeed * dt, _radarHeight);
        var f = z / _radarHeight;
        RadarSpot = new Vector3(_radarTarget * f, z);
        RadarSpotRadius = RadarRadius * f;
        if (z < _radarHeight)
        {
            return;
        }

        Radar = RadarState.Track;
        _radarTarget = RandomPoint();
        _radarTicks = 0;
    }

    private void Track(float dt, int ticks)
    {
        _radarHeight = KurtPosition.Z - RadarBelow;
        RadarSpotRadius = RadarRadius;
        var spot = new Vector2(RadarSpot.X, RadarSpot.Y);
        var towards = _radarTarget - spot;
        if (towards != Vector2.Zero)
        {
            towards = Vector2.Normalize(towards);
        }

        for (var i = 0; i < ticks; i++)
        {
            _radarVelocity = RadarInertia * _radarVelocity + (1f - RadarInertia) * BeamSpeed * towards;
        }

        spot += _radarVelocity * dt;
        RadarSpot = new Vector3(spot, _radarHeight);
        _radarTicks += ticks;
        var near = Vector2.Abs(_radarTarget - spot);
        if (_radarTicks >= RadarTargetTicks || (near.X < RadarNear && near.Y < RadarNear))
        {
            Sound?.Invoke("R_MOVE");
            _radarTarget = ChooseTarget();
            _radarTicks = 0;
        }

        var kurt = new Vector2(KurtPosition.X, KurtPosition.Y);
        if (Vector2.Distance(kurt, spot) < RadarSee && !Dying)
        {
            Detected();
        }
    }

    private void Retract(float dt)
    {
        var z = RadarSpot.Z - RadarRetractSpeed * dt;
        if (z <= 0f)
        {
            RadarActive = false;
            _radarWait = RadarGap + _random.Next(RadarGapRandom);
            return;
        }

        RadarSpotRadius = RadarRadius * z / _radarHeight;
        RadarSpot = RadarSpot with { Z = z };
    }

    /// <summary>Kurt was seen (<c>K_SEEN</c>): the radar retracts, missiles are queued and the screen flashes.</summary>
    private void Detected()
    {
        Sound?.Invoke("K_SEEN");
        Radar = RadarState.Retract;
        _missileQueue += MissilesPerDetection + _random.Next(2);
        _missileWait = Math.Min(_missileWait, 1);
        _rate = FlashRate;
        _target = MathF.Max(_target - DetectDim, DetectFloor);
    }

    /// <summary>The next point the radar goes to (0x412a38): Kurt, a falling pickup or a random point.</summary>
    private Vector2 ChooseTarget()
    {
        var choices = new List<Vector2> { new(KurtPosition.X, KurtPosition.Y) };
        foreach (var pickup in _pickups)
        {
            if (choices.Count < RadarChoices)
            {
                choices.Add(new Vector2(pickup.Position.X, pickup.Position.Y));
            }
        }

        for (var i = 0; i < RadarRandomChoices; i++)
        {
            choices.Add(RandomPoint());
        }

        return choices[_random.Next(choices.Count)];
    }
}
