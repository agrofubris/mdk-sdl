using System.Numerics;
using Mdk.Game.Collision;
using Mdk.Game.Flow;
using Mdk.Game.Kurt;
using Mdk.Game.Level;
using Mdk.Game.Scripts;

namespace Mdk.Game.DevTools;

/// <summary>The console's commands on the level being played (<see cref="Viewer"/>).</summary>
public sealed class LevelCommands(LevelData level, Kurt.Kurt kurt, ScriptRuntime scripts, ArenaSpace space,
    Func<string, bool> save) : ILevelTarget
{
    public Placement Where() => new(level.Number, scripts.CurrentArena, kurt.Feet, kurt.Yaw);

    public bool Teleport(string arena, Vector3? point)
    {
        if (point is not { } at)
        {
            return ArenaStops.Find(level, space, arena) is { } stop && Enter(arena, stop);
        }

        var target = arena.Length != 0 ? arena : space.ArenaAt(at) ?? scripts.CurrentArena;
        return level.IsReachable(target) && level.Arenas.Any(a => a.Name == target) && Enter(target, at);
    }

    private bool Enter(string arena, Vector3 at)
    {
        scripts.TeleportKurt(arena, at, kurt.Yaw);
        return true;
    }

    public void SetGod(Switch god) => kurt.Mortality = god == Switch.On ? Mortality.God : Mortality.Mortal;

    public void SetNoclip(Switch noclip) => kurt.Clipping = noclip == Switch.On ? Clipping.Off : Clipping.On;

    public bool Give(string pickup) => kurt.Collect(pickup).Length != 0;

    public void SetHealth(int health) => kurt.SetHealth(health);

    public int KillEnemies()
    {
        var enemies = scripts.Objects
            .Where(o => !o.Dead && o.Health > 0 && o.Arena == scripts.CurrentArena && GameStats.IsEnemy(o.TypeName))
            .ToList();
        foreach (var enemy in enemies)
        {
            scripts.Kill(enemy);
        }

        return enemies.Count;
    }

    public bool Save(string slot) => scripts.CanSnapshot() && save(slot);

    public void SetDifficulty(Difficulty difficulty) => kurt.Inventory.Difficulty = difficulty;

    public string? Snapshot() => scripts.CanSnapshot() ? scripts.Capture() : null;
}
