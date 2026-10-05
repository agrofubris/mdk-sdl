namespace Mdk.Game.Scripts;

/// <summary>The level's counts for the Score-O-matic and what carries over between levels (the
/// Godot port's GameState).</summary>
// TODO port game_state.gd
public sealed class GameStats
{
    /// <summary>LEVELn numbers in the order of play.</summary>
    private static readonly int[] Order = [7, 6, 3, 4, 8, 5];

    /// <summary>The object types that count as enemies (0x491c38).</summary>
    private static readonly HashSet<string> EnemyTypes =
    [
        "XB", "XB1", "XB2", "XB3", "XBSHARK", "XBSHIP", "XBT", "XC", "XCARGO", "XD", "XD6GUN", "XE", "XEARTH", "XF",
        "XFORK", "XG", "XG_BOMB1", "XGEN", "XGHTARG", "XGSNOW", "XGSMOKE", "XG_MISS", "XGTARG", "XGUNTA", "XM3",
        "XMART", "XPER", "XS", "XT", "XTANK", "XTGUN", "XTUR", "XU",
    ];

    public int Shots;
    public int ShotHits;
    public int SniperShots;
    public int SniperHits;
    public int HeadShots;
    public int Enemies;
    public int Kills;
    /// <summary>The global flags when the level ended (which towns were flattened).</summary>
    public int TownFlags;
    public bool StrikeUsed;

    /// <summary>An enemy created (0x43bc20) or killed by Kurt (0x43357c).</summary>
    public void CountEnemy(string type, Kill kill)
    {
        if (!EnemyTypes.Contains(type.ToUpperInvariant()))
        {
            return;
        }

        if (kill == Kill.Killed)
        {
            Kills++;
        }
        else
        {
            Enemies++;
        }
    }

    public enum Kill { Created, Killed }

    /// <summary>The index (0-5) of a LEVELn number in the order of play.</summary>
    public static int IndexOf(int level) => Array.IndexOf(Order, level);
}
