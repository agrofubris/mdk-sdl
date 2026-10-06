namespace Mdk.Game.Audio;

/// <summary>The music of Kurt's arena (0x419158, fades 0x418ffc; godot-mdk <c>level_audio.gd</c>,
/// docs/sound.md "Music"). An arena plays its track (the CMI's), a corridor (no track) the level's
/// <c>CORRIDOR</c>, <c>NONE</c> nothing. A new track fades in from −25 dB to full in about 8.7 s while
/// the previous one fades out in about 4.35 s, then stops; going back to the fading track swaps them.
/// <code>
///   arena ──► track ──┬─ same as current: nothing
///                     ├─ the fading one: they swap (it goes on where it was)
///                     └─ else: current ──► fading (−0x100/frame), new ──► current (+0x80/frame)
/// </code></summary>
public sealed class LevelMusic(SoundMixer mixer, IReadOnlyDictionary<string, string> tracks)
{
    public const string Corridor = "CORRIDOR";
    /// <summary>The original's frames: 34 ms each.</summary>
    private const float FramesPerSecond = 1000f / 34f;
    public const float FadeIn = 0x80 * FramesPerSecond;
    public const float FadeOut = 0x100 * FramesPerSecond;

    /// <summary>A track and its volume (0-0x7FFF); no name is silence.</summary>
    private sealed class Track
    {
        public string? Name;
        public int Voice;
        public float Volume;
    }

    private Track _current = new();
    private Track _fading = new();
    private string _arena = "";
    private string _ambience = "";
    private int _ambienceVoice;

    /// <summary>Arena to its ambient loop, played under the music (the 1996 demo's <c>Cmi.ArenaAmbience</c>).</summary>
    public IReadOnlyDictionary<string, string> Ambience { get; init; } = new Dictionary<string, string>();

    /// <summary>The track fading in or playing (not a sound, as <c>NONE</c>: silence), and its volume.</summary>
    public string? Playing => _current.Name;
    public float Volume => _current.Volume;
    /// <summary>The track fading out, and its volume.</summary>
    public string? Fading => _fading.Name;
    public float FadingVolume => _fading.Volume;

    /// <summary>The track of an arena: its own, or <c>CORRIDOR</c> without one.</summary>
    public string TrackOf(string arena)
    {
        var name = tracks.GetValueOrDefault(arena, "");
        return name.Length == 0 ? Corridor : name;
    }

    /// <summary>Kurt is in <paramref name="arena"/> (null outside every arena: no change).</summary>
    public void Enter(string? arena)
    {
        if (string.IsNullOrEmpty(arena) || arena == _arena)
        {
            return;
        }

        _arena = arena;
        Switch(TrackOf(arena));
        if (Ambience.Count != 0)
        {
            PlayAmbience(Ambience.GetValueOrDefault(arena, ""));
        }
    }

    /// <summary>The arena's ambient loop replaces the last one (none: silence).</summary>
    private void PlayAmbience(string name)
    {
        if (name == _ambience)
        {
            return;
        }

        mixer.StopVoice(_ambienceVoice);
        _ambience = name;
        _ambienceVoice = name.Length == 0 ? 0 : mixer.PlayLooped(name);
    }

    /// <summary>One frame of the fades: the current track up, the fading one down and stopped below 0.</summary>
    public void Update(float delta)
    {
        _current.Volume = MathF.Min(_current.Volume + FadeIn * delta, SoundMixer.FullVolume);
        _fading.Volume -= FadeOut * delta;
        if (_fading.Volume < 0f && _fading.Name != null)
        {
            mixer.StopVoice(_fading.Voice);
            _fading = new Track();
        }

        Apply(_current);
        Apply(_fading);
    }

    private void Switch(string name)
    {
        if (name == _current.Name)
        {
            return;
        }

        // Back to the fading track: they swap.
        if (name == _fading.Name)
        {
            (_current, _fading) = (_fading, _current);
            return;
        }

        // The current track fades out, the new one fades in from silence.
        if (_fading.Voice != 0)
        {
            mixer.StopVoice(_fading.Voice);
        }

        _fading = _current;
        var voice = mixer.PlayMusic(name);
        _current = new Track { Name = name, Voice = voice };
        if (voice != 0)
        {
            Console.WriteLine($"Music: {name}");
        }
    }

    private void Apply(Track track)
    {
        if (track.Voice != 0)
        {
            mixer.SetVolume(track.Voice, (int)MathF.Max(track.Volume, 0f));
        }
    }
}
