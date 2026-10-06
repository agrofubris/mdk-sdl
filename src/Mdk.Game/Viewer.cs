using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Mdk.Engine.Audio;
using Mdk.Engine.Platform;
using Mdk.Engine.Render;
using Mdk.Formats;
using Mdk.Game.Flow;
using Mdk.Game.Menu;
using Mdk.Game.Audio;
using Mdk.Game.Collision;
using Mdk.Game.Hud;
using Mdk.Game.Kurt;
using Mdk.Game.Level;
using Mdk.Game.Objects;
using Mdk.Game.Scripts;

namespace Mdk.Game;

/// <summary>How a session starts, and what tests make it do: hold keys for some seconds, then save
/// a frame and quit.</summary>
public sealed record ViewerOptions(int Level, Vector3? Position, float? Yaw, float Pitch, SoundMode Sound)
{
    /// <summary>Save a frame after <see cref="Wait"/> seconds of game time, print Kurt's state, and quit.</summary>
    public string? Screenshot { get; init; }
    public float Wait { get; init; }
    /// <summary>Hold "forward" this many seconds from the start.</summary>
    public float Walk { get; init; }
    /// <summary>Seconds before the held keys start (like the Godot port's --delay).</summary>
    public float Delay { get; init; }
    public bool Jump { get; init; }
    /// <summary>Hold "fire".</summary>
    public bool Fire { get; init; }
    /// <summary>Pickups Kurt starts with (SW_HBOMB...), and press "use" once he has landed.</summary>
    public IReadOnlyList<string> Give { get; init; } = [];
    public bool Use { get; init; }
    public bool Fly { get; init; }
    /// <summary>Print the scripted objects once per second of game time.</summary>
    public bool Profile { get; init; }
    /// <summary>Sniper mode once Kurt stands, at this zoom (X) and pitch (Y); see <see cref="SniperTest"/>.</summary>
    public Vector2? Sniper { get; init; }
    /// <summary>Hold "zoom in" this many seconds in sniper mode.</summary>
    public float Zoom { get; init; }
    /// <summary>Fire one sniper round once the clip is loaded.</summary>
    public bool SniperFire { get; init; }
    /// <summary>Bones' full-screen strike after a second.</summary>
    public StrikeScene.Plane? Strike { get; init; }
    /// <summary>Kurt is hurt to death after a second.</summary>
    public bool Die { get; init; }
    /// <summary>A special_event after a second (1 ends the level).</summary>
    public int? Event { get; init; }
    /// <summary>After the delay: a teleport, an object killed, a walker ridden, the XE boarded (see <see cref="RideTest"/>).</summary>
    public TeleportTarget? Teleport { get; init; }
    public string? Kill { get; init; }
    public string? Ride { get; init; }
    public BomberTest? Bomber { get; init; }
    /// <summary>At the screenshot, a full save of this name (as F2), its hash printed.</summary>
    public string? Snapshot { get; init; }
    /// <summary>The soak test's seed: random keys (<see cref="SoakKeys"/>) and checks (<see cref="SoakTest"/>) until the screenshot.</summary>
    public int? Soak { get; init; }
    public SoakRoute Route { get; init; }
}

public enum SoundMode { On, Muted }

