using System.Numerics;
using Mdk.Game.Objects;

namespace Mdk.Game.Tests;

/// <summary>Objects' rope lines (update_ropes): a swing's rope, or opcode 242's lines.</summary>
public class RopeTests
{
    private static readonly Vector3 Pivot = new(10f, 20f, 50f);

    [Fact]
    public void SwingRopeGoesFromAboveTheObjectToThePivot()
    {
        var obj = new MdkObject { Position = new Vector3(10f, 20f, 0f), RopeMask = 1 };
        obj.RopePoints[0] = obj.Position;
        obj.RopePoints[1] = Pivot;
        Assert.Equal([new Vector3(10f, 20f, 5f), Pivot], RopeView.LinesOf(obj));
    }

    [Fact]
    public void AllLinesSkipZeroPointsAndStartAtTheObjectWithoutModel()
    {
        var obj = new MdkObject { Position = Vector3.UnitX, RopeMask = RopeView.AllLines };
        obj.RopePoints[1] = Pivot;
        // Without a model a reference point is the object's position.
        Assert.Equal([Vector3.UnitX, Pivot], RopeView.LinesOf(obj));
    }

    [Fact]
    public void NoMaskNoLines() => Assert.Empty(RopeView.LinesOf(new MdkObject()));
}
