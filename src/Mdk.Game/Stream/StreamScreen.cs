using System.Globalization;
using Mdk.Engine.Audio;
using Mdk.Engine.Platform;
using Mdk.Formats;
using Mdk.Game.Flow;
using Mdk.Game.Menu;

namespace Mdk.Game.Stream;

/// <summary>The stream after a level (game state 5: init 0x433b50, each frame 0x4352ac, cleanup
/// 0x435210; stream.gd). Kurt flies down the generated tube (<see cref="Flight"/>), drawn by
/// <see cref="StreamView"/>; the walls hurt. After about 30 seconds (or at 1 health) Bones picks him
/// up; after LEVEL8 (the Gunter variant) Kurt follows Gunter carrying Bones to a planet and can die.
/// <code>
///   level ─► stream ─┬─ normal: StreamEnded ─► statistics
///                    ├─ Gunter: StreamEnded ─► save prompt ─► LEVEL5
///                    └─ died (Gunter tube): main menu
/// </code></summary>
public sealed class StreamScreen : IScreen
{
    private const string Archive = "STREAM/STREAM.BNI";
    private const string Textures = "STREAM/STREAM.MTI";
    private const string SystemPalette = "SYS_PAL";
    private const string StreamPalette = "PAL";
    private const int RgbSize = 3;
    /// <summary>The last level index with statistics after the stream; the next one is Gunter's.</summary>
    private const int LastStatisticsIndex = 3;
    private const string KurtAnimation = "KURTANIM";
    private const string RescueAnimation = "BONESANIM";
    private const string HitSound = "HITSIDE";
    private const string WindSound = "WIND";
    /// <summary>Sounds playing at once at most (the stream's players).</summary>
    private const int Players = 8;

    private readonly Ui _ui;
    private readonly int _level;
    private readonly Bni _bni;
    private readonly StreamView _view;
    private readonly Flight _flight;
    private readonly string _otherModel;
    private readonly string _otherAnimation;
    private readonly Dictionary<string, Sound?> _sounds = [];
    private readonly List<int> _voices = [];
    private int _hitVoice;
    private int _wallHits;

    public StreamScreen(Ui ui, int level, int health, WatcomRandom random)
    {
        _ui = ui;
        _level = level;
        var index = Math.Clamp(GameState.IndexOf(level), 0, LastStatisticsIndex + 1);
        var kind = index > LastStatisticsIndex ? StreamTube.Kind.Gunter : StreamTube.Kind.Normal;
        var difficulty = ui.Settings.Difficulty;
        _bni = Bni.Load(ui.Data.PathOf(Archive));
        (_otherModel, _otherAnimation) = kind == StreamTube.Kind.Gunter ? ("GUNTA", "GUNTANIM") : ("SWH150", "SWHANM");

        var tube = new StreamTube(index, difficulty, kind, random);
        _view = new StreamView(ui.Renderer, _bni, LoadPalette(), TextureArchive.Load(ui.Data.PathOf(Textures)));
        _flight = new Flight(tube, kind, difficulty, random, health, ClipOf(KurtAnimation), ClipOf(RescueAnimation), ClipOf(_otherAnimation));

        _ui.Play(Ui.SoundOf(_bni, WindSound, Looping.Forever));
        var last = tube.Head - 1;
        var ring = tube.Origin(last);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Stream after LEVEL{level}: {kind}, turn {tube.MaxTurn}, radius {tube.MinRadius}-{tube.MaxRadius}, {_flight.LightCount} lights, ring {last} at {ring.X:0.000} {ring.Y:0.000} {ring.Z:0.000}"));
    }