/// <summary>Plays a level: Kurt walks it (F1 switches to a flying camera), the scripts run. Esc
/// opens the pause menu. It ends when Kurt dies, the level ends (after the tornado) or the game ends.
/// <code>
///   input ──► Kurt (60 steps/s) ──► scripts (30 ticks/s) ──► camera ──► level, objects, sky, Kurt ──► frame
///                └── mixer (listener at the camera)
/// </code></summary>
public sealed class Viewer : IScreen
{
    /// <summary>Kurt moves in fixed steps; the tests' frames are steps too.</summary>
    public const float Step = 1f / 60f;
    /// <summary>Steps caught up at most after a slow frame.</summary>
    private const int MaxSteps = 10;
    /// <summary>The start is slightly below the landing pad (the original lands Kurt by chute).</summary>
    private const float StartDrop = 3f;
    /// <summary>A pickup's name stays this long.</summary>
    private const float PickupMessageSeconds = 2f;
    /// <summary>The test's "use" is held from 1 second on, for 0.1 (game time).</summary>
    private const float UseStart = 1f;
    private const float UseTime = 0.1f;
    /// <summary>The tests' --die and --event happen after 1 second (game time).</summary>
    private const float TestStart = 1f;
    private const int FatalDamage = 1000;
    /// <summary>The game goes on this long after the level ended, longer when it's lost (main.gd).</summary>
    private const float EndDelay = 0.5f;
    private const float GameOverDelay = 5f;
    /// <summary>Cheats typed in a level (0x42c5f0): gore on or off, and the main menu's debug keys.</summary>
    private const string GoreCheat = "TOOSCARYFORME";
    private const string DebugCheat = "SEETHEWHOLEGAME";

    private readonly Ui _ui;
    private readonly ViewerOptions _options;
    private readonly GameState _state;
    private readonly Renderer _renderer;
    private readonly AudioDevice _audio;
    private readonly LevelData _level;
    private readonly LevelView _view;
    private readonly SoundMixer _mixer;
    private readonly ArenaSpace _space;
    private readonly KurtSprite _sprite;
    private readonly Kurt.Kurt _kurt;
    private readonly ScriptRuntime _scripts;
    private readonly HudView _hud;
    private readonly ObjectView _objects;
    private readonly EffectsView _effects;
    private readonly Dictionary<string, ObjectView.Look> _looks = [];
    private readonly SniperView _sniper;
    private readonly SniperTest _sniperTest;
    private readonly RideTest _rideTest;
    private readonly SoakTest? _soak;
    /// <summary>F2's name prompt, while it's open.</summary>
    private SavePrompt? _snapshotPrompt;
    private readonly FollowCamera _follow;
    private readonly FreeCamera _fly;
    private readonly PauseMenu _pause;
    private readonly bool _test;
    private bool _flying;
    private float _time;
    private float _pending;
    private float _strikeTime;
    private string _typed = "";
    private Event _next = Event.None;
    /// <summary>The level ended: the event that follows once the delay is over.</summary>
    private (Event Event, float Delay)? _ending;

