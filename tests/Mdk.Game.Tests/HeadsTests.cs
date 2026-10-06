using Mdk.Game.Menu;

namespace Mdk.Game.Tests;

/// <summary>The Score-O-matic's heads (0x433268): one row below 5, else two rows of (count + 1) / 2.</summary>
public class HeadsTests
{
    [Fact]
    public void FewHeadsMakeOneRow()
    {
        var places = HeadsView.Places(3, 3).ToList();
        Assert.Equal([-5f, 0f, 5f], places.Select(p => p.X));
        Assert.All(places, p => Assert.Equal(-3f, p.Z));
    }

    [Fact]
    public void ManyHeadsMakeTwoRowsOfAtMostEight()
    {
        var places = HeadsView.Places(20, 20).ToList();
        Assert.Equal(16, places.Count);
        Assert.Equal(8, places.Count(p => p.Z == -4.5f));
    }
}
