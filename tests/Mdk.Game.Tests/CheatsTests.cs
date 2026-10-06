using Mdk.Game.Flow;

namespace Mdk.Game.Tests;

/// <summary>Letters typed in a level (main.gd _type).</summary>
public class CheatsTests
{
    /// <summary>Types every letter; returns the last letter's cheat.</summary>
    private static Cheats.Cheat TypeAll(Cheats cheats, string text)
    {
        var cheat = Cheats.Cheat.None;
        foreach (var letter in text)
        {
            cheat = cheats.Type(letter);
        }

        return cheat;
    }

    [Fact]
    public void GoreCheatLastsTheSession()
    {
        var settings = new Settings();
        var state = new GameState();

        // Typed in one level, it holds in the next (the settings live as long as the game).
        Assert.Equal(Cheats.Cheat.Gore, TypeAll(new Cheats(settings, state), "XTOOSCARYFORME"));
        Assert.False(settings.Gore);

        Assert.Equal(Cheats.Cheat.Gore, TypeAll(new Cheats(settings, state), "TOOSCARYFORME"));
        Assert.True(settings.Gore);
    }

    [Fact]
    public void DebugCheatTogglesTheDebugKeys()
    {
        var state = new GameState();
        Assert.Equal(Cheats.Cheat.DebugKeys, TypeAll(new Cheats(new Settings(), state), "SEETHEWHOLEGAME"));
        Assert.True(state.DebugKeys);
    }
}
