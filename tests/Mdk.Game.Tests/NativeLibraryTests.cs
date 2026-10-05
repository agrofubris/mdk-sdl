using Mdk.Engine.Audio;

namespace Mdk.Game.Tests;

/// <summary>SDL3 is unpacked and routed once; callers racing to it must all find it ready.</summary>
public class NativeLibraryTests
{
    private const int Callers = 16;

    [Fact]
    public void ParallelFirstUsesFindSdl()
    {
        using var start = new Barrier(Callers);
        var failures = 0;
        var threads = Enumerable.Range(0, Callers).Select(_ => new Thread(() =>
        {
            start.SignalAndWait();
            try
            {
                using var device = new AudioDevice(Output.Muted);
            }
            catch (DllNotFoundException)
            {
                Interlocked.Increment(ref failures);
            }
        })).ToList();

        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());
        Assert.Equal(0, failures);
    }
}
