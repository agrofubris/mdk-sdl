using System.Runtime.InteropServices;
using SDL;
using static SDL.SDL3;

namespace Mdk.Engine.Audio;

/// <summary>Sound data the mixer can play: samples as floats, interleaved when stereo.</summary>
public sealed class Sound(float[] samples, int channels, int sampleRate, Looping looping)
{
    public float[] Samples { get; } = samples;
    public int Channels { get; } = channels;
    public int SampleRate { get; } = sampleRate;
    public Looping Looping { get; } = looping;
    public int Frames => Samples.Length / Channels;

    /// <summary>From PCM: 8-bit unsigned or 16-bit signed little-endian.</summary>
    public static Sound FromPcm(byte[] data, int channels, int sampleRate, int bitsPerSample, Looping looping)
    {
        const float ByteScale = 1f / 128f;
        const float ShortScale = 1f / 32768f;
        const int ByteMiddle = 128;
        if (bitsPerSample == 8)
        {
            return new Sound(data.Select(b => (b - ByteMiddle) * ByteScale).ToArray(), channels, sampleRate, looping);
        }

        var shorts = MemoryMarshal.Cast<byte, short>(data.AsSpan(0, data.Length & ~1));
        var samples = new float[shorts.Length];
        for (var i = 0; i < shorts.Length; i++)
        {
            samples[i] = shorts[i] * ShortScale;
        }

        return new Sound(samples, channels, sampleRate, looping);
    }
}

public enum Looping { Once, Forever }

/// <summary>Voices mix into a bus, each with its volume: the music (with its filter) or the effects.</summary>
public enum Bus { Effects, Music }

/// <summary>The music's low-pass filter on or off.</summary>
public enum Filter { Off, On }

/// <summary>Whether sounds reach the speakers or are mixed silently (tests).</summary>
public enum Output { Speakers, Muted }

/// <summary>The sound output: a software mixer of voices pushed to an SDL audio stream (stereo
/// float). Each voice has a gain, a pitch (a multiple of its sound's rate) and a pan (-1 left, 1 right,
/// the far channel attenuated like DirectSound's).
/// <code>
///   voices ──resample, gain, pan──► mix buffer ──► SDL audio stream ──► device
/// </code>
/// <see cref="Update"/> tops the stream up once a frame. Voices go through a bus (music or
/// effects), the master volume and a limiter; a streamed voice plays what is pushed to it (a movie's sound).
/// <code>
///   effects voices ─────────────────────┐
///   music voices ──low-pass (option)─────┼──master──limiter──► stream
/// </code></summary>
public sealed unsafe class AudioDevice : IDisposable
{
    private const int OutputRate = 44100;
    private const int OutputChannels = 2;
    /// <summary>Audio kept queued ahead: enough to cover a slow frame, short enough to stay in sync.</summary>
    private const float QueueSeconds = 0.06f;
    private const int MaxVoices = 64;
    /// <summary>DirectSound pan: the far channel loses up to 100 dB.</summary>
    private const float PanRangeDb = 100f;
    /// <summary>The music is 8-bit at 14-20 kHz and hisses: the filter cuts it above 6 kHz.</summary>
    private const float MusicCutoff = 6000f;

    private sealed class Voice(Sound sound, float gain, float pitch, float pan, Bus bus)
    {
        public readonly Sound Sound = sound;
        public readonly Bus Bus = bus;
        public float Gain = gain;
        public float Pitch = pitch;
        public float Pan = pan;
        public double Position;
        /// <summary>A streamed voice: frames pushed and not played yet (stereo).</summary>
        public Queue<(float Left, float Right)>? Pending;
    }

    private readonly SDL_AudioStream* _stream;
    private readonly Dictionary<int, Voice> _voices = [];
    private float[] _mix = [];
    private float[] _music = [];
    private readonly float[] _busGains = [1f, 1f];
    private (float Left, float Right) _filtered;
    private readonly Limiter _limiter = new(OutputRate);
    private int _nextId = 1;

