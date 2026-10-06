using Mdk.Game.Scripts;

namespace Mdk.Game.Flow;

/// <summary>What follows a level (the original's states, main loop 0x401cb8; see godot-mdk
/// docs/gameplay.md "Level flow"):
/// <code>
///   index 0-3: tornado ─► [stream] ─► statistics, save prompt, next briefing ─► [fall] ─► next level
///   index 4:   tornado ─► [Gunter stream] ─► save prompt ─► LEVEL5 (no statistics, no fall)
///   index 5:   event 81 ─► end movies ─► menu
///   game over (the town flattened) or a level outside the order ─► menu
/// </code>
/// [ ] are not ported yet.</summary>
public static class LevelFlow
{
    public enum After { Menu, Statistics, LastLevel }

    /// <summary>The last level index followed by the statistics.</summary>
    private const int LastStatisticsIndex = 3;

    public static After AfterLevel(int level, ScriptRuntime.GameOver over)
    {
        var index = GameState.IndexOf(level);
        if (over == ScriptRuntime.GameOver.Yes || index < 0 || index + 1 >= GameState.Order.Length)
        {
            return After.Menu;
        }

        return index <= LastStatisticsIndex ? After.Statistics : After.LastLevel;
    }

    /// <summary>The level after <paramref name="level"/> in the order of play (the last stays).</summary>
    public static int Next(int level)
    {
        var index = Math.Clamp(GameState.IndexOf(level) + 1, 0, GameState.Order.Length - 1);
        return GameState.Order[index];
    }

    /// <summary>The name the save prompt offers: the level's number in the order of play (1-6).</summary>
    public static string SaveName(int level) => (GameState.IndexOf(level) + 1).ToString();
}