    public Viewer(Ui ui, ViewerOptions options, GameState state)
    {
        var loading = Stopwatch.StartNew();
        _ui = ui;
        _options = options;
        _state = state;
        _renderer = ui.Renderer;
        _audio = ui.Audio;
        var data = ui.Data;
        _level = new LevelData(data, options.Level);
        var level = _level;
        var renderer = _renderer;
        var groups = new TriangleGroups();
        _view = new LevelView(renderer, level, groups);
        var bank = SoundBank.ForLevel(data, options.Level);
        _mixer = new SoundMixer(_audio, bank.Get);
        var cmi = Cmi.Load(data.PathOf($"TRAVERSE/LEVEL{level.Number}/LEVEL{level.Number}.CMI"));
        PlayMusic(_mixer, cmi, level);
        renderer.Panorama = CreatePanorama(renderer, level.Dti);

        _space = new ArenaSpace();
        foreach (var arena in level.Arenas.Where(a => level.IsReachable(a.Name)))
        {
            _space.Add(arena);
        }

        var sprites = Bni.Load(data.PathOf("TRAVERSE/TRAVSPRT.BNI"));
        _sprite = new KurtSprite(renderer, sprites, level.Dti.Palette);
        foreach (var name in KurtSprite.LevelAnimations)
        {
            _sprite.Add(bank.Animation(name));
        }

        _kurt = new Kurt.Kurt(_space, _mixer, _sprite.FrameCount)
        {
            Feet = options.Position ?? level.Dti.StartPosition + new Vector3(0f, 0f, StartDrop),
            Yaw = options.Yaw ?? level.Dti.StartAngle,
        };

        // The difficulty decides the town's timer: before the scripts.
        _kurt.Inventory.Difficulty = ui.Settings.Difficulty;
        _scripts = new ScriptRuntime(level, cmi, sprites, _space, groups, _mixer, _kurt, options.Soak)
        {
            Option = ui.Settings.Gore ? 1 : 0,
        };
        _scripts.AirStrike.UsedUp = state.StrikeUsed;
        _follow = new FollowCamera(_space) { ClipView = _scripts.ClipView };
        _scripts.ShakeRaised += _follow.RaiseShake;
        _kurt.Died += OnDied;
        _scripts.LevelEnded += over => _ending = over == ScriptRuntime.GameOver.Yes ? (Event.GameOver, GameOverDelay) : (Event.LevelEnded, EndDelay);
        _scripts.GameFinished += () => _next = Event.GameFinished;

        // The HUD; pickups show their names (0x46c448).
        _hud = new HudView(renderer, sprites, level.Dti.Palette, ui.Fti);
        _scripts.Messages = _hud.Messages;
        CarryFromFall(state);
        RestoreSnapshot(state);
        _kurt.Inventory.PickedUp += name => _hud.Messages.Push(name, Messages.FlagZoom, PickupMessageSeconds);

        foreach (var pickup in options.Give)
        {
            _kurt.Collect(pickup);
        }
        _scripts.ArenaEntered += _view.Enter;
        _objects = new ObjectView(renderer, new MaterialResolver(renderer, level.Dti));
        _effects = new EffectsView(renderer, new MaterialResolver(renderer, level.Dti), level);
        _sniper = new SniperView(renderer, _objects, level, _hud);
        _sniperTest = new SniperTest(options);
        _rideTest = new RideTest(options);
        _soak = options.Soak != null ? new SoakTest(level, _space, options.Route, options.Wait) : null;
        _fly = new FreeCamera { Pitch = options.Pitch };
        _flying = options.Fly;
        _pause = new PauseMenu(ui);
        Console.WriteLine($"Level {options.Level}: {level.Arenas.Count} arenas, {_view.TriangleCount} triangles, loaded in {loading.ElapsedMilliseconds} ms");

        _test = options.Screenshot != null;
        ui.Window.CaptureMouse(_test ? Capture.Off : Capture.On);
    }

    /// <summary>The counts of the level (for the statistics).</summary>
    public GameStats Stats => _scripts.Stats;

    /// <summary>Kurt's health when the level ended (the save after LEVEL8 keeps it).</summary>
    public int Health => _kurt.Health;

    /// <summary>Kurt keeps the health and the pickups of the fall (taken again, without their messages).</summary>
    private void CarryFromFall(GameState state)
    {
        if (state.Carry is not { } carry)
        {
            return;
        }

        state.Carry = null;
        foreach (var pickup in carry.Pickups)
        {
            _kurt.Collect(pickup);
        }

        _kurt.SetHealth(carry.Health);
        Console.WriteLine($"Level starts with the fall's health {carry.Health} and pickups {string.Join(",", carry.Pickups)}");
    }