    /// <summary>Colours 0-63 are the system palette, 64-255 the stream's <c>PAL</c>.</summary>
    private Palette LoadPalette()
    {
        var systemSize = Palette.ArenaFirstIndex * RgbSize;
        var rgb = new byte[Palette.Size * RgbSize];
        _ui.Fti.GetBytes(SystemPalette).AsSpan(0, systemSize).CopyTo(rgb);
        var entry = _bni.Entries[StreamPalette];
        _bni.Bytes.AsSpan(entry.Offset + systemSize, rgb.Length - systemSize).CopyTo(rgb.AsSpan(systemSize));
        return Palette.FromRgb(rgb);
    }

    private Clip ClipOf(string animation)
    {
        var a = _view.AnimationOf(animation);
        return new Clip(a.FrameCount, a.Speed);
    }

    public Event Frame(float elapsed, string? screenshot)
    {
        var input = _ui.Input;
        if (input.WasPressed(MenuKey.Back))
        {
            _flight.Stop();
        }

        _flight.Update(elapsed, SteeringOf(input));
        PlaySounds();
        Report();

        var animation = _flight.Bones == null ? KurtAnimation : RescueAnimation;
        _view.Draw(_flight, animation, _otherModel, _otherAnimation);
        _ui.Renderer.Present(_view.ViewOf(_flight), Ui.Black, screenshot);
        if (!_flight.Ended)
        {
            return Event.None;
        }

        return _flight.Outcome == Outcome.Died ? Event.Menu : Event.StreamEnded;
    }

    /// <summary>The turn keys or strafe keys steer left and right, forward and back up and down.</summary>
    private static Steering SteeringOf(Input input)
    {
        int Held(Key key) => input.IsDown(key) ? 1 : 0;
        var right = Held(Key.TurnRight) + Held(Key.StrafeRight) - Held(Key.TurnLeft) - Held(Key.StrafeLeft);
        var up = Held(Key.Forward) - Held(Key.Back);
        return new Steering(Math.Sign(right), Math.Sign(up));
    }

    /// <summary>The flight's sounds; a wall hit's thud doesn't start again while it plays.</summary>
    private void PlaySounds()
    {
        if (_flight.Events.Contains(FlightEvent.WallHit) && !_ui.Audio.IsPlaying(_hitVoice))
        {
            _hitVoice = _ui.Play(SoundOf(HitSound));
        }

        _voices.RemoveAll(v => !_ui.Audio.IsPlaying(v));
        foreach (var name in _flight.Sounds)
        {
            if (_voices.Count >= Players)
            {
                return;
            }

            _voices.Add(_ui.Play(SoundOf(name)));
        }
    }

    private Sound? SoundOf(string name)
    {
        if (!_sounds.TryGetValue(name, out var sound))
        {
            sound = _sounds[name] = Ui.SoundOf(_bni, name);
        }

        return sound;
    }

    /// <summary>Prints the stream's moments (tests compare them).</summary>
    private void Report()
    {
        var tube = _flight.Tube;
        var kurt = _flight.Kurt;
        foreach (var e in _flight.Events)
        {
            var firstHit = e == FlightEvent.WallHit && _wallHits++ == 0;
            FormattableString? line = e switch
            {
                FlightEvent.WallHit when firstHit =>
                    $"Stream: first wall hit at segment {tube.Tail}, t {kurt.T:0.000} x {kurt.X:0.000} z {kurt.Z:0.000} yaw {kurt.Yaw:0.00} speed {kurt.Speed:0.000} health {_flight.Health}",
                FlightEvent.BonesCame => $"Stream: Bones comes at segment {tube.Tail}, health {_flight.Health}, {_flight.Time:0.0} s",
                FlightEvent.KurtDied => $"Stream: Kurt died at segment {tube.Tail}, {_flight.Time:0.0} s",
                FlightEvent.BonusTaken => $"Stream: health bonus, health {_flight.Health}",
                FlightEvent.Ended => $"Stream ended after LEVEL{_level}: {_flight.Outcome}, health {_flight.Health}, segment {tube.Tail}, {_flight.Time:0.0} s",
                _ => null,
            };
            if (line != null)
            {
                Console.WriteLine(line.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    public void Dispose()
    {
    }
}
