using Mdk.Engine.Audio;
using Mdk.Formats;

namespace Mdk.Game.Audio;

/// <summary>A level's sounds by name, from its SNI archives (<c>TRAVERSE.SNI</c>, <c>LEVELnS.SNI</c>,
/// <c>LEVELnO.SNI</c> with the music), converted once on first use. Later archives win.</summary>
public sealed class SoundBank(IReadOnlyList<Sni> archives)
{
    private readonly Dictionary<string, SoundMixer.Entry?> _cache = [];

    public static SoundBank ForLevel(MdkData data, int level)
    {
        var dir = $"TRAVERSE/LEVEL{level}/LEVEL{level}";
        return new SoundBank([
            Sni.Load(data.PathOf("TRAVERSE/TRAVERSE.SNI")),
            Sni.Load(data.PathOf(dir + "S.SNI")),
            Sni.Load(data.PathOf(dir + "O.SNI")),
        ]);
    }

    /// <summary>A level of the 1996 demo: its own archives only (sounds it lacks stay silent).</summary>
    public static SoundBank ForBeta(BetaDemo beta, int number) => new(beta.LoadSounds(BetaDemo.LevelOf(number)));

    public SoundMixer.Entry? Get(string name)
    {
        if (_cache.TryGetValue(name, out var entry))
        {
            return entry;
        }

        return _cache[name] = Load(name);
    }

    /// <summary>Kurt's sprite animations stored with the sounds (the board's <c>K_SURF</c>), or null.</summary>
    public SpriteAnimation? Animation(string name)
    {
        for (var i = archives.Count - 1; i >= 0; i--)
        {
            if (archives[i].GetAnimation(name) is { } animation)
            {
                return animation;
            }
        }

        return null;
    }

    private SoundMixer.Entry? Load(string name)
    {
        for (var i = archives.Count - 1; i >= 0; i--)
        {
            var archive = archives[i];
            var found = archive.Entries.FirstOrDefault(e => e.Key == name);
            if (found.Value == null || !archive.IsSound(found.Value))
            {
                continue;
            }

            var wav = Wav.Parse(archive.GetBytes(found.Value));
            if (wav == null)
            {
                return null;
            }

            var looping = (found.Value.Flags & Sni.FlagLoop) != 0 ? Looping.Forever : Looping.Once;
            var sound = Sound.FromPcm(wav.Data, wav.Channels, wav.SampleRate, wav.BitsPerSample, looping);
            return new SoundMixer.Entry(sound, found.Value.Volume);
        }

        return null;
    }
}