    public Event Frame(float elapsed, string? screenshot)
    {
        var input = _ui.Input;

        // F2's name prompt: the game waits under it.
        if (_snapshotPrompt != null)
        {
            return AskSnapshot(elapsed, screenshot);
        }

        // The pause menu: the game waits under it.
        if (_pause.Open)
        {
            DrawScene(elapsed);
            var paused = _pause.Update(elapsed);
            _renderer.Present(SceneView(), SkyColour(_level.Dti), screenshot);
            CaptureMouse();
            return paused;
        }

        if (input.WasPressed(MenuKey.Back) && _scripts.Strike is not { Active: true })
        {
            _pause.Show();
            _ui.Window.CaptureMouse(Capture.Off);
            return Event.None;
        }

        // The full-screen strike: the game waits (Esc skips it); tests count its time.
        var skip = input.WasPressed(Key.Escape) ? SniperView.StrikeSkip.Now : SniperView.StrikeSkip.No;
        if (_sniper.DrawStrike(_scripts, skip, elapsed) is { } strikeView)
        {
            _strikeTime += elapsed;
            var shot = SavePath(_options, input, _time + _strikeTime) ?? screenshot;
            _renderer.Present(strikeView, SkyColour(_level.Dti), shot);
            if (shot != null && _test)
            {
                Console.WriteLine($"Saved {shot} (strike)");
                return Event.Quit;
            }

            return Event.None;
        }

        if (input.WasPressed(Key.Fly))
        {
            _flying = !_flying;
            _fly.Position = _follow.Position;
            _fly.Yaw = _kurt.Yaw;
        }

        // Tests ignore the real mouse.
        if (_test)
        {
            input.ClearMouse();
        }

        TypeCheats(input.Typed);
        if (input.WasPressed(MenuKey.Snapshot))
        {
            OpenSnapshot();
        }

        RunSteps(input, elapsed);
        if (_ending is { } ending)
        {
            _ending = ending with { Delay = ending.Delay - elapsed };
            if (_ending.Value.Delay <= 0f)
            {
                _next = ending.Event;
            }
        }

        var camera = DrawScene(elapsed);
        var save = SavePath(_options, input, _time) ?? screenshot;
        _renderer.Present(camera, SkyColour(_level.Dti), save);
        if (save == null)
        {
            return _next;
        }

        Console.WriteLine($"Saved {save}");
        if (!_test)
        {
            return _next;
        }

        if (_options.Snapshot is { } name)
        {
            SaveSnapshot(name);
        }

        Report(_kurt, _space);
        _soak?.Report(_scripts, _kurt, _time);
        return Event.Quit;
    }

    /// <summary>Kurt in fixed steps; the tests' keys by game time.</summary>
    private void RunSteps(Input input, float elapsed)
    {
        var options = _options;
        _pending = MathF.Min(_pending + elapsed, Step * MaxSteps);
        while (_pending >= Step)
        {
            _pending -= Step;
            _time += Step;
            if (_soak == null)
            {
                HoldTestKeys(input);
            }

            RunTests();
            _sniperTest.Step(_kurt, _scripts, input, _time);
            _rideTest.Step(_kurt, _scripts, input, _time);
            if (_flying)
            {
                _fly.Update(input, Step);
            }
            else
            {
                _kurt.Update(input, Step);
                _follow.Update(_kurt, ArenaPitch(_level, _space, _kurt.Feet), input, Step);
            }

            input.ClearMouse();
            _scripts.Update(Step);
            _space.SetSolid(_scripts.SolidArenas);
            _soak?.Step(_kurt, _scripts, _space, _time);
            if (MathF.Floor(_time * Kurt.Kurt.Ticks) > MathF.Floor((_time - Step) * Kurt.Kurt.Ticks))
            {
                _hud.Tick(HudStateOf(_kurt, _scripts));
            }

            if (options.Profile && MathF.Floor(_time) > MathF.Floor(_time - Step))
            {
                Profile(_scripts, _time);
            }
        }
    }

    /// <summary>The tests' held keys: forward for --walk seconds, jump, fire, and use at 1 second.</summary>
    private void HoldTestKeys(Input input)
    {
        var options = _options;
        var started = _time >= options.Delay;
        input.Hold(Key.Forward, started && _time <= options.Delay + options.Walk ? Input.State.Down : Input.State.Up);
        input.Hold(Key.Jump, started && options.Jump ? Input.State.Down : Input.State.Up);
        input.Hold(Key.Fire, started && options.Fire ? Input.State.Down : Input.State.Up);
        input.Hold(Key.UseItem, options.Use && _time >= UseStart && _time <= UseStart + UseTime ? Input.State.Down : Input.State.Up);
    }

    /// <summary>The tests' --die (Kurt is hurt to death) and --event (a special_event: 1 ends the level).</summary>
    private void RunTests()
    {
        if (_time < TestStart || _time - Step >= TestStart)
        {
            return;
        }

        if (_options.Die)
        {
            _kurt.Hurt(FatalDamage);
        }

        if (_options.Event is { } value)
        {
            _scripts.SpecialEvent(_scripts.GetArenaState(_scripts.CurrentArena).Controller, value);
        }
    }

