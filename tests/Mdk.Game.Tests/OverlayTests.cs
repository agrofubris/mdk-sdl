using System.Numerics;
using Mdk.Engine.Diagnostics;
using Mdk.Engine.Render;
using Mdk.Game.DevTools;

namespace Mdk.Game.Tests;

/// <summary>The debug overlay's text.</summary>
public class OverlayTests
{
    private const long Megabyte = 1024 * 1024;

    [Fact]
    public void LinesShowTimingsRendererMemoryAndKurt()
    {
        var profiler = new Profiler();
        var sample = new OverlaySample(profiler, new RenderStats(120, 4500),
            new MemorySample(12 * Megabyte, 80 * Megabyte, [3, 2, 1]),
            new Placement(6, "ARENA_2", new Vector3(1f, -2.5f, 30f), 270f), 42, "Run");

        var text = string.Join('\n', OverlayText.Lines(sample));
        Assert.Contains("draw calls 120, triangles 4500", text);
        Assert.Contains("GC heap 12.0 MB, working set 80.0 MB, collections 3/2/1", text);
        Assert.Contains("Kurt 1.00 -2.50 30.00 yaw 270 Run", text);
        Assert.Contains("level 6 arena ARENA_2, objects 42", text);
        Assert.Contains("render", text);
        Assert.Contains("audio", text);
    }
}
