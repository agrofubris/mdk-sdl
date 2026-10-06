using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Formats;
using Mdk.Game.Collision;
using Mdk.Game.Flow;
using Mdk.Game.Kurt;
using Mdk.Game.Level;
using Mdk.Game.Menu;
using Mdk.Game.Scripts;

namespace Mdk.Game.DevTools;

/// <summary>The console's commands on the level being played (<see cref="Viewer"/>). Commands that
/// leave the level (map, load, quit, a new look) set <see cref="Next"/>, which the viewer returns.</summary>
public sealed class LevelCommands(Ui ui, GameState state, LevelData level, Kurt.Kurt kurt, ScriptRuntime scripts, ArenaSpace space,
    Func<string, bool> save) : ICommandTarget
{
    /// <summary>The game's event after this frame (<see cref="Event.None"/>: the level goes on).</summary>
    public Event Next { get; private set; } = Event.None;
    /// <summary>Game time runs this much faster.</summary>
    public float TimeScale { get; private set; } = 1f;

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

    public bool Map(int number, string arena)
    {
        var known = GameState.IndexOf(number) >= 0 || (BetaDemo.IsBeta(number) && ui.Beta != null);
        if (!known)
        {
            return false;
        }

        state.Level = number;
        state.StartArena = arena.Length != 0 ? arena : null;
        (state.Snapshot, state.Carry) = (null, null);
        Next = Event.Play;
        return true;
    }

    public Switch ToggleGod()
    {
        kurt.Mortality = kurt.Mortality == Mortality.God ? Mortality.Mortal : Mortality.God;
        return kurt.Mortality == Mortality.God ? Switch.On : Switch.Off;
    }

    public Switch ToggleNoclip()
    {
        kurt.Clipping = kurt.Clipping == Clipping.Off ? Clipping.On : Clipping.Off;
        return kurt.Clipping == Clipping.Off ? Switch.On : Switch.Off;
    }

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

    public bool Load(string slot)
    {
        if (SaveGames.In(ui.UserFolder).Read(slot) is not { } saved)
        {
            return false;
        }

        state.Load(saved);
        Next = saved.Kind == SaveKind.BeforeLevel ? Event.Briefing : Event.Play;
        return true;
    }

    public void SetDifficulty(Difficulty difficulty)
    {
        ui.Settings.Difficulty = difficulty;
        kurt.Inventory.Difficulty = difficulty;
        ui.ApplySettings();
    }

    /// <summary>The level reloads as it is (a full save) in the new look, when it can be saved.</summary>
    public bool SetLook(Graphics look)
    {
        ui.Settings.Graphics = look;
        ui.ApplySettings();
        if (!scripts.CanSnapshot())
        {
            return false;
        }

        state.Level = level.Number;
        state.Snapshot = scripts.Capture();
        Next = Event.Play;
        return true;
    }

    public void SetAntiAliasing(AntiAliasing antiAliasing)
    {
        ui.Settings.AntiAliasing = antiAliasing;
        ui.ApplySettings();
    }

    public void SetTimeScale(float scale) => TimeScale = scale;

    public Switch ToggleOverlay() => ui.Dev.ToggleOverlay();

    public void Quit() => Next = Event.Quit;
}