    /// <summary>The camera, the scene, the HUD and the sounds' listener.</summary>
    private View DrawScene(float elapsed)
    {
        var camera = SceneView();
        _mixer.ListenerPosition = camera.Position;
        _mixer.ListenerRight = _flying ? _fly.Right : _follow.Right;
        // In sniper mode the sounds are heard through the scope.
        _mixer.ScopeZoom = _kurt.Sniping && !_flying ? _kurt.Scope.Zoom : 0f;
        _mixer.ListenerForward = _kurt.SniperForward;
        _mixer.ListenerUp = FollowCamera.UpOf(_kurt.SniperForward);
        _mixer.Update(elapsed);
        _scripts.Eye = camera.Position;
        _view.Draw(_scripts.DrawnArenas);
        _view.DrawEnd(_scripts.EndLevel);
        DrawObjects(_objects, _scripts, _level, _looks);
        _sniper.Draw(_kurt, _scripts, elapsed);
        _effects.Draw(_scripts, camera);
        _sprite.Draw(_kurt, camera.Position, _flying ? _fly.Forward : _follow.Forward, _flying ? Vector3.UnitZ : _follow.Up,
            _flying ? FreeCamera.FieldOfView : FollowCamera.FieldOfView);

        _hud.Messages.Update(elapsed);
        _hud.Draw(HudStateOf(_kurt, _scripts));
        return camera;
    }

    private View SceneView() => _flying ? _fly.View(_renderer.AspectRatio)
        : _kurt.Sniping ? FollowCamera.SniperView(_kurt, _renderer.AspectRatio) : _follow.View(_renderer.AspectRatio);

    /// <summary>A full save being loaded: the level and Kurt as they were.</summary>
    private void RestoreSnapshot(GameState state)
    {
        if (state.Snapshot is not { } json)
        {
            return;
        }

        state.Snapshot = null;
        _scripts.Load(json);
        Console.WriteLine($"restored hash {Snapshot.Hash(_scripts.Capture())}, objects {_scripts.Objects.Count}");
    }

    /// <summary>F2 (0x42b520(0)): the game stops and the name is asked, the level's number offered.</summary>
    private void OpenSnapshot()
    {
        if (!_scripts.CanSnapshot() || _kurt.Sniping)
        {
            return;
        }

        var name = (GameState.IndexOf(_level.Number) + 1).ToString(CultureInfo.InvariantCulture);
        _snapshotPrompt = new SavePrompt(_ui, new Fonts(_renderer, _ui.Fti), SaveGames.In(_ui.UserFolder), SnapshotSave(_scripts.Capture()), name);
        _ui.Window.CaptureMouse(Capture.Off);
    }

    /// <summary>A frame of F2's prompt (on black); Enter saves the level as it was, Esc gives up.</summary>
    private Event AskSnapshot(float elapsed, string? screenshot)
    {
        _ui.View.Layout(ScreenView.Fit.Inside);
        _snapshotPrompt!.Update(_ui.Input, elapsed);
        _snapshotPrompt.Draw();
        _ui.Present(screenshot);
        if (_snapshotPrompt.Closed)
        {
            _snapshotPrompt = null;
            CaptureMouse();
        }

        return Event.None;
    }

    /// <summary>The tests' full save (--snapshot): written at once, its hash printed.</summary>
    private void SaveSnapshot(string name)
    {
        var json = _scripts.Capture();
        SaveGames.In(_ui.UserFolder).Write(name, SnapshotSave(json));
        Console.WriteLine($"snapshot hash {Snapshot.Hash(json)}, objects {_scripts.Objects.Count}");
    }

    private SaveGame SnapshotSave(string json)
    {
        _state.Level = _level.Number;
        return _state.Save(SaveKind.Snapshot) with { Health = _kurt.Health, State = json };
    }

