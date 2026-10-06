using Mdk.Engine.Audio;

namespace Mdk.Game.Tests;

/// <summary>The master limiter (settings.gd: AudioEffectHardLimiter on Master, ceiling -0.5 dB).</summary>
public class LimiterTests
{
    private const int Rate = 44100;
    private const int Channels = 2;
    private const float Hertz = 440f;
    private const float Tolerance = 1e-6f;

    /// <summary>A stereo sine of <paramref name="amplitude"/>, <paramref name="seconds"/> long.</summary>
    private static float[] Sine(float amplitude, float seconds)
    {
        var frames = (int)(Rate * seconds);
        var samples = new float[frames * Channels];
        for (var f = 0; f < frames; f++)
        {
            var s = amplitude * MathF.Sin(2f * MathF.PI * Hertz * f / Rate);
            samples[f * Channels] = s;
            samples[f * Channels + 1] = s;
        }

        return samples;
    }

    [Fact]
    public void LoudSoundStaysUnderTheCeiling()
    {
        var limiter = new Limiter(Rate);
        var samples = Sine(4f, 0.5f);
        limiter.Process(samples);
        Assert.All(samples, s => Assert.True(MathF.Abs(s) <= Limiter.Ceiling + Tolerance));
        Assert.True(samples.Max() > Limiter.Ceiling * 0.99f);
    }

    [Fact]
    public void QuietSoundPassesUnchanged()
    {
        var limiter = new Limiter(Rate);
        var samples = Sine(0.5f, 0.5f);
        var original = (float[])samples.Clone();
        limiter.Process(samples);
        Assert.Equal(original, samples);
    }

    [Fact]
    public void GainRecoversAfterAPeak()
    {
        var limiter = new Limiter(Rate);
        limiter.Process(Sine(4f, 0.1f));

        // A second later the quiet sound is back to (almost) its own level.
        limiter.Process(Sine(0.5f, 1f));
        var after = Sine(0.5f, 0.1f);
        limiter.Process(after);
        Assert.Equal(0.5f, after.Max(), 2);
    }
}
