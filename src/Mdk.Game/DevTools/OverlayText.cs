using System.Globalization;
using Mdk.Engine.Diagnostics;
using Mdk.Engine.Render;

namespace Mdk.Game.DevTools;

/// <summary>The program's memory: the GC heap, the working set and the collections per generation.</summary>
public readonly record struct MemorySample(long GcHeap, long WorkingSet, IReadOnlyList<int> Collections)
{
    public static MemorySample Now() =>
        new(GC.GetTotalMemory(false), Environment.WorkingSet,
            Enumerable.Range(0, GC.MaxGeneration + 1).Select(GC.CollectionCount).ToList());
}

/// <summary>What the debug overlay shows of a frame: timings, draws, memory, and the screen's own
/// lines (a menu's name, the level's Kurt, the stream's segment).</summary>
public readonly record struct OverlaySample(Profiler Profiler, RenderStats Render, MemorySample Memory, IReadOnlyList<string> Screen);

/// <summary>The debug overlay's lines; the last are the screen's (a level's here).
/// <code>
///   FPS 60 (16.7 ms)
///   scene 0.3  render 5.2  physics 0.4  scripts 1.1  audio 0.2 ms
///   draw calls 120, triangles 4500, GPU wait 4.1 ms   (render includes the wait: vsync, the GPU)
///   GC heap 12.0 MB, working set 80.0 MB, collections 3/2/1
///   Kurt 1.00 -2.50 30.00 yaw 270 Run
///   level 6 arena ARENA_2, objects 42
/// </code></summary>
public static class OverlayText
{
    private const double Megabyte = 1024.0 * 1024.0;

    public static IReadOnlyList<string> Lines(OverlaySample sample)
    {
        var profiler = sample.Profiler;
        var sections = string.Join("  ", Enum.GetValues<Section>().Select(s => $"{s.ToString().ToLowerInvariant()} {Format(profiler.Milliseconds(s), "0.0")}"));
        var memory = sample.Memory;
        return
        [
            $"FPS {Format(profiler.FramesPerSecond, "0")} ({Format(profiler.FrameMilliseconds, "0.0")} ms)",
            $"{sections} ms",
            $"draw calls {sample.Render.DrawCalls}, triangles {sample.Render.Triangles}, GPU wait {Format(sample.Render.GpuWait, "0.0")} ms",
            $"GC heap {Format(memory.GcHeap / Megabyte, "0.0")} MB, working set {Format(memory.WorkingSet / Megabyte, "0.0")} MB, collections {string.Join('/', memory.Collections)}",
            .. sample.Screen,
        ];
    }

    /// <summary>A level's lines: Kurt, his state, the level, his arena and the objects.</summary>
    public static IReadOnlyList<string> Level(Placement kurt, int objects, string state)
    {
        var feet = kurt.Feet;
        return
        [
            $"Kurt {Format(feet.X, "0.00")} {Format(feet.Y, "0.00")} {Format(feet.Z, "0.00")} yaw {Format(kurt.Yaw, "0")} {state}",
            $"level {kurt.Level} arena {kurt.Arena}, objects {objects}",
        ];
    }

    private static string Format(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
}
