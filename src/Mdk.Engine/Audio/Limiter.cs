namespace Mdk.Engine.Audio;

/// <summary>A hard limiter on the master mix (settings.gd: Godot's AudioEffectHardLimiter on
/// <c>Master</c>) so that many sounds at once don't clip. Both channels share one gain: a peak over
/// the ceiling lowers it at once, then it recovers over the release time.
/// <code>
///   peak 2.0 ─► gain 0.47 (out 0.94) ─► ~0.1 s later back toward 1
/// </code></summary>
internal sealed class Limiter(int sampleRate)
{
    private const float CeilingDb = -0.5f;
    /// <summary>Godot's default release.</summary>
    private const float ReleaseSeconds = 0.1f;

    /// <summary>The highest sample out (linear, -0.5 dB).</summary>
    public static readonly float Ceiling = MathF.Pow(10f, CeilingDb / 20f);

    /// <summary>The gain's step back toward 1 each frame (one-pole).</summary>
    private readonly float _release = 1f - MathF.Exp(-1f / (ReleaseSeconds * sampleRate));
    private float _gain = 1f;

    /// <summary>Limits interleaved stereo samples in place.</summary>
    public void Process(Span<float> stereo)
    {
        for (var f = 0; f + 1 < stereo.Length; f += 2)
        {
            var peak = MathF.Max(MathF.Abs(stereo[f]), MathF.Abs(stereo[f + 1]));
            var limit = peak > Ceiling ? Ceiling / peak : 1f;
            _gain = MathF.Min(_gain + (1f - _gain) * _release, limit);
            if (_gain >= 1f)
            {
                continue;
            }

            stereo[f] *= _gain;
            stereo[f + 1] *= _gain;
        }
    }
}