    /// <summary>The volume of everything (0-1).</summary>
    public float MasterGain { get; set; } = 1f;
    public Filter MusicFilter { get; set; } = Filter.On;

    /// <summary>A bus's volume (0-1).</summary>
    public void SetBusGain(Bus bus, float gain) => _busGains[(int)bus] = gain;

    /// <summary>Opens the default output; muted or without one, sounds are mixed and dropped.</summary>
    public AudioDevice(Output output)
    {
        if (output == Output.Muted || !SDL_InitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO))
        {
            return;
        }

        var spec = new SDL_AudioSpec { format = SDL_AudioFormat.SDL_AUDIO_F32LE, channels = OutputChannels, freq = OutputRate };
        _stream = SDL_OpenAudioDeviceStream(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &spec, null, IntPtr.Zero);
        if (_stream != null)
        {
            SDL_ResumeAudioStreamDevice(_stream);
        }
    }

    /// <summary>Starts a voice; returns its id, or 0 when all voices are busy.</summary>
    public int Play(Sound sound, float gain, float pitch = 1f, float pan = 0f, Bus bus = Bus.Effects)
    {
        if (_voices.Count >= MaxVoices)
        {
            return 0;
        }

        _voices[_nextId] = new Voice(sound, gain, pitch, pan, bus);
        return _nextId++;
    }

    /// <summary>Starts a streamed voice at <paramref name="sampleRate"/>: it plays what
    /// <see cref="Push"/> gives it, silent while it waits. Returns its id, or 0.</summary>
    public int PlayStream(int sampleRate, float gain)
    {
        var id = Play(new Sound([0f, 0f], 2, sampleRate, Looping.Forever), gain);
        if (id != 0)
        {
            _voices[id].Pending = new Queue<(float, float)>();
        }

        return id;
    }

    /// <summary>Stereo frames for a streamed voice.</summary>
    public void Push(int voice, IEnumerable<(float Left, float Right)> frames)
    {
        if (!_voices.TryGetValue(voice, out var v) || v.Pending == null)
        {
            return;
        }

        foreach (var frame in frames)
        {
            v.Pending.Enqueue(frame);
        }
    }

    /// <summary>Seconds of sound a streamed voice still has to play.</summary>
    public float Queued(int voice) =>
        _voices.TryGetValue(voice, out var v) && v.Pending != null ? (float)v.Pending.Count / v.Sound.SampleRate : 0f;

    /// <summary>Stops every voice (a screen of the game ends).</summary>
    public void StopAll() => _voices.Clear();

    public bool IsPlaying(int voice) => _voices.ContainsKey(voice);

    public void Set(int voice, float gain, float pitch, float pan)
    {
        if (!_voices.TryGetValue(voice, out var v))
        {
            return;
        }

        v.Gain = gain;
        v.Pitch = pitch;
        v.Pan = pan;
    }

    public void Stop(int voice) => _voices.Remove(voice);

    /// <summary>A voice's gain, 0 when it isn't playing.</summary>
    public float GainOf(int voice) => _voices.TryGetValue(voice, out var v) ? v.Gain : 0f;

    /// <summary>Mixes what the stream needs to stay <see cref="QueueSeconds"/> ahead; without a
    /// device, the <paramref name="delta"/> seconds that passed.</summary>
    public void Update(float delta)
    {
        var bytesPerFrame = OutputChannels * sizeof(float);
        var frames = _stream == null
            ? (int)(OutputRate * delta)
            : (int)(OutputRate * QueueSeconds) - SDL_GetAudioStreamQueued(_stream) / bytesPerFrame;
        if (frames <= 0)
        {
            return;
        }

        if (_mix.Length < frames * OutputChannels)
        {
            _mix = new float[frames * OutputChannels];
        }

        if (_music.Length < frames * OutputChannels)
        {
            _music = new float[frames * OutputChannels];
        }

        var mix = _mix.AsSpan(0, frames * OutputChannels);
        var music = _music.AsSpan(0, frames * OutputChannels);
        mix.Clear();
        music.Clear();
        foreach (var (id, voice) in _voices.ToList())
        {
            var mixed = voice.Pending != null ? MixStream(voice, mix, frames) : MixVoice(voice, voice.Bus == Bus.Music ? music : mix, frames);
            if (!mixed)
            {
                _voices.Remove(id);
            }
        }

        AddMusic(mix, music, frames);
        _limiter.Process(mix);

        if (_stream == null)
        {
            return;
        }

        fixed (float* data = mix)
        {
            SDL_PutAudioStreamData(_stream, (IntPtr)data, mix.Length * sizeof(float));
        }
    }

    /// <summary>The music bus, filtered (one-pole low-pass), into the mix; then the volumes.</summary>
    private void AddMusic(Span<float> mix, Span<float> music, int frames)
    {
        var alpha = 1f - MathF.Exp(-2f * MathF.PI * MusicCutoff / OutputRate);
        var effects = _busGains[(int)Bus.Effects] * MasterGain;
        var musicGain = _busGains[(int)Bus.Music] * MasterGain;
        for (var f = 0; f < frames; f++)
        {
            var (l, r) = (music[f * 2], music[f * 2 + 1]);
            if (MusicFilter == Filter.On)
            {
                _filtered = (_filtered.Left + (l - _filtered.Left) * alpha, _filtered.Right + (r - _filtered.Right) * alpha);
                (l, r) = _filtered;
            }

            mix[f * 2] = mix[f * 2] * effects + l * musicGain;
            mix[f * 2 + 1] = mix[f * 2 + 1] * effects + r * musicGain;
        }
    }

    /// <summary>A streamed voice into the mix (nearest sample: its rate is close to the output's).
    /// It never ends by itself.</summary>
    private static bool MixStream(Voice voice, Span<float> mix, int frames)
    {
        var step = (double)voice.Sound.SampleRate / OutputRate;
        for (var f = 0; f < frames && voice.Pending!.Count > 0; f++)
        {
            var (l, r) = voice.Pending.Peek();
            mix[f * 2] += l * voice.Gain;
            mix[f * 2 + 1] += r * voice.Gain;
            voice.Position += step;
            while (voice.Position >= 1d && voice.Pending.Count > 0)
            {
                voice.Position -= 1d;
                voice.Pending.Dequeue();
            }
        }

        return true;
    }

    /// <summary>Adds a voice to the mix (linear interpolation). Returns false once it has ended.</summary>
    private static bool MixVoice(Voice voice, Span<float> mix, int frames)
    {
        var sound = voice.Sound;
        var step = voice.Pitch * sound.SampleRate / OutputRate;
        var far = MathF.Pow(10f, -PanRangeDb * Math.Abs(voice.Pan) / 20f);
        var left = voice.Gain * (voice.Pan > 0f ? far : 1f);
        var right = voice.Gain * (voice.Pan < 0f ? far : 1f);
        for (var f = 0; f < frames; f++)
        {
            if (voice.Position >= sound.Frames)
            {
                if (sound.Looping == Looping.Once)
                {
                    return false;
                }

                voice.Position -= sound.Frames;
            }

            var i = (int)voice.Position;
            var t = (float)(voice.Position - i);
            var next = i + 1 < sound.Frames ? i + 1 : (sound.Looping == Looping.Forever ? 0 : i);
            var (l, r) = Sample(sound, i);
            var (nl, nr) = Sample(sound, next);
            mix[f * 2] += (l + (nl - l) * t) * left;
            mix[f * 2 + 1] += (r + (nr - r) * t) * right;
            voice.Position += step;
        }

        return true;
    }

    private static (float Left, float Right) Sample(Sound sound, int frame)
    {
        if (sound.Channels == 1)
        {
            var s = sound.Samples[frame];
            return (s, s);
        }

        return (sound.Samples[frame * 2], sound.Samples[frame * 2 + 1]);
    }

    public void Dispose()
    {
        if (_stream != null)
        {
            SDL_DestroyAudioStream(_stream);
        }

        SDL_QuitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO);
    }
}
