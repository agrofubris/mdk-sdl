namespace Mdk.Game.Scripts;

/// <summary>How hard Kurt's hits are: as the original's, or all the target's health (the console's onehit).</summary>
public enum Lethality { Normal, OneHit }

public sealed partial class ScriptRuntime
{
    public Lethality Lethality { get; set; } = Lethality.Normal;

    /// <summary>The damage of Kurt's hit on a target (object, part, group) with <paramref name="health"/>
    /// left: with onehit, at least all of it (a hit of 1 on a grunt of 20 takes 20). Groups' health is
    /// their scripts' to count: they take <see cref="Indestructible"/>.</summary>
    public int KurtDamage(int damage, int health) =>
        Lethality == Lethality.OneHit && damage > 0 ? Math.Max(damage, health) : damage;
}
