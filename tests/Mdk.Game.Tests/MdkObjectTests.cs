using System.Numerics;
using Mdk.Formats;
using Mdk.Game.Objects;

namespace Mdk.Game.Tests;

/// <summary>Found by flaky test runs: every object shares one part bounds cache, and test classes
/// running in parallel corrupted it ("concurrent update was performed on this collection").</summary>
public class MdkObjectTests
{
    private const int Threads = 8;
    private const int ObjectsPerThread = 2000;

    /// <summary>Objects of different models on many threads at once all get their bounds.</summary>
    [Fact]
    public void PartBoundsAreThreadSafe()
    {
        Parallel.For(0, Threads, new ParallelOptions { MaxDegreeOfParallelism = Threads }, _ =>
        {
            for (var i = 0; i < ObjectsPerThread; i++)
            {
                var model = new Model { PartList = [new Model.Part { Vertices = [Vector3.Zero, new Vector3(i)] }] };
                var bounds = new MdkObject { Model = model }.PartBounds();
                Assert.Equal(new Vector3(i), bounds[0]!.Value.Max);
            }
        });
    }
}
