using System.Diagnostics;
using System.Globalization;
using Mdk.Engine.Diagnostics;
using Mdk.Engine.Render;

namespace Mdk.Game.DevTools;

/// <summary>A test run's frame costs (--perf): after <paramref name="warmup"/> seconds of game time,
/// each frame's sections, GPU wait, managed allocations and the collections, summed for a report.
/// <code>
///   Perf: 600 frames, 2.10 ms (476 FPS): scene 0.31 render 1.20 (GPU wait 0.80) physics 0.05 ...
///   Perf: allocated 0 B/frame (max 0), collections 0/0/0 in 1.3 s, load 850 ms, ...
/// </code></summary>
public sealed class PerfLog(float warmup)
{
    private const double Megabyte = 1024.0 * 1024.0;

    private readonly double[] _sections = new double[Enum.GetValues<Section>().Length];
    private readonly int[] _collections = new int[GC.MaxGeneration + 1];
    private readonly long _start = Stopwatch.GetTimestamp();
    private long _windowStart;
    private long _allocated = -1;
    private long _allocatedSum;
    private long _allocatedMax;
    private double _frameSum;
    private double _gpuWait;
    private int _count;
    private double _loadMilliseconds;
    private (long Allocated, float Frame, RenderStats Stats) _pending;
    private readonly float[] _pendingSections = new float[Enum.GetValues<Section>().Length];
    private RenderStats _draws;

    /// <summary>A level's loading time (the viewer's creation).</summary>
    public void Loaded(TimeSpan time) => _loadMilliseconds = time.TotalMilliseconds;

    /// <summary>A frame has ended at <paramref name="time"/> (game seconds of the screen).</summary>
    public void Frame(float time, Profiler profiler, RenderStats stats)
    {
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        if (time < warmup)
        {
            return;
        }

        // The first measured frame is the baseline.
        if (_allocated < 0)
        {
            _allocated = allocated;
            _windowStart = Stopwatch.GetTimestamp();
            for (var g = 0; g < _collections.Length; g++)
            {
                _collections[g] = GC.CollectionCount(g);
            }

            return;
        }

        // The previous frame is counted now: the last (the screenshot's download) never is.
        Count(_pending);
        _pending = (allocated - _allocated, profiler.LastFrameMilliseconds, stats);
        profiler.CopyLast(_pendingSections);
        _allocated = allocated;
    }

    private void Count((long Allocated, float Frame, RenderStats Stats) frame)
    {
        if (frame.Frame == 0f)
        {
            return;
        }

        var delta = frame.Allocated;
        _allocatedSum += delta;
        _allocatedMax = Math.Max(_allocatedMax, delta);
        _count++;
        _frameSum += frame.Frame;
        _gpuWait += frame.Stats.GpuWait;
        _draws = frame.Stats;
        for (var i = 0; i < _sections.Length; i++)
        {
            _sections[i] += _pendingSections[i];
        }
    }

    /// <summary>The report's lines (none before a measured frame).</summary>
    public IReadOnlyList<string> Report()
    {
        if (_count == 0)
        {
            return ["Perf: no frame measured"];
        }

        var frame = _frameSum / _count;
        var sections = string.Join(' ', Enum.GetValues<Section>().Select(s => $"{s.ToString().ToLowerInvariant()} {Ms(_sections[(int)s] / _count)}"));
        var seconds = Stopwatch.GetElapsedTime(_windowStart).TotalSeconds;
        var collections = string.Join('/', _collections.Select((start, g) => GC.CollectionCount(g) - start));
        return
        [
            $"Perf: {_count} frames, {Ms(frame)} ms ({F(1000.0 / frame, "0")} FPS): {sections} ms (GPU wait {Ms(_gpuWait / _count)})",
            $"Perf: allocated {_allocatedSum / _count} B/frame (max {_allocatedMax}), collections {collections} in {F(seconds, "0.0")} s, draw calls {_draws.DrawCalls}, triangles {_draws.Triangles}",
            $"Perf: load {F(_loadMilliseconds, "0")} ms, run {F(Stopwatch.GetElapsedTime(_start).TotalSeconds, "0.0")} s, GC heap {F(GC.GetTotalMemory(false) / Megabyte, "0.0")} MB (live {F(GC.GetTotalMemory(true) / Megabyte, "0.0")} MB), working set {F(Environment.WorkingSet / Megabyte, "0.0")} MB",
        ];
    }

    private static string Ms(double value) => F(value, "0.00");

    private static string F(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
}
