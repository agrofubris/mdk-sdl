using System.Diagnostics;
using Mdk.Engine.Audio;
using Mdk.Engine.Diagnostics;
using Mdk.Engine.Platform;
using Mdk.Engine.Render;
using Mdk.Formats;
using Mdk.Game.DevTools;
using Mdk.Game.HdTextures;
using Mdk.Game.Hud;
using Mdk.Game.Menu;

namespace Mdk.Game.Flow;

/// <summary>How the game starts (command line): the screen shown first.</summary>
public enum Start { Menu, Level, Statistics, Briefing, EndMovie, Stream, Fall }

/// <summary>The command line's choices: where the game starts, the first level's test options, the
/// statistics' test counts, a save to load or write, a screenshot.</summary>
/// <summary>Whether the game shows its window.</summary>
public enum Display { Window, Hidden }

public sealed record GameOptions(Start Start, ViewerOptions Level)
{
    /// <summary>Hidden: no window is shown or focused, frames are drawn off screen (tests).</summary>
    public Display Display { get; init; }
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
    /// <summary>Kurt's health in a stream started directly (--stream).</summary>
    public int? Health { get; init; }
    /// <summary>The look instead of the settings' (--enhanced, --original; tests).</summary>
    public Graphics? Graphics { get; init; }
    /// <summary>Gore instead of the settings' (the original's -bloodyes, -nobloodno).</summary>
    public bool? Gore { get; init; }
    /// <summary>Keys pressed once at given times (--press; tests).</summary>
    public TestPresses? Presses { get; init; }
    /// <summary>Measure the frames after this many seconds of game time, waiting for the GPU each
    /// frame, and print their costs at the end (--perf; tests).</summary>
    public float? Perf { get; init; }
}

/// <summary>The game: its screens one after the other, as the original's game states.
/// <code>
///   splash ─► menu ─new game─► briefing ──fall──► loading ─► level ─tornado─► stream ─► statistics
///              ▲  └continue/save─► loading                     │                       save prompt
///              │                                               │ died: LASTGAME           briefing ──fall──► next level
///              └───────────────────────────────────────────────┘ event 81: end movies ─► splash ─► menu
/// </code></summary>
public sealed class Game : IDisposable
{
    private const int WindowWidth = 1280;
    private const int WindowHeight = 960;
    /// <summary>Where older builds kept settings and saves, under the local data folder.</summary>
    private const string UserFolderName = "mdk-sdl";
    /// <summary>The saves' folder in the user folder (<see cref="SaveGames"/>).</summary>
    private const string SavesFolder = "saves";
    /// <summary>Overrides the user folder (settings and saves), for tests.</summary>
    private const string UserFolderVariable = "MDK_USER_DIR";
    private const int TownShift = 29;
    /// <summary>The soak test's frames are this many game steps (0.1 s): it runs faster.</summary>
    private const int SoakSteps = 6;
    /// <summary>The overlay's line between screens (the loading screen).</summary>
    private const string Loading = "loading";
    /// <summary>How often --upscale-textures prints its progress (ms).</summary>
    private const int UpscalePoll = 1000;

    private readonly GameOptions _options;
    private readonly Window _window;
    private readonly Renderer _renderer;
    private readonly AudioDevice _audio;
    private readonly Input _input = new();
    private readonly Ui _ui;
    private readonly GameState _state = new();
    private readonly SaveGames _saves;
    private readonly Renderer.Scope _scope;
    /// <summary>The console and the overlay, over every screen.</summary>
    private readonly DevUi _dev;
    private readonly GameCommands _commands;
    /// <summary>The soak test's random keys, on every screen; menu keys too when it starts in the
    /// menus or the flow screens.</summary>
    private readonly SoakKeys? _soak;
    private readonly bool _soakMenus;
    private IScreen? _screen;
    /// <summary>The first level gets the command line's test options; later ones don't.</summary>
    private bool _firstLevel = true;
    /// <summary>The level shown takes its own screenshot (the first, with --level).</summary>
    private bool _levelShot;
    /// <summary>The level that ended, for its counts.</summary>
    private Viewer? _viewer;
    private readonly PerfLog? _perf;

