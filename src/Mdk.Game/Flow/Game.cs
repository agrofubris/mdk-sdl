using System.Diagnostics;
using Mdk.Engine.Audio;
using Mdk.Engine.Platform;
using Mdk.Engine.Render;
using Mdk.Formats;
using Mdk.Game.Menu;

namespace Mdk.Game.Flow;

/// <summary>How the game starts (command line): the screen shown first.</summary>
public enum Start { Menu, Level, Statistics, Briefing, EndMovie }

/// <summary>The command line's choices: where the game starts, the first level's test options, the
/// statistics' test counts, a save to load or write, a screenshot.</summary>
public sealed record GameOptions(Start Start, ViewerOptions Level)
{
    public MenuPage Page { get; init; }
    /// <summary>The menu shows the splash first even when it's started directly.</summary>
    public bool Splash { get; init; }
    /// <summary>The statistics start at this page.</summary>
    public StatsScreen.Phase? Phase { get; init; }
    /// <summary>The statistics' counts: shots, hits, sniper rounds, sniper hits, kills, enemies, heads.</summary>
    public IReadOnlyList<int> Counts { get; init; } = [];
    /// <summary>The town flags' bits 31-29 for the debriefing.</summary>
    public int? Towns { get; init; }
    /// <summary>A save to load first, and a save written when the first level starts.</summary>
    public string? Load { get; init; }
    public string? Save { get; init; }
    /// <summary>A screen other than the first level saves this frame after <see cref="Wait"/> seconds, then the game quits.</summary>
    public string? Screenshot { get; init; }
    public float Wait { get; init; }
}

/// <summary>The game: its screens one after the other, as the original's game states.
/// <code>
///   splash ─► menu ─new game─► briefing ─[fall]─► loading ─► level ─tornado─► [stream] ─► statistics
///              ▲  └continue/save─► loading                     │                       save prompt
///              │                                               │ died: LASTGAME           briefing ─[fall]─► next level
///              └───────────────────────────────────────────────┘ event 81: end movies ─► splash ─► menu
/// </code>
/// [ ] are not ported yet (TODO hooks in <see cref="Handle"/>, the statistics and the menu).</summary>
public sealed class Game : IDisposable
{
    private const int WindowWidth = 1280;
    private const int WindowHeight = 960;
    private const string UserFolderName = "mdk-sdl";
    /// <summary>Overrides the user folder (settings and saves), for tests.</summary>
    private const string UserFolderVariable = "MDK_USER_DIR";
    private const string SavesFolder = "saves";
    private const int TownShift = 29;

    private readonly GameOptions _options;
    private readonly Window _window;
    private readonly Renderer _renderer;
    private readonly AudioDevice _audio;
    private readonly Input _input = new();
    private readonly Ui _ui;
    private readonly GameState _state = new();
    private readonly SaveGames _saves;
    private readonly Renderer.Scope _scope;
    private IScreen? _screen;
    /// <summary>The first level gets the command line's test options; later ones don't.</summary>
    private bool _firstLevel = true;
    /// <summary>The level shown takes its own screenshot (the first, with --level).</summary>
    private bool _levelShot;
    /// <summary>The level that ended, for its counts.</summary>
    private Viewer? _viewer;

