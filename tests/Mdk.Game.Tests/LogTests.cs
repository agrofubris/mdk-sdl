using Mdk.Engine.Diagnostics;

namespace Mdk.Game.Tests;

/// <summary>The log the console shows: the last lines printed, still written to the output.</summary>
public class LogTests
{
    [Fact]
    public void RingKeepsTheLastLines()
    {
        var ring = new LogRing(3);
        foreach (var line in new[] { "a", "b", "c", "d" })
        {
            ring.Add(line);
        }

        Assert.Equal(["b", "c", "d"], ring.Lines());
        Assert.Equal(["c", "d"], ring.Lines(2));

        ring.Clear();
        Assert.Empty(ring.Lines());
    }

    [Fact]
    public void WriterCopiesWholeLinesToTheRing()
    {
        var ring = new LogRing(10);
        var output = new StringWriter();
        var writer = new LogWriter(output, ring);

        // Parts of a line wait for its end; \r\n ends a line too.
        writer.Write("Level 3: ");
        writer.Write(42);
        writer.WriteLine(" arenas");
        writer.Write("one\r\ntwo\nthree");

        Assert.Equal("Level 3: 42 arenas" + Environment.NewLine + "one\r\ntwo\nthree", output.ToString());
        Assert.Equal(["Level 3: 42 arenas", "one", "two"], ring.Lines());
    }

    [Fact]
    public void RingWritesItsLinesToAFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mdk_log_{Guid.NewGuid():N}.log");
        var ring = new LogRing(10);
        ring.Add("before");

        // The lines kept so far, then each new one as it comes.
        ring.WriteTo(path);
        ring.Add("after");
        ring.Close();

        Assert.Equal(["before", "after"], File.ReadAllLines(path));
        File.Delete(path);
    }
}
