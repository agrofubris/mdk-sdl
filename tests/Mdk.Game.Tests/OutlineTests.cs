using System.Numerics;
using Mdk.Formats;
using Mdk.Game.Level;

namespace Mdk.Game.Tests;

/// <summary>Outlined edges of arena triangles (flag bit 23, edges bits 20-22; _add_outlines).</summary>
public class OutlineTests
{
    private const uint V0V1 = 1u << 20;
    private const uint V1V2 = 1u << 21;
    private const uint V2V0 = 1u << 22;

    private static Arena TwoTriangles(uint first, uint second) => new()
    {
        Vertices = [Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ],
        TriangleIndices = [0, 1, 2, 1, 2, 3],
        TriangleFlags = [first, second],
    };

    [Fact]
    public void EdgeBitsPickTheEdgesOfOutlinedTriangles()
    {
        var arena = TwoTriangles(Outlines.Outlined | V0V1 | V2V0, Outlines.Outlined | V1V2);
        var lines = Outlines.Of(arena, [0, 1]);
        Assert.Equal([Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.Zero, Vector3.UnitY, Vector3.UnitZ], lines);
    }

    [Fact]
    public void EdgeBitsWithoutTheOutlineFlagDrawNothing()
    {
        var arena = TwoTriangles(V0V1 | V1V2 | V2V0, 0);
        Assert.Empty(Outlines.Of(arena, [0, 1]));
    }

    [Fact]
    public void OnlyTheGivenTrianglesCount()
    {
        var arena = TwoTriangles(Outlines.Outlined | V0V1, Outlines.Outlined | V0V1);
        Assert.Equal(2, Outlines.Of(arena, [1]).Count);
    }
}
