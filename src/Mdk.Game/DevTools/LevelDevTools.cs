using Mdk.Engine.Platform;
using Mdk.Game.Menu;
using Mdk.Game.Scripts;

namespace Mdk.Game.DevTools;

/// <summary>The developer tools of a level: the console (Grave) with its commands, and the debug
/// overlay (F3). While the console is open the game runs on without the keys.
/// <code>
///   Input ──► Grave, F3 ──► console / overlay
///         └─► console open? ──► console's line : the game's keys
///   frame ──► overlay text, console ──► canvas
/// </code></summary>
public sealed class LevelDevTools
{
    private readonly Ui _ui;
    private readonly Kurt.Kurt _kurt;
    private readonly ScriptRuntime _scripts;
    private readonly DevConsole _console;
    private readonly ConsoleView _consoleView;
    private readonly OverlayView _overlayView;
    private bool _optionRan;

    public LevelDevTools(Ui ui, LevelCommands commands, Kurt.Kurt kurt, ScriptRuntime scripts)
    {
        _ui = ui;
        _kurt = kurt;
        _scripts = scripts;
        Commands = commands;
        var registry = new CommandRegistry();
        ConsoleCommands.Register(registry, commands);
        _console = new DevConsole(registry, ui.Dev.History, ui.Dev.Log, Console.WriteLine);
        var text = new DevTextView(ui.Renderer, new Fonts(ui.Renderer, ui.Fti).Small);
        _consoleView = new ConsoleView(text);
        _overlayView = new OverlayView(text);
    }

    public LevelCommands Commands { get; }

    /// <summary>The frame's keys: returns <see cref="ConsoleState.Open"/> when the console had them.</summary>
    public ConsoleState Update(Input input, float elapsed)
    {
        if (input.WasPressed(RawKey.F3))
        {
            _ui.Dev.ToggleOverlay();
        }

        return _console.Update(input, elapsed);
    }

    /// <summary>The tests' --console: the console opens and runs these lines, once.</summary>
    public void RunOnce(string? lines)
    {
        if (lines == null || _optionRan)
        {
            return;
        }

        _optionRan = true;
        _console.Toggle();
        _console.Run(lines);
    }

    /// <summary>The overlay and the console on the canvas (before the frame is presented).</summary>
    public void Draw()
    {
        if (_ui.Dev.Overlay == Switch.On)
        {
            var sample = new OverlaySample(_ui.Dev.Profiler, _ui.Renderer.Stats, MemorySample.Now(), Commands.Where(),
                _scripts.Objects.Count, _kurt.Current.ToString());
            _overlayView.Draw(OverlayText.Lines(sample), ConsoleView.Bottom(_console));
        }

        _consoleView.Draw(_console, _ui.Dev.Log);
    }
}
