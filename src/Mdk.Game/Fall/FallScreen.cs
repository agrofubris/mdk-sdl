using System.Globalization;
using System.Numerics;
using Mdk.Engine.Platform;
using Mdk.Formats;
using Mdk.Game.Audio;
using Mdk.Game.Flow;
using Mdk.Game.Hud;
using Mdk.Game.Menu;

namespace Mdk.Game.Fall;

/// <summary>The fall before a level (fall.gd) as a screen of the game: the keys steer Kurt, Esc
/// skips it (the port's addition). It ends with the level, which keeps Kurt's health and pickups
/// (<see cref="GameState.Carry"/>), or, when he dies, with the main menu.
/// <code>
///   input ──► FallSim ──► FallView (3D), FallHud, palette effects ──► frame
///                └── sounds (FALL3D.SNI): 8 voices, the wind and grind loops
/// </code></summary>
public sealed class FallScreen : IScreen
{
    /// <summary>The falls are those of the level indices 0-4 (LEVEL5 has none).</summary>
    private const int LastIndex = 4;
    private const float MaxDelta = 0.1f;
    /// <summary>The original mixes the fall's sounds on 8 voices at most; more are dropped.</summary>
    private const int MaxVoices = 8;
    private const float MessageSeconds = 3f;
    private const float PrintInterval = 1f;

    private readonly Ui _ui;
    private readonly GameState _state;
    private readonly ViewerOptions _test;
    private readonly FallSim _sim;
    private readonly FallView _view;
    private readonly SoundBank _sounds;
    private readonly List<int> _voices = [];
    private readonly int _windVoice;
    private int _grindVoice;
    private float _nextPrint;

    public FallScreen(Ui ui, GameState state, ViewerOptions test)
    {
        _ui = ui;
        _state = state;
        _test = test;
        // No pointer over the fall (as in a level); a hidden test window keeps it free.
        if (ui.Window.Visibility == Visibility.Shown)
        {
            ui.Window.CaptureMouse(Capture.On);
        }

        var index = Math.Clamp(GameState.IndexOf(state.Level), 0, LastIndex);
        var n = index + 1;
        var data = ui.Data;
        var bni = Bni.Load(data.PathOf("FALL3D/FALL3D.BNI"));
        var mti = TextureArchive.Load(data.PathOf($"FALL3D/FALL3D_{n}.MTI"));
        _view = new FallView(ui.Renderer, bni, mti, index, ui.Fti);
        _sounds = new SoundBank([Sni.Load(data.PathOf("FALL3D/FALL3D.SNI"))]);

        var models = _view.Models;
        FallSim.Animation Animation(string name)
        {
            var animation = models.Animation(name);
            return new FallSim.Animation(animation.Speed, animation.FrameCount);
        }

        var setup = new FallSim.Setup(index, ui.Settings.Difficulty, Fall3d.Pickups(bni, $"FALLPU_{n}"),
            Animation("KURTANIM"), Animation("KURT_HIT"), Animation("BONESANM"));
        _sim = new FallSim(setup, new Random());
        _sim.Sound += Play;
        _sim.Message += name => _view.Hud.Messages.Push(name, Messages.FlagZoom, MessageSeconds);

        // The wind plays from the start, silent until the intro's end.
        _windVoice = Loop("WINDLOOP");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Fall: index {index} difficulty {(int)setup.Difficulty} beam {_sim.BeamSpeed:0.00} missiles {_sim.MissilesPerDetection} spread {_sim.Spread:0.00} missile gap {_sim.MissileGap} radar gap {_sim.RadarGap} pickups {string.Join(",", setup.Pickups)}"));
    }

