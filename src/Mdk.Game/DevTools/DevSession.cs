using Mdk.Engine.Diagnostics;

namespace Mdk.Game.DevTools;

/// <summary>What the developer tools keep for the whole session: the log the console shows (the
/// program's output), the frame timings, the console's history, whether the overlay is shown, and
/// god mode, noclip and onehit (every level's Kurt gets them).</summary>
public sealed class DevSession
{
    public LogRing Log { get; } = Engine.Diagnostics.Log.Capture(Engine.Diagnostics.Log.Lines);
    public Profiler Profiler { get; } = new();
    public CommandHistory History { get; } = new();
    public Switch Overlay { get; set; } = Switch.Off;
    public Switch God { get; set; } = Switch.Off;
    public Switch Noclip { get; set; } = Switch.Off;
    public Switch OneHit { get; set; } = Switch.Off;

    public Switch ToggleOverlay() => Overlay = Overlay == Switch.On ? Switch.Off : Switch.On;

    /// <summary>A menu asked for the console (phones have no Grave key); the game opens it next frame.</summary>
    public bool ConsoleAsked { get; set; }
}
