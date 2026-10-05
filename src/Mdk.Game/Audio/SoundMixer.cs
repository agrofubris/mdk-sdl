using System.Numerics;
using Mdk.Engine.Audio;

namespace Mdk.Game.Audio;

/// <summary>The game's sound effects, mixed like the original (0x402b40-0x403c70, listener update
/// 0x403348; see godot-mdk docs/sound.md).
/// <list type="bullet">
/// <item>Volume is linear in decibels over 25 dB: <c>dB = -25 + 25 * volume / 0x7FFF</c> (never silent).</item>
/// <item>3D voices: full volume up to 20 units, linear to 0 at 250; Doppler at 1100 u/s (0.25-3).</item>
/// <item>A new sound is dropped when all voices are busy.</item>
/// </list>
/// <code>
///   Play("LAND")                     2D, the sound's default volume
///   PlayAt("EXPLODE", point)         3D, fixed at a point
///   PlayOn("DUMMY", () => position)  3D, following something
/// </code></summary>
public sealed class SoundMixer(AudioDevice device, Func<string, SoundMixer.Entry?> sounds)
{
    /// <summary>A new voice starts even if the sound plays (<c>New</c>), stops its voices first
    /// (<c>Restart</c>), or starts only if it isn't playing (<c>Once</c>).</summary>
    public enum Start { New, Restart, Once }

    /// <summary>A sound and its default volume (0-0x7FFF, from the SNI entry).</summary>
    public sealed record Entry(Sound Sound, int Volume);

    public const int FullVolume = 0x7FFF;
    private const float FloorDb = -25f;
    private const float Near = 20f;
    private const float Far = 250f;
    private const float SpeedOfSound = 1100f;
    private const float PitchMin = 0.25f;
    private const float PitchMax = 3f;

    private sealed class Voice(int id, string name, int volume, Func<Vector3>? position)
    {
        public readonly int Id = id;
        public readonly string Name = name;
        public int Volume = volume;
        public readonly Func<Vector3>? Position = position;
        /// <summary>Distance at the last update, -1 before the first.</summary>
        public float Distance = -1f;
    }

    private readonly List<Voice> _voices = [];

    /// <summary>The listener: position and right (MDK coordinates).</summary>
    public Vector3 ListenerPosition;
    public Vector3 ListenerRight = Vector3.UnitX;

    /// <summary>Plays a sound without position. Returns the voice, or 0.</summary>
    public int Play(string name, Start start = Start.New) => Launch(name, start, null);

    public int PlayAt(string name, Vector3 point, Start start = Start.New) => Launch(name, start, () => point);

    public int PlayOn(string name, Func<Vector3> position, Start start = Start.New) => Launch(name, start, position);

    public bool IsPlaying(string name) => _voices.Any(v => v.Name == name);

    public void Stop(string name)
    {
        foreach (var voice in _voices.Where(v => v.Name == name).ToList())
        {
            StopVoice(voice.Id);
        }
    }

    public void StopVoice(int id)
    {
        device.Stop(id);
        _voices.RemoveAll(v => v.Id == id);
    }

    /// <summary>A voice's volume (0-0x7FFF), e.g. the XD2's quieter DUMMY (0x4032e8).</summary>
    public void SetVolume(int id, int volume)
    {
        var voice = _voices.Find(v => v.Id == id);
        if (voice == null)
        {
            return;
        }

        voice.Volume = volume;
        if (voice.Position == null)
        {
            device.Set(id, Gain(volume), 1f, 0f);
        }
    }

    /// <summary>Decibels to a linear gain, for a volume (0-0x7FFF): linear over 25 dB.</summary>
    public static float Gain(float volume)
    {
        var db = FloorDb * (1f - Math.Clamp(volume, 0f, FullVolume) / FullVolume);
        return MathF.Pow(10f, db / 20f);
    }

    /// <summary>Once a frame: forgets ended voices and updates the 3D ones from the listener.</summary>
    public void Update(float delta)
    {
        _voices.RemoveAll(v => !device.IsPlaying(v.Id));
        foreach (var voice in _voices.Where(v => v.Position != null))
        {
            Update3D(voice, delta);
        }
    }

    private int Launch(string name, Start start, Func<Vector3>? position)
    {
        var entry = sounds(name);
        if (entry == null || (start == Start.Once && IsPlaying(name)))
        {
            return 0;
        }

        if (start == Start.Restart)
        {
            Stop(name);
        }

        var id = device.Play(entry.Sound, Gain(entry.Volume));
        if (id == 0)
        {
            return 0;
        }

        var voice = new Voice(id, name, entry.Volume, position);
        _voices.Add(voice);
        if (position != null)
        {
            Update3D(voice, 0f);
        }

        return id;
    }

    /// <summary>Volume, pan and pitch of a 3D voice from its place relative to the listener (0x40347c).</summary>
    private void Update3D(Voice voice, float delta)
    {
        var offset = voice.Position!() - ListenerPosition;
        var distance = Math.Max(offset.Length(), 1f);

        // Doppler: coming closer raises the pitch.
        var pitch = 1f;
        if (voice.Distance >= 0f && delta > 0f)
        {
            pitch = Math.Clamp(1f + (voice.Distance - distance) / (delta * SpeedOfSound), PitchMin, PitchMax);
        }

        voice.Distance = distance;
        var gain = Math.Clamp((Far - distance) / (Far - Near), 0f, 1f);
        var side = Vector3.Dot(offset, ListenerRight) / distance;
        device.Set(voice.Id, Gain(voice.Volume * gain), pitch, side);
    }
}