    /// <summary>The mouse looks around again once the pause menu closes.</summary>
    private void CaptureMouse()
    {
        if (!_pause.Open)
        {
            _ui.Window.CaptureMouse(_test ? Capture.Off : Capture.On);
        }
    }

    /// <summary>Letters typed in a level: <c>TOOSCARYFORME</c> turns gore on or off (not saved),
    /// <c>SEETHEWHOLEGAME</c> the main menu's debug keys (0x5742bc).</summary>
    private void TypeCheats(string typed)
    {
        foreach (var c in typed.ToUpperInvariant().Where(char.IsAsciiLetterUpper))
        {
            _typed = (_typed + c)[Math.Max(0, _typed.Length + 1 - DebugCheat.Length)..];
            if (_typed.EndsWith(GoreCheat, StringComparison.Ordinal))
            {
                _typed = "";
                _scripts.Option = 1 - _scripts.Option;
                Console.WriteLine($"Gore {(_scripts.Option != 0 ? "on" : "off")}");
            }
            else if (_typed.EndsWith(DebugCheat, StringComparison.Ordinal))
            {
                _typed = "";
                _state.DebugKeys = !_state.DebugKeys;
                Console.WriteLine($"Debug keys {(_state.DebugKeys ? "on" : "off")}");
            }
        }
    }

    /// <summary>Kurt died (damp_control 0x466b40): the death is counted and the game goes back to the
    /// main menu, whose "Continue" starts the level again.</summary>
    private void OnDied()
    {
        Console.WriteLine("Kurt died");
        _state.Level = _level.Number;
        _state.Deaths++;
        _state.StrikeUsed = _scripts.AirStrike.UsedUp;
        _next = Event.KurtDied;
    }

    public void Dispose()
    {
        _audio.StopAll();
        _ui.Window.CaptureMouse(Capture.Off);
    }

    /// <summary>What the HUD shows of Kurt and the object he shoots at.</summary>
    private static HudState HudStateOf(Kurt.Kurt kurt, ScriptRuntime scripts)
    {
        var inventory = kurt.Inventory;
        var slots = inventory.Slots.Select(s => ((int)s.Item, s.Count)).ToList();
        var (barHealth, barMax) = scripts.GetBar();
        return new HudState(kurt.Health, kurt.HurtFlash, kurt.WhiteFlash, kurt.Current == Kurt.Kurt.State.Dead,
            slots, inventory.Selected, inventory.SuperChainGun, barHealth, barMax, scripts.Rides.Bomber?.Shown);
    }

    /// <summary>The visible objects, each with its arena's palette and textures.</summary>
    private static void DrawObjects(ObjectView view, ScriptRuntime scripts, LevelData level, Dictionary<string, ObjectView.Look> looks)
    {
        foreach (var obj in scripts.Objects)
        {
            if (!obj.Visible)
            {
                continue;
            }

            if (!looks.TryGetValue(obj.Arena, out var look))
            {
                var arena = level.Arenas.Find(a => a.Name == obj.Arena);
                look = looks[obj.Arena] = arena != null
                    ? new ObjectView.Look(level.PaletteOf(arena), level.ArchivesOf(arena))
                    : new ObjectView.Look(level.Dti.Palette, [level.LevelTextures]);
            }

            view.Draw(obj, look);
        }
    }

