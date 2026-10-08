using Mdk.Engine.Render;
using Mdk.Formats;
using Mdk.Game.Flow;
using Mdk.Game.Kurt;
using Mdk.Game.Menu;

namespace Mdk.Game.DevTools;

/// <summary>The console's commands on any screen (<see cref="Game"/>): those of the level go to
/// <paramref name="level"/>, the screen's level or null. Commands that leave the screen (map,
/// load, quit, a new look) set the event the game takes after the frame (<see cref="TakeNext"/>).</summary>
public sealed class GameCommands(Ui ui, GameState state, Func<ILevelTarget?> level) : ICommandTarget
{
    private Event _next = Event.None;

    public ILevelTarget? Level => level();

    /// <summary>Game time runs this much faster, on every screen.</summary>
    public float TimeScale { get; private set; } = 1f;

    /// <summary>The event a command asked for (once), <see cref="Event.None"/> if none.</summary>
    public Event TakeNext()
    {
        var next = _next;
        _next = Event.None;
        return next;
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
        _next = Event.Play;
        return true;
    }

    public bool Load(string slot)
    {
        if (SaveGames.In(ui.UserFolder).Read(slot) is not { } saved)
        {
            return false;
        }

        state.Load(saved);
        _next = saved.Kind == SaveKind.BeforeLevel ? Event.Briefing : Event.Play;
        return true;
    }

    public Switch ToggleGod()
    {
        var dev = ui.Dev;
        dev.God = Flip(dev.God);
        Level?.SetGod(dev.God);
        return dev.God;
    }

    public Switch ToggleNoclip()
    {
        var dev = ui.Dev;
        dev.Noclip = Flip(dev.Noclip);
        Level?.SetNoclip(dev.Noclip);
        return dev.Noclip;
    }

    public Switch ToggleOneHit()
    {
        var dev = ui.Dev;
        dev.OneHit = Flip(dev.OneHit);
        Level?.SetOneHit(dev.OneHit);
        return dev.OneHit;
    }

    public void SetDifficulty(Difficulty difficulty)
    {
        ui.Settings.Difficulty = difficulty;
        Level?.SetDifficulty(difficulty);
        ui.ApplySettings();
    }

    /// <summary>The level reloads as it is (a full save) in the new look, when it can be saved.</summary>
    public bool SetLook(Graphics look)
    {
        ui.Settings.Graphics = look;
        ui.ApplySettings();
        if (Level is not { } played || played.Snapshot() is not { } snapshot)
        {
            return false;
        }

        state.Level = played.Where().Level;
        state.Snapshot = snapshot;
        _next = Event.Play;
        return true;
    }

    public void SetAntiAliasing(AntiAliasing antiAliasing)
    {
        ui.Settings.AntiAliasing = antiAliasing;
        ui.ApplySettings();
    }

    public void SetTimeScale(float scale) => TimeScale = scale;

    public Switch ToggleOverlay() => ui.Dev.ToggleOverlay();

    public void Quit() => _next = Event.Quit;

    public IReadOnlyList<string> ModList()
    {
        ui.ScanMods();
        return [.. ui.Mods.Mods.Select(m => $"{m.Name} ({m.Folder}) {ui.Settings.StateOf(m.Folder).ToString().ToLowerInvariant()}, priority {m.Priority}")];
    }

    private static Switch Flip(Switch value) => value == Switch.On ? Switch.Off : Switch.On;
}
