using Mdk.Engine.Audio;
using Mdk.Formats;
using Mdk.Game.Audio;

namespace Mdk.Game.Tests;

/// <summary>The arena music and its fades (level_audio.gd), on a muted device.</summary>
public class MusicTests
{
    private const int Rate = 44100;
    private const string None = "NONE";
    /// <summary>The fades: 256 frames in, 128 out, at 34 ms a frame.</summary>
    private const float FadeInSeconds = 256 * 0.034f;
    private const float FadeOutSeconds = 128 * 0.034f;

    private static readonly Dictionary<string, string> Tracks = new()
    {
        ["HMO_1"] = "H1",
        ["HMO_2"] = "H2",
        ["CHMO_1"] = "",
        ["HMO_3"] = None,
    };

    /// <summary>Every name but NONE is a looping sound.</summary>
    private static (LevelMusic Music, AudioDevice Device) Create()
    {
        var device = new AudioDevice(Output.Muted);
        var entry = new SoundMixer.Entry(new Sound(new float[Rate], 1, Rate, Looping.Forever), 0);
        var mixer = new SoundMixer(device, name => name == None ? null : entry);
        return (new LevelMusic(mixer, Tracks), device);
    }

    private static void Run(LevelMusic music, float seconds)
    {
        const float Frame = 1f / 60f;
        for (var t = 0f; t < seconds; t += Frame)
        {
            music.Update(Frame);
        }
    }

    [Fact]
    public void CorridorsPlayTheLevelsCorridorTrack()
    {
        var (music, device) = Create();
        using var _ = device;
        Assert.Equal("H1", music.TrackOf("HMO_1"));
        Assert.Equal(LevelMusic.Corridor, music.TrackOf("CHMO_1"));
        Assert.Equal(LevelMusic.Corridor, music.TrackOf("UNKNOWN"));
        Assert.Equal(None, music.TrackOf("HMO_3"));
    }

    [Fact]
    public void NewTrackFadesInFromSilence()
    {
        var (music, device) = Create();
        using var _ = device;
        music.Enter("HMO_1");
        Assert.Equal("H1", music.Playing);
        Assert.Equal(0f, music.Volume);

        Run(music, FadeInSeconds / 2f);
        Assert.Equal(SoundMixer.FullVolume / 2f, music.Volume, SoundMixer.FullVolume * 0.01f);
        Run(music, FadeInSeconds / 2f + 0.1f);
        Assert.Equal(SoundMixer.FullVolume, music.Volume);
    }

    [Fact]
    public void OldTrackFadesOutAndStops()
    {
        var (music, device) = Create();
        using var _ = device;
        music.Enter("HMO_1");
        Run(music, FadeInSeconds + 0.1f);
        music.Enter("CHMO_1");
        Assert.Equal(LevelMusic.Corridor, music.Playing);
        Assert.Equal("H1", music.Fading);

        Run(music, FadeOutSeconds - 0.1f);
        Assert.Equal("H1", music.Fading);
        Run(music, 0.2f);
        Assert.Null(music.Fading);
    }

    [Fact]
    public void GoingBackSwapsWithTheFadingTrack()
    {
        var (music, device) = Create();
        using var _ = device;
        music.Enter("HMO_1");
        Run(music, FadeInSeconds + 0.1f);
        music.Enter("CHMO_1");
        Run(music, 1f);
        var left = music.FadingVolume;
        music.Enter("HMO_1");
        Assert.Equal("H1", music.Playing);
        Assert.Equal(left, music.Volume);
        Assert.Equal(LevelMusic.Corridor, music.Fading);
    }

    [Fact]
    public void NoneIsSilence()
    {
        var (music, device) = Create();
        using var _ = device;
        music.Enter("HMO_1");
        music.Enter("HMO_3");
        Assert.Equal(None, music.Playing);
        Assert.Equal("H1", music.Fading);
        // Still in the same arena: nothing changes.
        music.Enter("HMO_3");
        Assert.Equal("H1", music.Fading);
    }

    [DataFact]
    public void Level3CorridorsHaveNoTrack()
    {
        var data = MdkData.Find()!;
        var cmi = Cmi.Load(data.PathOf("TRAVERSE/LEVEL3/LEVEL3.CMI"));
        Assert.Contains(cmi.ArenaMusic, m => m.Value.Length == 0);
        Assert.Equal("H1", cmi.ArenaMusic["HMO_1"]);
    }
}
