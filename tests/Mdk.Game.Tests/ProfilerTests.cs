using System.Diagnostics;
using Mdk.Engine.Diagnostics;

namespace Mdk.Game.Tests;

/// <summary>The debug overlay's timings: sections of a frame, averaged over frames.</summary>
public class ProfilerTests
{
    [Fact]
    public void SectionsAddUpWithinAFrame()
    {
        var profiler = new Profiler();
        var ticks = Stopwatch.Frequency / 1000;
        profiler.Add(Section.Scripts, ticks);
        profiler.Add(Section.Scripts, ticks);
        profiler.EndFrame();

        // The first frame sets the average.
        Assert.Equal(2f, profiler.Milliseconds(Section.Scripts), 3);
        Assert.Equal(0f, profiler.Milliseconds(Section.Render));
    }

    [Fact]
    public void MeasureTimesItsScope()
    {
        var profiler = new Profiler();
        using (profiler.Measure(Section.Render))
        {
            Thread.Sleep(5);
        }

        profiler.EndFrame();
        Assert.True(profiler.Milliseconds(Section.Render) >= 4f);
    }
}
