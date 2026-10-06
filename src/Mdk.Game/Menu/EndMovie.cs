using Mdk.Engine.Audio;
using Mdk.Engine.Platform;
using Mdk.Formats;
using Mdk.Game.Flow;

namespace Mdk.Game.Menu;

/// <summary>The end of the game (game state 8, 0x47727c; end_movie.gd): <c>MISC/FLIC/MDKEND.FLC</c>
/// with its sounds from <c>MISC/FINISH.BNI</c> and a white flash (0x477604), then
/// <c>MISC/FLIC/MDKBZK.MVE</c> (0x477870) and the main menu, after the splash. Esc gives up.
/// <code>
///   frame 1 DOGSHIP (loop) ... 129 DROP, 133 FLYBY, 186 EXPLODE1, 188 DOGSHIP stops, 194 ENDEXP,
///   196 EXPLODE1, 210-232 whiter, 233 white for 1 s, 233-260 back
/// </code></summary>
public sealed class EndMovie : IScreen
{
    private const string Video = "MISC/FLIC/MDKEND.FLC";
    private const string Movie = "MISC/FLIC/MDKBZK.MVE";
    private const string Sounds = "MISC/FINISH.BNI";
    /// <summary>Sounds by frame (the frame counter before decoding a frame).</summary>
    private static readonly Dictionary<int, string> FrameSounds = new()
    {
        [1] = "DOGSHIP", [129] = "DROP", [133] = "FLYBY", [186] = "EXPLODE1", [194] = "ENDEXP", [196] = "EXPLODE1",
    };
    private const string Looped = "DOGSHIP";
    private const int LoopEnd = 188;
    private const int FlashStart = 210;
    private const int FlashPeak = 233;
    private const int FlashEnd = 260;
    private const float FlashHold = 1f;
    private const float MaxColour = 255f;

    private readonly Ui _ui;
    private readonly VideoPlayer _video;
    private readonly Bni? _sounds;
    private readonly Dictionary<string, int> _voices = [];
    private Event _next = Event.None;

    public EndMovie(Ui ui)
    {
        _ui = ui;
        _video = new VideoPlayer(ui);
        var path = ui.Data.PathOf(Sounds);
        _sounds = File.Exists(path) ? Bni.Load(path) : null;
        _video.FrameShown += OnFrame;
        _video.Finished += PlayMovie;
        if (!_video.Play(Video))
        {
            PlayMovie();
        }
    }

    public Event Frame(float elapsed, string? screenshot)
    {
        _ui.View.Layout(ScreenView.Fit.Inside);
        if (_ui.Input.WasPressed(MenuKey.Back))
        {
            _next = Event.Menu;
        }

        _video.Update(elapsed);
        _video.Draw();
        _ui.Present(screenshot);
        return _next;
    }

    /// <summary>A frame was shown: the extras of the next one (0x477604).</summary>
    private void OnFrame(int frame)
    {
        if (FrameSounds.TryGetValue(frame, out var name))
        {
            Play(name);
        }

        if (frame == LoopEnd && _voices.TryGetValue(Looped, out var loop))
        {
            _ui.Audio.Stop(loop);
        }

        _video.Brighten(Flash(frame));
        if (frame == FlashPeak)
        {
            _video.Hold(FlashHold);
        }
    }

    /// <summary>How much whiter a frame is (0-1).</summary>
    public static float Flash(int frame)
    {
        if (frame is < FlashStart or > FlashEnd)
        {
            return 0f;
        }

        var t = frame < FlashPeak
            ? (float)(frame - FlashStart) / (FlashPeak - FlashStart)
            : (float)(FlashEnd - frame) / (FlashEnd - FlashPeak);
        return MathF.Round(MaxColour * t) / MaxColour;
    }

    /// <summary>After the FLC: the MVE, then the menu.</summary>
    private void PlayMovie()
    {
        _video.Finished -= PlayMovie;
        foreach (var voice in _voices.Values)
        {
            _ui.Audio.Stop(voice);
        }

        _video.Brighten(0f);
        _video.Finished += () => _next = Event.Menu;
        if (!_video.PlayMovie(Movie))
        {
            _next = Event.Menu;
        }
    }

    private void Play(string name)
    {
        if (_sounds == null)
        {
            return;
        }

        _voices[name] = _ui.Play(Ui.SoundOf(_sounds, name, name == Looped ? Looping.Forever : Looping.Once));
    }

    public void Dispose()
    {
        _video.Dispose();
        _ui.Audio.StopAll();
    }
}