    public Game(MdkData data, GameOptions options)
    {
        _options = options;
        var folder = Environment.GetEnvironmentVariable(UserFolderVariable) ?? UserFolder();
        _saves = SaveGames.In(folder);

        // The original deletes LASTGAME.SAV when it quits.
        _saves.Delete(SaveGames.LastGame);
        var settings = Settings.Load(Settings.PathIn(folder));
        _window = new Window("MDK", WindowWidth, WindowHeight, options.Display == Display.Hidden ? Visibility.Hidden : Visibility.Shown);
        _renderer = new Renderer(_window);
        _audio = new AudioDevice(options.Level.Sound == SoundMode.Muted ? Output.Muted : Output.Speakers);
        // Tests choose the look (saved only if the options change).
        if (options.Graphics is { } graphics)
        {
            settings.Graphics = graphics;
        }

        if (options.Gore is { } gore)
        {
            settings.Gore = gore;
        }

        settings.Apply(_audio, _window, _renderer, _input);
        _ui = new Ui(data, _window, _renderer, _audio, _input, settings, folder);

        // The developer tools draw over whatever screen presents; their font outlives the screens.
        _commands = new GameCommands(_ui, _state, () => (_screen as Viewer)?.Commands);
        var registry = new CommandRegistry();
        ConsoleCommands.Register(registry, _commands);
        _dev = new DevUi(new DevConsole(registry, _ui.Dev.History, _ui.Dev.Log, Console.WriteLine), () => _ui.Dev.ToggleOverlay());
        var small = new Fonts(_renderer, _ui.Fti).Small;
        var devView = new DevUiView(_renderer, small);
        // The on-screen controls of a touch screen (Android), under the developer tools.
        var touchView = new TouchView(_renderer, small);
        // Made once: a lambda made in the overlay's would be made every frame.
        Func<IReadOnlyList<string>> status = () => _screen?.Status ?? [Loading];
        _renderer.Overlay = () =>
        {
            touchView.Draw(_window);
            devView.Draw(_dev, _ui.Dev, status);
        };
        _scope = _renderer.Mark();
        _soak = options.Level.Soak is { } seed ? new SoakKeys(seed) : null;
        _soakMenus = options.Start is Start.Menu or Start.Statistics or Start.Briefing or Start.EndMovie;
        if (options.Perf is { } warmup)
        {
            _perf = new PerfLog(warmup);
            _renderer.Sync = GpuSync.Wait;
        }
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
        var test = _options.Screenshot != null || _options.Level.Frames != null;
        var step = _soak != null ? Viewer.Step * SoakSteps : Viewer.Step;
        var clock = Stopwatch.StartNew();
        var time = 0f;
        while (_screen != null && _window.PumpEvents(_input))
        {
            var elapsed = test ? step : (float)clock.Elapsed.TotalSeconds;
            clock.Restart();
            time += elapsed;
            _soak?.Hold(_input, time);
            if (_soakMenus)
            {
                _soak?.PressMenu(_input, time);
            }

            _options.Presses?.Apply(_input, time);

            // The open console has the keys: the screen runs on without them.
            _dev.Update(_input, elapsed);
            if (_screen is not Viewer { ConsoleReady: false })
            {
                _dev.RunOnce(_options.Level.Console);
            }

            var shot = test && !_levelShot && time >= _options.Wait ? _options.Screenshot : null;
            var next = _screen.Frame(elapsed * _commands.TimeScale, shot);

            // A console command leaves the screen (map, load, quit).
            var command = _commands.TakeNext();
            if (command != Event.None)
            {
                next = command;
            }

            using (_ui.Dev.Profiler.Measure(Section.Audio))
            {
                _audio.Update(elapsed);
            }

            _ui.Dev.Profiler.EndFrame();
            _perf?.Frame(time, _ui.Dev.Profiler, _renderer.Stats);
            if (shot != null)
            {
                Console.WriteLine($"Saved {shot}");
                break;
            }

            if (next != Event.None)
            {
                Handle(next);
            }
        }

        foreach (var line in _perf?.Report() ?? [])
        {
            Console.WriteLine(line);
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
            case Start.Stream:
                return Event.Stream;
            case Start.Fall:
                return Event.Fall;
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
                // The level is saved as LASTGAME; the menu's "Continue" starts it again (not the 1996 demo's).
                if (!BetaDemo.IsBeta(_state.Level))
                {
                    _saves.Write(SaveGames.LastGame, _state.Save(SaveKind.LevelStart));
                }

                _state.Splash = true;
                Handle(Event.Menu);
                break;
            case Event.GameFinished:
                Show(() => new EndMovie(_ui));
                _state.Splash = true;
                break;
            case Event.Stream:
                ShowStream(_options.Health ?? SaveGame.FullHealth);
                break;
            case Event.Fall:
                Show(() => new Fall.FallScreen(_ui, _state, _firstLevel ? _options.Level : new ViewerOptions(0, null, null, 0f, _options.Level.Sound)));
                break;
            case Event.StreamEnded:
                AfterStream();
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
        var loading = Stopwatch.StartNew();
        _viewer = new Viewer(_ui, test with { Level = _state.Level }, _state);
        _perf?.Loaded(loading.Elapsed);
        _screen = _viewer;
        if (_firstLevel && _options.Save is { } name && _saves.Write(name, _state.Save(SaveKind.LevelStart)))
        {
            Console.WriteLine($"Saved game {name}: level {_state.Level}");
        }
    }

    /// <summary>The level ended (the tornado is over): its counts are kept, then the stream or the menu.</summary>
    private void AfterLevel(Scripts.ScriptRuntime.GameOver over)
    {
        if (_viewer != null)
        {
            _state.Stats = _viewer.Stats;
        }

        if (LevelFlow.AfterLevel(_state.Level, over) == LevelFlow.After.Menu)
        {
            Handle(Event.Menu);
            return;
        }

        ShowStream(_viewer?.Health ?? SaveGame.FullHealth);
    }

    /// <summary>The stream after the level, with Kurt's health. Started directly (--stream) its tube
    /// is the one of the C library's first seed, as tests expect.</summary>
    private void ShowStream(int health)
    {
        var direct = _firstLevel && _options.Start == Start.Stream;
        var seed = direct ? Stream.WatcomRandom.DefaultSeed : (uint)Environment.TickCount;
        Show(() => new Stream.StreamScreen(_ui, _state.Level, health, new Stream.WatcomRandom(seed)));
    }

    /// <summary>The stream is over: the statistics, or the last level.</summary>
    private void AfterStream()
    {
        var level = _state.Level;
        switch (LevelFlow.AfterLevel(level, Scripts.ScriptRuntime.GameOver.No))
        {
            case LevelFlow.After.Statistics:
                Handle(Event.Statistics);
                break;
            case LevelFlow.After.LastLevel:
                // LEVEL5 follows at once, after a save prompt (kind 3, "6"), with the stream's health.
                _state.EnterLastLevel((_screen as Stream.StreamScreen)?.Health ?? SaveGame.FullHealth);
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

    /// <summary>Makes the HD textures in the user folder without a window (--upscale-textures),
    /// printing the progress; returns the exit code.</summary>
    public static int UpscaleTextures(MdkData data, HdOptions options)
    {
        // The upscaler's builds are desktop-only.
        if (!HdMenu.CanMake(HdMenu.Current))
        {
            Console.Error.WriteLine("HD textures can't be made on this platform");
            return 1;
        }

        var folder = Environment.GetEnvironmentVariable(UserFolderVariable) ?? UserFolder();
        var progress = new HdProgress();
        var run = Task.Run(() => HdGenerator.Run(data, folder, options, progress, CancellationToken.None));
        var last = "";
        while (!run.Wait(UpscalePoll))
        {
            var line = progress.Describe();
            if (line != last)
            {
                Console.WriteLine(line);
                last = line;
            }
        }

        if (run.IsFaulted)
        {
            Console.Error.WriteLine($"HD textures failed: {run.Exception!.GetBaseException().Message}");
            return 1;
        }

        Console.WriteLine($"HD textures: {run.Result}");
        return 0;
    }

    /// <summary>Settings and saves live next to the executable (a portable install). The first run
    /// moves over what an older build kept in the local data folder.</summary>
    private static string UserFolder()
    {
        var folder = AppContext.BaseDirectory;
        var old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), UserFolderName);
        if (File.Exists(Settings.PathIn(folder)) || !File.Exists(Settings.PathIn(old)))
        {
            return folder;
        }

        File.Copy(Settings.PathIn(old), Settings.PathIn(folder));
        var oldSaves = Path.Combine(old, SavesFolder);
        if (!Directory.Exists(oldSaves))
        {
            return folder;
        }

        var saves = Directory.CreateDirectory(Path.Combine(folder, SavesFolder)).FullName;
        foreach (var file in Directory.GetFiles(oldSaves))
        {
            File.Copy(file, Path.Combine(saves, Path.GetFileName(file)), overwrite: false);
        }

        return folder;
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

    public IReadOnlyList<string> Status => ["save prompt"];

    public void Dispose()
    {
    }
}
