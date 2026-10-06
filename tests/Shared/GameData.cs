using Mdk.Formats;

namespace Mdk.Tests;

/// <summary>Whether the original game's files are installed. They are never in the repository
/// (nor on CI), so tests reading them are skipped without them.</summary>
internal static class GameData
{
    public const string Missing = "MDK data not found: install MDK or set MDK_DATA_DIR";

    public static readonly bool Present = MdkData.Find() != null;
}

/// <summary>A fact reading the game's files: skipped when they are missing.</summary>
public sealed class DataFactAttribute : FactAttribute
{
    public DataFactAttribute()
    {
        if (!GameData.Present)
        {
            Skip = GameData.Missing;
        }
    }
}

/// <summary>A theory reading the game's files: skipped when they are missing.</summary>
public sealed class DataTheoryAttribute : TheoryAttribute
{
    public DataTheoryAttribute()
    {
        if (!GameData.Present)
        {
            Skip = GameData.Missing;
        }
    }
}

/// <summary>Whether the 1996 beta demo is installed (godot-mdk docs/beta96.md; <c>MDK_BETA_DIR</c>).</summary>
internal static class BetaData
{
    public const string Missing = "The 1996 beta demo not found: set MDK_BETA_DIR";

    public static readonly BetaDemo? Demo = BetaDemo.Find(MdkData.Find());
}

/// <summary>A fact reading the 1996 demo's files: skipped when it's missing.</summary>
public sealed class BetaFactAttribute : FactAttribute
{
    public BetaFactAttribute()
    {
        if (BetaData.Demo == null)
        {
            Skip = BetaData.Missing;
        }
    }
}
