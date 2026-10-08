using Mdk.Game.Objects;
using static Mdk.Game.Scripts.ScriptMath;

namespace Mdk.Game.Scripts;

/// <summary>How hard Kurt's hits are: as the original's, or all the target's health (the console's onehit).</summary>
public enum Lethality { Normal, OneHit }

public sealed partial class ScriptRuntime
{
    /// <summary>What onehit leaves an object until its script has run.</summary>
    private const int OneHitLeft = 1;

    public Lethality Lethality { get; set; } = Lethality.Normal;

    /// <summary>The damage of Kurt's hit on a target (object, part, group) with <paramref name="health"/>
    /// left: with onehit, at least all of it (a hit of 1 on a grunt of 20 takes 20). Groups' health is
    /// their scripts' to count: they take <see cref="Indestructible"/>.</summary>
    public int KurtDamage(int damage, int health) =>
        Lethality == Lethality.OneHit && damage > 0 ? Math.Max(damage, health) : damage;

    /// <summary>The damage of Kurt's hit on an object. With onehit, a hit the object would survive
    /// leaves it 1 hit point, and it dies once its script has run unless the script gave health
    /// back: LEVEL8's forklift resets its health on each hit, and the shots push it.</summary>
    public int KurtDamage(MdkObject obj, int damage)
    {
        if (Lethality == Lethality.Normal || damage <= 0 || damage >= obj.Health)
        {
            return damage;
        }

        if (!_oneHits.Contains(obj))
        {
            _oneHits.Add(obj);
        }

        return obj.Health - OneHitLeft;
    }

    private List<MdkObject> _oneHits = [];
    private List<MdkObject> _oneHitChecks = [];

    /// <summary>Before the objects run: the onehit hits so far wait for their scripts.</summary>
    private void HoldOneHits() => (_oneHits, _oneHitChecks) = (_oneHitChecks, _oneHits);

    /// <summary>After the objects ran: the onehit targets their scripts didn't heal die.</summary>
    private void EndOneHits()
    {
        foreach (var obj in _oneHitChecks)
        {
            if (obj.Dead || obj.Health != OneHitLeft)
            {
                continue;
            }

            obj.Health = 0;
            Stats.CountEnemy(obj.TypeName, GameStats.Kill.Killed);
            Kill(obj, obj.HitDirection + HalfTurn);
        }

        _oneHitChecks.Clear();
    }
}
