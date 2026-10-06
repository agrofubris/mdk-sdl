using System.Numerics;
using Mdk.Engine.Audio;
using Mdk.Formats;
using Mdk.Game.Audio;
using Mdk.Game.Collision;
using Mdk.Game.Level;
using Mdk.Game.Objects;
using Mdk.Game.Scripts;

namespace Mdk.Game.Tests;

/// <summary>The mixer's laws, on a muted device (nothing reaches the speakers).</summary>
public class SoundTests
{
    private const int Rate = 44100;
    /// <summary>-25 dB, the floor of the volume law.</summary>
    private const float FloorGain = 0.05623f;

    private static Sound Tone(float seconds, Looping looping) => new(new float[(int)(Rate * seconds)], 1, Rate, looping);

    [Fact]
    public void VolumeIsLinearInDecibelsAndNeverSilent()
    {
        Assert.Equal(1f, SoundMixer.Gain(SoundMixer.FullVolume), 4);
        Assert.Equal(FloorGain, SoundMixer.Gain(0), 4);
        // Half volume: -12.5 dB.
        Assert.Equal(MathF.Pow(10f, -12.5f / 20f), SoundMixer.Gain(SoundMixer.FullVolume / 2f), 3);
    }

    [Fact]
    public void OneShotEndsAndLoopKeepsPlaying()
    {
        using var device = new AudioDevice(Output.Muted);
        var once = device.Play(Tone(0.1f, Looping.Once), 1f);
        var loop = device.Play(Tone(0.1f, Looping.Forever), 1f);
        device.Update(0.2f);
        Assert.False(device.IsPlaying(once));
        Assert.True(device.IsPlaying(loop));
    }

    [Fact]
    public void OncePlaysOnlyWhenSilent()
    {
        using var device = new AudioDevice(Output.Muted);
        var entry = new SoundMixer.Entry(Tone(1f, Looping.Once), SoundMixer.FullVolume);
        var mixer = new SoundMixer(device, _ => entry);
        Assert.NotEqual(0, mixer.Play("A"));
        Assert.Equal(0, mixer.Play("A", SoundMixer.Start.Once));
        Assert.NotEqual(0, mixer.Play("A", SoundMixer.Start.New));
    }

    [Fact]
    public void FarSoundsFallToTheFloor()
    {
        using var device = new AudioDevice(Output.Muted);
        var entry = new SoundMixer.Entry(Tone(1f, Looping.Forever), SoundMixer.FullVolume);
        var mixer = new SoundMixer(device, _ => entry) { ListenerPosition = Vector3.Zero };
        var near = mixer.PlayAt("A", new Vector3(10f, 0f, 0f));
        var far = mixer.PlayAt("A", new Vector3(300f, 0f, 0f));
        Assert.Equal(1f, device.GainOf(near), 3);
        Assert.Equal(FloorGain, device.GainOf(far), 3);
    }

    /// <summary>A sound following an object (play_sound flag 0x10) stops when the object is removed
    /// (sound_mixer.gd: its player goes with the object).</summary>
    [DataFact]
    public void FollowingSoundStopsWithItsObject()
    {
        const int Level = 3;
        const int Following = 0x10;
        var data = MdkData.Find()!;
        using var device = new AudioDevice(Output.Muted);
        var entry = new SoundMixer.Entry(Tone(1f, Looping.Forever), SoundMixer.FullVolume);
        var mixer = new SoundMixer(device, _ => entry);
        var space = new ArenaSpace();
        var runtime = new ScriptRuntime(new LevelData(data, Level), Cmi.Load(data.PathOf($"TRAVERSE/LEVEL{Level}/LEVEL{Level}.CMI")),
            Bni.Load(data.PathOf("TRAVERSE/TRAVSPRT.BNI")), space, new TriangleGroups(), mixer, new Kurt.Kurt(space, mixer, _ => 1));
        var obj = new MdkObject();
        runtime.PlaySound(obj, "A", Following, null);
        Assert.True(mixer.IsPlaying("A"));

        runtime.Remove(obj);

        Assert.False(mixer.IsPlaying("A"));
    }
}
