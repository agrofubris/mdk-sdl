using Mdk.Game.Scripts;

namespace Mdk.Game.Flow;

/// <summary>What the game keeps between its screens (game_state.gd): the level to play, the deaths,
/// the air strike, the last level's counts, the splash and the debug keys.</summary>
public sealed class GameState
{
    /// <summary>The order the levels are played in (0x490030): the level index (0x574268, 0-5) picks
    /// the <c>TRAVERSE/LEVELn</c> directory. Gunter's level, LEVEL5, is the last.</summary>
    public static readonly int[] Order = [7, 6, 3, 4, 8, 5];

    /// <summary>LEVELn to play (3-8).</summary>
    public int Level = Order[0];
    /// <summary>Deaths so far (0x574407).</summary>
    public int Deaths;
    /// <summary>The one air strike of the last two levels was used (0x57440b).</summary>
    public bool StrikeUsed;
    /// <summary>The last level's counts and town flags, for the statistics.</summary>
    public GameStats Stats = new();
    /// <summary>The <c>INTRO1A</c> splash shows when the main menu opens next: at start, after Kurt
    /// died and after the end movies.</summary>
    public bool Splash = true;
    /// <summary>The cheat <c>SEETHEWHOLEGAME</c> (0x5742bc): keys 3-8 in the main menu start that
    /// level, D the statistics with random counts.</summary>
    public bool DebugKeys;
    /// <summary>What the fall hands on to the next level: Kurt's health and the pickups he took.</summary>
    public FallCarry? Carry;
    /// <summary>The level's state of a full save being loaded (JSON), until the level restores it.</summary>
    public string? Snapshot;

    /// <summary>The index (0-5) of a LEVELn number in the order of play, -1 if none.</summary>
    public static int IndexOf(int level) => Array.IndexOf(Order, level);

    /// <summary>A new game from the level at <paramref name="index"/> of the order.</summary>
    public void NewGame(int index)
    {
        Level = Order[index];
        Deaths = 0;
        StrikeUsed = false;
    }

    /// <summary>The state of a save: its level, deaths and air strike.</summary>
    public void Load(SaveGame save)
    {
        Level = save.Level;
        Deaths = save.Deaths;
        StrikeUsed = save.StrikeUsed;
        Snapshot = save.Kind == SaveKind.Snapshot ? save.State : null;
    }

    /// <summary>LEVEL5 after the Gunter stream: Kurt keeps the stream's health, not the pickups
    /// (0x4325b0 clears the inventory).</summary>
    public void EnterLastLevel(int health)
    {
        Level = LevelFlow.Next(Level);
        Carry = new FallCarry(health, []);
    }

    /// <summary>A light save of the level to play (health 100, as the original's).</summary>
    public SaveGame Save(SaveKind kind) =>
        new(kind, Level, SaveGame.FullHealth, Deaths, StrikeUsed, DateTime.Now.ToString("s"));
}

/// <summary>Kurt's health and the pickups (model names, in order) he took in the fall.</summary>
public sealed record FallCarry(int Health, IReadOnlyList<string> Pickups);
