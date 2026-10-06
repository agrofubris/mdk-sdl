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

    private const uint AllEdges = Outlines.Outlined | V0V1 | V1V2 | V2V0;

    /// <summary>Level 7's DANT_7 glass column: a fan triangle (t6) flags its diagonal, as the
    /// editor gave the fan the flags of a quad's half.</summary>
    private const string Shaft = "DANT_7";
    private const int FanTriangle = 6;
    private static readonly (Vector3 From, Vector3 To) FanDiagonal = (new(406f, 4450f, -226f), new(427f, 4429f, -204f));
    private static readonly Lazy<LevelData> Level7 = new(() => new LevelData(MdkData.Find()!, 7));

    private static Arena TwoTriangles(uint first, uint second) => new()
    {
        Vertices = [Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ],
        TriangleIndices = [0, 1, 2, 1, 2, 3],
        TriangleFlags = [first, second],
    };

    /// <summary>A unit square in z = 0, split along v1-v2, both halves flagging every edge.</summary>
    private static Arena Square() => new()
    {
        Vertices = [Vector3.Zero, Vector3.UnitX, Vector3.UnitY, new Vector3(1f, 1f, 0f)],
        TriangleIndices = [0, 1, 2, 2, 1, 3],
        TriangleFlags = [AllEdges, AllEdges],
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

    [Fact]
    public void FramesDropEdgesInsideAFlatSurface()
    {
        var arena = Square();
        Assert.Equal(12, Outlines.Of(arena, [0, 1]).Count);
        Assert.Equal(8, Outlines.Of(arena, [0, 1], OutlineEdges.Frame).Count);
    }

    [Fact]
    public void FramesKeepCorners()
    {
        // The two triangles share v1-v2 at an angle: a corner, not a diagonal.
        var arena = TwoTriangles(Outlines.Outlined | V1V2, Outlines.Outlined | V0V1);
        Assert.Equal(4, Outlines.Of(arena, [0, 1], OutlineEdges.Frame).Count);
    }

    [DataFact]
    public void ColumnFramesSkipTheFanDiagonal()
    {
        var arena = Level7.Value.Arenas.First(a => a.Name == Shaft);
        Assert.True(HasLine(Outlines.Of(arena, [FanTriangle]), FanDiagonal));

        var glass = Enumerable.Range(0, arena.TriangleCount).Where(t => arena.TriangleMaterials[t] == arena.TriangleMaterials[FanTriangle]);
        Assert.False(HasLine(Outlines.Of(arena, glass, OutlineEdges.Frame), FanDiagonal));
    }

    private static bool HasLine(List<Vector3> lines, (Vector3 From, Vector3 To) line)
    {
        for (var i = 0; i < lines.Count; i += 2)
        {
            if ((lines[i] == line.From && lines[i + 1] == line.To) || (lines[i] == line.To && lines[i + 1] == line.From))
            {
                return true;
            }
        }

        return false;
    }
}
