using System.Diagnostics;

namespace Mdk.Engine.Diagnostics;

/// <summary>The parts of a frame the profiler times.</summary>
public enum Section { Render, Physics, Scripts, Audio }

/// <summary>Times the sections of each frame and the frames themselves, averaged over the last
/// frames (the debug overlay shows them).
/// <code>
///   using (profiler.Measure(Section.Scripts)) { ... }   // adds to this frame's scripts time
///   profiler.EndFrame();                                 // averages, starts the next frame
/// </code></summary>
public sealed class Profiler
{
    /// <summary>Each frame weighs this much in the averages (about the last 10 frames).</summary>
    private const float Smoothing = 0.1f;
    private const double MillisecondsPerSecond = 1000.0;

    private readonly long[] _ticks = new long[Enum.GetValues<Section>().Length];
    private readonly float[] _average = new float[Enum.GetValues<Section>().Length];
    private long _frameStart = Stopwatch.GetTimestamp();
    private float _frame;
    private bool _started;

    /// <summary>Times its scope (a <c>using</c>) into <paramref name="section"/>.</summary>
    public readonly struct Measurement(Profiler profiler, Section section) : IDisposable
    {
        private readonly long _start = Stopwatch.GetTimestamp();

        public void Dispose() => profiler.Add(section, Stopwatch.GetTimestamp() - _start);
    }

    public Measurement Measure(Section section) => new(this, section);

    /// <summary>Adds <paramref name="ticks"/> (<see cref="Stopwatch"/>'s) to this frame's section.</summary>
    public void Add(Section section, long ticks) => _ticks[(int)section] += ticks;

    /// <summary>The frame is over: its times go into the averages.</summary>
    public void EndFrame()
    {
        var now = Stopwatch.GetTimestamp();
        var frame = Milliseconds(now - _frameStart);
        _frameStart = now;
        var weight = _started ? Smoothing : 1f;
        _started = true;
        _frame += (frame - _frame) * weight;
        for (var i = 0; i < _ticks.Length; i++)
        {
            _average[i] += (Milliseconds(_ticks[i]) - _average[i]) * weight;
            _ticks[i] = 0;
        }
    }

    /// <summary>A section's average time per frame.</summary>
    public float Milliseconds(Section section) => _average[(int)section];

    /// <summary>The average frame's time and the frames per second.</summary>
    public float FrameMilliseconds => _frame;

    public float FramesPerSecond => _frame > 0f ? (float)(MillisecondsPerSecond / _frame) : 0f;

    private static float Milliseconds(long ticks) => (float)(ticks * MillisecondsPerSecond / Stopwatch.Frequency);
}