    public Event Frame(float elapsed, string? screenshot)
    {
        var input = _ui.Input;
        if (input.WasPressed(MenuKey.Back))
        {
            _sim.Skip();
        }

        HoldTestKeys(input);
        _sim.Update(elapsed, Steering(input));
        UpdateSounds();
        var hud = _view.Hud;
        if (!_sim.InIntro)
        {
            hud.Messages.Update(MathF.Min(elapsed, MaxDelta));
            hud.Tick(_sim.Inventory, _sim.Ticks);
            Profile();
        }

        var view = _view.Draw(_sim);
        if (!_sim.InIntro)
        {
            hud.Draw(_sim.Health, _sim.Inventory, _sim.Dying ? FallHud.Life.Dying : FallHud.Life.Alive, _sim.Red);
        }

        _view.DrawEffects(_sim);
        _ui.Renderer.Present(view, _view.Backdrop, screenshot);
        return Next();
    }

    /// <summary>The level with Kurt's health and pickups, or the menu when he died.</summary>
    private Event Next()
    {
        switch (_sim.State)
        {
            case FallSim.Outcome.Landed:
                Report("Fall ended");
                _state.Carry = new FallCarry(_sim.Health, [.. _sim.Collected]);
                return Event.Play;
            case FallSim.Outcome.GameOver:
                Report("Fall: Kurt died");
                return Event.Menu;
            default:
                return Event.None;
        }
    }

    /// <summary>The steering keys: x right, y up the screen (the turn or strafe keys, forward and back).</summary>
    private static Vector2 Steering(Input input)
    {
        static int Held(Input input, Key key) => input.IsDown(key) ? 1 : 0;
        var right = Held(input, Key.TurnRight) + Held(input, Key.StrafeRight) - Held(input, Key.TurnLeft) - Held(input, Key.StrafeLeft);
        var up = Held(input, Key.Forward) - Held(input, Key.Back);
        return new Vector2(Math.Sign(right), Math.Sign(up));
    }

    /// <summary>Tests: "forward" held for --walk seconds from --delay seconds of fall.</summary>
    private void HoldTestKeys(Input input)
    {
        if (_test.Walk <= 0f)
        {
            return;
        }

        var held = !_sim.InIntro && _sim.Time >= _test.Delay && _sim.Time < _test.Delay + _test.Walk;
        input.Hold(Key.Forward, held ? Input.State.Down : Input.State.Up);
    }

    /// <summary>Tests (--profile): Kurt, the camera, the wind and the health once a second of fall.</summary>
    private void Profile()
    {
        if (!_test.Profile || _sim.Time < _nextPrint)
        {
            return;
        }

        _nextPrint += PrintInterval;
        Report("Fall");
    }

    private void Report(string what)
    {
        var k = _sim.KurtPosition;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{what} t {_sim.Time:0.0} kurt {k.X:0.00} {k.Y:0.00} {k.Z:0.0} camera {_sim.CameraPosition.Z:0.0} wind {_sim.Wind >> FallSim.WindShift} health {_sim.Health} pickups {string.Join(",", _sim.Collected)}"));
    }

    private void UpdateSounds()
    {
        var audio = _ui.Audio;
        audio.Set(_windVoice, _sim.WindVolume, 1f, 0f);
        if (_sim.InIntro)
        {
            return;
        }

        // The minecrawler grinds from the start of the fall.
        if (_grindVoice == 0)
        {
            _grindVoice = Loop("C_GRIND");
        }

        audio.Set(_grindVoice, _sim.GrindVolume, 1f, 0f);
    }

    private int Loop(string name) => _sounds.Get(name) is { } entry ? _ui.Audio.Play(entry.Sound, 0f) : 0;

    private void Play(string name)
    {
        _voices.RemoveAll(v => !_ui.Audio.IsPlaying(v));
        if (_voices.Count >= MaxVoices || _sounds.Get(name) is not { } entry)
        {
            return;
        }

        _voices.Add(_ui.Audio.Play(entry.Sound, 1f));
    }

    public void Dispose()
    {
        _ui.Window.CaptureMouse(Capture.Off);
        if (_test.Walk > 0f)
        {
            _ui.Input.Hold(Key.Forward, Input.State.Up);
        }
    }
}
