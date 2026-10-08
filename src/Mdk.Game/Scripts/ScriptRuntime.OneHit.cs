using Mdk.Game.Objects;

namespace Mdk.Game.Scripts;

/// <summary>How hard Kurt's hits are: as the original's, or all an enemy's health (the console's onehit).</summary>
public enum Lethality { Normal, OneHit }

public sealed partial class ScriptRuntime
{
    /// <summary>Enemies whose scripts count hits through their health (puzzles): LEVEL8's forklift
    /// (driver off below 1000, then pushed by each hit) and XEARTH (sinks after 720 counted hits).</summary>
    private static readonly HashSet<string> ScriptedTargets = ["XEARTH", "XFORK"];

    public Lethality Lethality { get; set; } = Lethality.Normal;

    /// <summary>The damage of Kurt's hit on an object.</summary>
    public int KurtDamage(MdkObject obj, int damage) => KurtDamage(obj, damage, obj.Health);

    /// <summary>The damage of Kurt's hit on an object or its part with <paramref name="health"/> left:
    /// with onehit, at least all of an enemy's (a hit of 1 on a grunt of 20 takes 20).</summary>
    public int KurtDamage(MdkObject obj, int damage, int health)
    {
        if (Lethality == Lethality.Normal || damage <= 0 || !IsOneHitTarget(obj))
        {
            return damage;
        }

        return Math.Max(damage, health);
    }

    private static bool IsOneHitTarget(MdkObject obj) =>
        GameStats.IsEnemy(obj.TypeName) && !ScriptedTargets.Contains(obj.TypeName.ToUpperInvariant());
}
