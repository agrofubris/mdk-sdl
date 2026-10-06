using System.Numerics;
using Mdk.Game.Kurt;

namespace Mdk.Game.Fall;

/// <summary>The missiles (0x412568, 0x411ee8) launched when a radar sees Kurt, their hits and
/// explosions.</summary>
public sealed partial class FallSim
{
    /// <summary>An explosion: where, its random yaw (radians) and its age in ticks.</summary>
    public sealed class Explosion(Vector3 position, float yaw)
    {
        public Vector3 Position = position;
        public float Yaw { get; } = yaw;
        public float Ticks;
    }

    /// <summary>An explosion lasts 26 ticks (the frames of its texture).</summary>
    public const int ExplosionTicks = 26;
    private const int MissileGapRandom = 32;
    private static readonly string[] ExplodeSounds = ["EXPLODE1", "EXPLODE2"];
    private static readonly string[] HitSounds = ["K_HIT1", "K_HIT2", "K_HIT3", "K_HIT4", "K_HIT5", "K_HIT6", "K_HIT7"];
    /// <summary>Damage of a hit: 4 on easy, 4 + 0-7 on normal, twice that on hard.</summary>
    private const int HitDamage = 4;
    private const int HitDamageRandom = 8;
    private const int HardFactor = 2;
    // Screen flashes: a detection dims the target brightness by 0.5 (not below 0.75), a launch by
    // 0.2 (not below 0.5).
    private const float DetectDim = 0.5f;
    private const float DetectFloor = 0.75f;
    private const float LaunchDim = 0.2f;
    private const float LaunchFloor = 0.5f;

    private readonly List<FallMissile> _missiles = [];
    private readonly List<Explosion> _explosions = [];
    private int _missileQueue;
    private int _missileWait;

    public IReadOnlyList<FallMissile> Missiles => _missiles;
    public IReadOnlyList<Explosion> Explosions => _explosions;

    private void UpdateMissiles(float dt, int ticks)
    {
        if (_missileQueue > 0)
        {
            _missileWait -= ticks;
            if (_missileWait <= 0)
            {
                _missileQueue--;
                _missileWait = MissileGap + _random.Next(MissileGapRandom);
                Launch();
            }
        }

        foreach (var missile in _missiles.ToList())
        {
            var previous = missile.Position;
            var happened = missile.Update(dt, ticks, KurtPosition);
            if (happened == FallMissile.Event.Passed)
            {
                Sound?.Invoke("M_PASS");
            }

            if (happened == FallMissile.Event.Gone)
            {
                _missiles.Remove(missile);
                continue;
            }

            if (Time <= SteerTime && !Dying && CrossesBox(KurtPosition, previous, missile.Position))
            {
                Hit(missile);
            }
        }
    }

    private void Launch()
    {
        Sound?.Invoke("M_LNCH");
        _rate = FlashRate;
        _target = MathF.Max(_target - LaunchDim, LaunchFloor);
        _missiles.Add(new FallMissile(Spread, _random));
    }

    /// <summary>A missile hits Kurt: an explosion, damage and a sound; at 0 health he's dying.</summary>
    private void Hit(FallMissile missile)
    {
        Sound?.Invoke(ExplodeSounds[_random.Next(ExplodeSounds.Length)]);
        Sound?.Invoke(HitSounds[_random.Next(HitSounds.Length)]);
        var damage = _setup.Difficulty switch
        {
            Difficulty.Normal => HitDamage + _random.Next(HitDamageRandom),
            Difficulty.Hard => HardFactor * (HitDamage + _random.Next(HitDamageRandom)),
            _ => HitDamage,
        };
        Health = Math.Max(Health - damage, 0);
        _brightness = HitBrightness;
        KurtPose = KurtAnimation.Hit;
        KurtFrame = 0f;
        _explosions.Add(new Explosion(missile.Position, _random.NextSingle() * MathF.Tau));
        _missiles.Remove(missile);
        if (Health > 0)
        {
            return;
        }

        Dying = true;
        _brightness = HitBrightness;
        KurtPose = KurtAnimation.Fall;
    }

    /// <summary>Explosions follow Kurt down while their frames play.</summary>
    private void UpdateExplosions(int ticks)
    {
        foreach (var explosion in _explosions.ToList())
        {
            explosion.Ticks += ticks;
            if (explosion.Ticks >= ExplosionTicks)
            {
                _explosions.Remove(explosion);
                continue;
            }

            explosion.Position.Z = KurtPosition.Z;
        }
    }
}