    public Game(MdkData data, GameOptions options)
    {
        _options = options;
        var folder = Environment.GetEnvironmentVariable(UserFolderVariable)
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), UserFolderName);
        _saves = new SaveGames(Path.Combine(folder, SavesFolder));

        // The original deletes LASTGAME.SAV when it quits.
        _saves.Delete(SaveGames.LastGame);
        var settings = Settings.Load(Settings.PathIn(folder));
        _window = new Window("MDK", WindowWidth, WindowHeight);
        _renderer = new Renderer(_window);
        _audio = new AudioDevice(options.Level.Sound == SoundMode.Muted ? Output.Muted : Output.Speakers);
        settings.Apply(_audio, _window, _input);
        _ui = new Ui(data, _window, _renderer, _audio, _input, settings, folder);
        _scope = _renderer.Mark();
    }

    /// <summary>Runs the screens until the window closes or one quits.</summary>
    public void Run()
    {
        var first = FirstEvent();
        if (first == Event.Quit)
        {
            return;
        }

        Handle(first);
        var test = _options.Screenshot != null;
        var clock = Stopwatch.StartNew();
        var time = 0f;
        while (_screen != null && _window.PumpEvents(_input))
        {
            var elapsed = test ? Viewer.Step : (float)clock.Elapsed.TotalSeconds;
            clock.Restart();
            time += elapsed;
            var shot = test && !_levelShot && time >= _options.Wait ? _options.Screenshot : null;
            var next = _screen.Frame(elapsed, shot);
            _audio.Update(elapsed);
            if (shot != null)
            {
                Console.WriteLine($"Saved {shot}");
                return;
            }

            if (next != Event.None)
            {
                Handle(next);
            }
        }
    }

    /// <summary>The first screen from the command line (a save to load first).</summary>
    private Event FirstEvent()
    {
        ApplyTestCounts();
        if (_options.Load is { } name)
        {
            var save = _saves.Read(name);
            if (save == null)
            {
                Console.Error.WriteLine($"No saved game {name}");
                return Event.Quit;
            }

            _state.Load(save);
            Console.WriteLine($"Loaded game {name}: level {save.Level}");
            return save.Kind == SaveKind.BeforeLevel ? Event.Briefing : Event.Play;
        }

        _state.Level = _options.Level.Level;
        switch (_options.Start)
        {
            case Start.Level:
                return Event.Play;
            case Start.Statistics:
                return Event.Statistics;
            case Start.Briefing:
                return Event.Briefing;
            case Start.EndMovie:
                return Event.GameFinished;
        }

        _state.Splash = _options.Splash;
        return Event.Menu;
    }

    /// <summary>The statistics' test counts (--counts, --towns).</summary>
    private void ApplyTestCounts()
    {
        var stats = _state.Stats;
        int[] counts = [.. _options.Counts, .. Enumerable.Repeat(0, 7)];
        if (_options.Counts.Count != 0)
        {
            (stats.Shots, stats.ShotHits, stats.SniperShots, stats.SniperHits) = (counts[0], counts[1], counts[2], counts[3]);
            (stats.Kills, stats.Enemies, stats.HeadShots) = (counts[4], counts[5], counts[6]);
        }

        if (_options.Towns is { } towns)
        {
            stats.TownFlags = towns << TownShift;
        }
    }

    /// <summary>What follows a screen's event.</summary>
    private void Handle(Event next)
    {
        switch (next)
        {
            case Event.Quit:
                Show(() => null);
                break;
            case Event.Menu:
                Show(() => new MainMenu(_ui, _state, _saves, _firstLevel ? _options.Page : MenuPage.Main));
                break;
            case Event.Play:
                Play();
                break;
            case Event.Briefing:
                Show(() => new StatsScreen(_ui, _state, _saves, StatsPages.Briefing, null));
                break;
            case Event.Statistics:
                Show(() => new StatsScreen(_ui, _state, _saves, StatsPages.All, _firstLevel ? _options.Phase : null));
                break;
            case Event.LevelEnded:
                AfterLevel(Scripts.ScriptRuntime.GameOver.No);
                break;
            case Event.GameOver:
                AfterLevel(Scripts.ScriptRuntime.GameOver.Yes);
                break;
            case Event.KurtDied:
                // The level is saved as LASTGAME; the menu's "Continue" starts it again.
                _saves.Write(SaveGames.LastGame, _state.Save(SaveKind.LevelStart));
                _state.Splash = true;
                Handle(Event.Menu);
                break;
            case Event.GameFinished:
                Show(() => new EndMovie(_ui));
                _state.Splash = true;
                break;
        }

        _firstLevel = false;
    }

    /// <summary>The loading screen, then the level.</summary>
    private void Play()
    {
        Show(() => null);
        LoadingScreen.Show(_ui, _state.Level, 0f);
        Show(() => null);
        var test = _firstLevel ? _options.Level : new ViewerOptions(0, null, null, 0f, _options.Level.Sound);
        _levelShot = _firstLevel && _options.Start == Start.Level && test.Screenshot != null;
        _viewer = new Viewer(_ui, test with { Level = _state.Level }, _state);
        _screen = _viewer;
        if (_firstLevel && _options.Save is { } name && _saves.Write(name, _state.Save(SaveKind.LevelStart)))
        {
            Console.WriteLine($"Saved game {name}: level {_state.Level}");
        }
    }

    /// <summary>The level ended (the tornado is over): its counts are kept, then the statistics, the
    /// last level or the menu.</summary>
    // TODO the stream after the level (stream.gd), and the Gunter stream before LEVEL5
    private void AfterLevel(Scripts.ScriptRuntime.GameOver over)
    {
        if (_viewer != null)
        {
            _state.Stats = _viewer.Stats;
        }

        var level = _state.Level;
        switch (LevelFlow.AfterLevel(level, over))
        {
            case LevelFlow.After.Statistics:
                Handle(Event.Statistics);
                break;
            case LevelFlow.After.LastLevel:
                // LEVEL5 follows at once, after a save prompt (kind 3, "6").
                _state.Level = LevelFlow.Next(level);
                Show(() => new SavePromptScreen(_ui, _saves, _state.Save(SaveKind.LevelStart), LevelFlow.SaveName(_state.Level)));
                break;
            default:
                Handle(Event.Menu);
                break;
        }
    }

    /// <summary>The next screen, created once the last one's resources are freed.</summary>
    private void Show(Func<IScreen?> create)
    {
        _screen?.Dispose();
        _screen = null;
        _levelShot = false;
        _audio.StopAll();
        _renderer.Release(_scope);
        _screen = create();
    }

    public void Dispose()
    {
        _screen?.Dispose();
        _audio.Dispose();
        _renderer.Dispose();
        _window.Dispose();
    }
}

/// <summary>The save prompt alone (after LEVEL8), then the level.</summary>
public sealed class SavePromptScreen(Ui ui, SaveGames saves, SaveGame save, string name) : IScreen
{
    private readonly SavePrompt _prompt = new(ui, new Fonts(ui.Renderer, ui.Fti), saves, save, name);

    public Event Frame(float elapsed, string? screenshot)
    {
        ui.View.Layout(ScreenView.Fit.Inside);
        _prompt.Update(ui.Input, elapsed);
        _prompt.Draw();
        ui.Present(screenshot);
        return _prompt.Closed ? Event.Play : Event.None;
    }

    public void Dispose()
    {
    }
}
