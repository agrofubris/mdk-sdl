using System.Numerics;

namespace Mdk.Game.Fall;

/// <summary>The pickups (0x4128fc, 0x41275c) of <c>FALLPU_n</c>, dropped one by one from above Kurt
/// (the list's last first): they fall at twice his speed, open a chute after 1-3 seconds and brake
/// to 50 u/s, spinning. Kurt takes the ones that cross his box until 30 s.</summary>
public sealed partial class FallSim
{
    public sealed class Pickup(string name, Vector3 position, int fallTicks)
    {
        public string Name { get; } = name;
        public Vector3 Position = position;
        public Vector3 Velocity = new(0f, 0f, -PickupFallSpeed);
        /// <summary>Yaw in degrees.</summary>
        public float Yaw;
        public bool Chute;
        public int FallTicks { get; } = fallTicks;
        public int Ticks;
    }

    private const float PickupFallSpeed = 2f * KurtSpeed;
    private const float PickupChuteSpeed = 50f;
    /// <summary>Degrees per second under the chute.</summary>
    private const float PickupSpin = 30f;
    /// <summary>Dropped 15 units above Kurt, within 95 % of his limits.</summary>
    private const float PickupAbove = 15f;
    private const float PickupSpread = 0.95f;
    // The first pickup comes 31-62 ticks after the intro, the next ones 31-158 ticks apart; each
    // opens its chute after 30-93 ticks.
    private const int PickupWait = 31;
    private const int PickupFirstRandom = 32;
    private const int PickupNextRandom = 128;
    private const int ChuteTicks = 30;
    private const int ChuteRandom = 64;
    private static readonly string[] CollectSounds = ["K_COLL1", "K_COLL2"];

    private readonly List<string> _pickupNames;
    private readonly List<Pickup> _pickups = [];
    private int _pickupWait;

    public IReadOnlyList<Pickup> Pickups => _pickups;

    private void StartPickups()
    {
        if (_pickupNames.Count != 0)
        {
            _pickupWait = _random.Next(PickupFirstRandom) + PickupWait;
        }
    }

    private void UpdatePickups(float dt, int ticks)
    {
        if (_pickupNames.Count != 0 && Time <= SteerTime)
        {
            _pickupWait -= ticks;
            if (_pickupWait <= 0)
            {
                _pickupWait = PickupWait + _random.Next(PickupNextRandom);
                Spawn(_pickupNames[^1]);
                _pickupNames.RemoveAt(_pickupNames.Count - 1);
            }
        }

        foreach (var pickup in _pickups.ToList())
        {
            var previous = pickup.Position;
            MovePickup(pickup, dt, ticks);

            // Only under its chute (0x41275c): dropped above the camera, it would go at once.
            if (!pickup.Chute)
            {
                continue;
            }

            if (pickup.Position.Z > CameraPosition.Z)
            {
                _pickups.Remove(pickup);
            }
            else if (!Dying && Time <= SteerTime && CrossesBox(KurtPosition, previous, pickup.Position))
            {
                Take(pickup);
            }
        }
    }

    /// <summary>Falls, then brakes to 50 u/s and spins under its chute.</summary>
    private void MovePickup(Pickup pickup, float dt, int ticks)
    {
        pickup.Ticks += ticks;
        if (!pickup.Chute && pickup.Ticks >= pickup.FallTicks)
        {
            Sound?.Invoke("P_CHUTE");
            pickup.Chute = true;
        }

        if (pickup.Chute)
        {
            pickup.Velocity.Z = MathF.Min(pickup.Velocity.Z + KurtSpeed * dt, -PickupChuteSpeed);
            pickup.Yaw += PickupSpin * dt;
        }

        pickup.Position += pickup.Velocity * dt;
    }

    private void Take(Pickup pickup)
    {
        Sound?.Invoke("P_COLL");
        Sound?.Invoke(CollectSounds[_random.Next(CollectSounds.Length)]);
        var health = Health;
        Inventory.Collect(pickup.Name, ref health);
        Health = health;
        _collected.Add(pickup.Name);
        _pickups.Remove(pickup);
    }

    private void Spawn(string name)
    {
        Sound?.Invoke("P_FALL");
        var position = new Vector3(RandomRange(PickupSpread * Limit.X), RandomRange(PickupSpread * Limit.Y), KurtPosition.Z + PickupAbove);
        _pickups.Add(new Pickup(name, position, ChuteTicks + _random.Next(ChuteRandom)));
    }
}
