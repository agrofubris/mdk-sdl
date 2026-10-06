using System.Numerics;
using Mdk.Formats;
using Mdk.Game.Level;
using Mdk.Game.Scripts;

namespace Mdk.Game.Tests;

/// <summary>The twisters' ribbon (0x4392a4, 0x439690).</summary>
public class RibbonTests
{
    [Fact]
    public void OnlyTheNewestTwoEdgesKeepTheirSize()
    {
        // Integer division: (i + 1 + 5 - n) / 4.
        Assert.Equal([0f, 0f, 0f, 1f, 1f], Enumerable.Range(0, 5).Select(i => Ribbon.ScaleOf(i, 5)));
        Assert.Equal([0f, 1f, 1f], Enumerable.Range(0, 3).Select(i => Ribbon.ScaleOf(i, 3)));
    }

    [Fact]
    public void KeepsTheLastFivePositions()
    {
        var ribbon = new Ribbon();
        for (var i = 0; i < 7; i++)
        {
            ribbon.Push(Vector3.UnitX * i);
        }

        Assert.Equal(Ribbon.Length, ribbon.Centres.Count);
        Assert.Equal(Vector3.UnitX * 2, ribbon.Centres.First());
    }

    [Fact]
    public void TwoQuadsJoinTheLastEdges()
    {
        var ribbon = new Ribbon();
        ribbon.Push(Vector3.Zero);
        Assert.Empty(ribbon.Triangles());

        ribbon.Push(Vector3.UnitX);
        var corners = ribbon.Triangles();
        // One segment: two triangles, from the bottom of the old edge, rows 0.5-16 of the texture.
        Assert.Equal(6, corners.Count);
        Assert.Equal((-Vector3.UnitZ, new Vector2(0.5f, 0.5f)), corners[0]);
        Assert.Equal((Vector3.UnitX + Vector3.UnitZ, new Vector2(15.5f, 16f)), corners[2]);
    }

    [DataFact]
    public void EveryLevelHasTheRibbonTexture()
    {
        var data = MdkData.Find()!;
        foreach (var number in Enumerable.Range(3, 6))
        {
            Assert.True(new LevelData(data, number).LevelTextures.Textures.ContainsKey(Ribbon.Texture));
        }
    }
}