    /// <summary>For tests: the objects of Kurt's arena (like the Godot port's --profile).</summary>
    private static void Profile(ScriptRuntime scripts, float time)
    {
        var second = scripts.SecondArena + (scripts.SecondActive ? " (active)" : "");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Profile {time:0}s: objects {scripts.Objects.Count}, arena {scripts.CurrentArena}, second {second}"));
        Console.WriteLine($"  effects {scripts.Effects.All.Count}, debris pieces {scripts.Debris.PieceCount()}, fans {scripts.Fans.Count}");
        foreach (var obj in scripts.Objects.Where(o => o.Arena == scripts.CurrentArena))
        {
            var p = obj.Position;
            var door = (obj.Flags & MdkObject.FlagDoor) != 0 ? $" door {obj.DoorState:x}" : "";
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {obj.TypeName}_{obj.InstanceId} {obj.Arena} ({Rounded(p.X)}, {Rounded(p.Y)}, {Rounded(p.Z)}) yaw {(int)obj.Yaw} " +
                $"move {obj.MoveCommand} path {obj.Path} anim {obj.Animation?.Name ?? "-"} frame {obj.AnimationFrame} " +
                $"speed {obj.Speed:0.0} health {obj.Health} flags {obj.Flags:x}{door}"));
        }

        RideTest.Report(scripts);
        if (scripts.EndLevel is { } end)
        {
            Console.WriteLine(end.Describe());
        }

        if (scripts.Vm.Unimplemented.Count != 0)
        {
            Console.WriteLine($"  unimplemented opcodes: {string.Join(", ", scripts.Vm.Unimplemented.Select(u => $"{u.Key}x{u.Value}"))}");
        }
    }

    /// <summary>A coordinate rounded like Godot's Vector3.round().</summary>
    private static string Rounded(float value) =>
        (MathF.Round(value, MidpointRounding.AwayFromZero) + 0f).ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>The test's screenshot once its wait is over, or F12's.</summary>
    private static string? SavePath(ViewerOptions options, Input input, float time)
    {
        if (options.Screenshot != null)
        {
            return time >= options.Wait ? options.Screenshot : null;
        }

        return input.WasPressed(Key.Screenshot) ? $"mdk-{DateTime.Now:yyyyMMdd-HHmmss}.bmp" : null;
    }

    /// <summary>For tests: where Kurt is and what he does.</summary>
    private static void Report(Kurt.Kurt kurt, ArenaSpace space)
    {
        var f = kurt.Feet;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Kurt at {f.X:0.00} {f.Y:0.00} {f.Z:0.00} yaw {kurt.Yaw:0} {kurt.Current} floor {kurt.OnFloor} arena {space.ArenaAt(f)} health {kurt.Health}"));
    }

    /// <summary>The camera pitch of the arena around the feet (DTI, degrees).</summary>
    private static float ArenaPitch(LevelData level, ArenaSpace space, Vector3 feet)
    {
        const float DefaultPitch = 4f;
        var name = space.ArenaAt(feet);
        return level.Dti.Arenas.FirstOrDefault(a => a.Name == name)?.Pitch ?? DefaultPitch;
    }

    /// <summary>The starting arena's music (named by the level's CMI, in <c>LEVELnO.SNI</c>).</summary>
    private static void PlayMusic(SoundMixer mixer, Cmi cmi, LevelData level)
    {
        var arena = level.Dti.Arenas[level.Dti.StartArena].Name;
        if (cmi.ArenaMusic.TryGetValue(arena, out var music) && mixer.PlayMusic(music) != 0)
        {
            Console.WriteLine($"Music: {music}");
        }
    }

    /// <summary>The level's sky and the panorama its mirrors show, through the level's palette.</summary>
    private static Panorama CreatePanorama(Renderer renderer, Dti dti)
    {
        var sky = renderer.CreateIndexTexture(dti.Sky.Width, dti.Sky.Height, dti.Sky.Indices);
        var mirrorSky = dti.MirrorSky == dti.Sky ? sky : renderer.CreateIndexTexture(dti.MirrorSky.Width, dti.MirrorSky.Height, dti.MirrorSky.Indices);
        var palette = renderer.CreatePalette(dti.Palette.Rgba);
        return new Panorama(sky, mirrorSky, palette, dti.SkyWrapWidth, dti.SkyHorizonRow, dti.SkyOffset, dti.Sky.Height,
            dti.SkyTopColor, dti.SkyBottomColor);
    }

    /// <summary>The colour above the sky panorama.</summary>
    private static Vector4 SkyColour(Dti dti)
    {
        var at = dti.SkyTopColor * 4;
        var rgba = dti.Palette.Rgba;
        return new Vector4(rgba[at], rgba[at + 1], rgba[at + 2], byte.MaxValue) / byte.MaxValue;
    }
}
