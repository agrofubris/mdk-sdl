using System.Numerics;

namespace Mdk.Game.Scripts;

/// <summary>What the scripts see of Kurt and do to him. The game feeds his position, yaw, velocity
/// and floor contact each tick; teleports come back through <see cref="Teleported"/>. Health,
/// firing, flashes, knock-downs, slides and the inventory are kept here until Kurt has them.</summary>
// TODO port the rest of kurt/kurt.gd (health, firing, sniper mode, knock-down, slides) and kurt/inventory.gd
public sealed class KurtLink
{
    /// <summary>Extra gravity of the wind zones in the air (kurt.gd SLIDE_GRAVITY, u/s²).</summary>
    public const float SlideGravity = 128f;
    public const float ZoomMin = 0.25f;

    /// <summary>Feet (MDK), yaw (MDK degrees), velocity (u/s).</summary>
    public Vector3 Position;
    public float Yaw;
    public Vector3 Velocity;
    public bool OnFloor;

    public int Health = 100;
    public bool Firing;
    public bool Sniping;
    public float ZoomLimit = ZoomMin;
    public float WhiteFlash;
    public float Invulnerable;
    public float KnockDamage;
    public bool Knocked;
    public bool Frozen;
    public bool Visible = true;
    public bool Sliding;
    public readonly Inventory Inventory = new();

    /// <summary>A script moved Kurt: feet and yaw.</summary>
    public event Action<Vector3, float>? Teleported;

    public void Teleport(Vector3 feet, float yaw)
    {
        Position = feet;
        Yaw = yaw;
        Teleported?.Invoke(feet, yaw);
    }

    public void Hurt(int damage) => Health = Math.Max(Health - damage, 0);

    public void StopFiring() => Firing = false;

    public void KnockDown(Vector2 push)
    {
    }

    public void StartSlide() => Sliding = true;

    public void StopSlide() => Sliding = false;

    public void SlideAccel(Vector2 acceleration, float dt)
    {
    }

    /// <summary>Kurt's vertical speed change (wind zones, push_kurt).</summary>
    public void AddVerticalSpeed(float speed)
    {
    }
}

/// <summary>Kurt's items and ammunition. Empty: pickups aren't taken yet.</summary>
// TODO port kurt/inventory.gd
public sealed class Inventory
{
    public readonly record struct Slot(int Item, int Count);

    /// <summary>The difficulty (0-2), by default medium.</summary>
    public int Difficulty = 1;
    public readonly List<Slot> Slots = [];
    /// <summary>Sniper ammo of the 5 round types.</summary>
    public readonly int[] Ammo = new int[5];
    /// <summary>Ticks of super chain gun left.</summary>
    public int SuperChainGun;

    /// <summary>Takes a pickup; returns its sound, or "" when it isn't taken.</summary>
    public string Collect(string pickup) => "";

    public void TickSuperChainGun(int ticks) => SuperChainGun = Math.Max(SuperChainGun - ticks, 0);
}
