namespace Mdk.Game.Flow;

/// <summary>Cheats typed in a level (0x42c5f0, main.gd _type): <c>TOOSCARYFORME</c> turns gore on or
/// off for the rest of the game (the settings, not saved), <c>SEETHEWHOLEGAME</c> the main menu's
/// debug keys.</summary>
public sealed class Cheats(Settings settings, GameState state)
{
    public enum Cheat { None, Gore, DebugKeys }

    private const string GoreCheat = "TOOSCARYFORME";
    private const string DebugCheat = "SEETHEWHOLEGAME";

    /// <summary>The last letters typed, as long as the longest cheat.</summary>
    private string _typed = "";

    /// <summary>One more letter (A-Z); returns the cheat it completes, applied.</summary>
    public Cheat Type(char letter)
    {
        _typed = (_typed + letter)[Math.Max(0, _typed.Length + 1 - DebugCheat.Length)..];
        if (_typed.EndsWith(GoreCheat, StringComparison.Ordinal))
        {
            _typed = "";
            settings.Gore = !settings.Gore;
            return Cheat.Gore;
        }

        if (_typed.EndsWith(DebugCheat, StringComparison.Ordinal))
        {
            _typed = "";
            state.DebugKeys = !state.DebugKeys;
            return Cheat.DebugKeys;
        }

        return Cheat.None;
    }
}
