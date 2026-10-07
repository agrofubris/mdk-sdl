using System.Numerics;
using Mdk.Formats;
using Mdk.Game.Level;

namespace Mdk.Game.Tests;

/// <summary>Depth layers of coplanar arena triangles (details drawn over the surfaces they lie on).</summary>
public class LayerTests
{
    /// <summary>Level 6's OLYM_5: a hub's rim (159) lies half on the glass floor around it (115).</summary>
    private const string Hubs = "OLYM_5";
    private const int HubRim = 159;
    private const int GlassFloor = 115;

    /// <summary>Level 6's OLYM_7: a dark floor triangle (2) under a mirror tile (56).</summary>
    private const string Mirrors = "OLYM_7";
    private const int DarkFloor = 2;
    private const int MirrorTile = 56;

    /// <summary>Level 6's arenas, each parsed when asked for.</summary>
    private static readonly Lazy<Mto> Level6 = new(() => Mto.Load(MdkData.Find()!.PathOf("TRAVERSE/LEVEL6/LEVEL6O.MTO")));

    /// <summary>Triangles in z = 0 (three corners each, wound as MDK's floors).</summary>
    private static Arena Flat(params Vector2[][] triangles) => new()
    {
        Vertices = [.. triangles.SelectMany(t => t.Select(c => new Vector3(c, 0f)))],
        TriangleIndices = [.. Enumerable.Range(0, triangles.Length * 3)],
        TriangleMaterials = new int[triangles.Length],
        TriangleFlags = new uint[triangles.Length],
    };

    private static readonly Vector2[] Wall = [new(0f, 0f), new(0f, 10f), new(10f, 0f)];

    [Fact]
    public void APosterOnAWallIsOneLayerUp()
    {
        var arena = Flat(Wall, [new(1f, 1f), new(1f, 2f), new(2f, 1f)]);
        Assert.Equal([0, 1], Layers.Of(arena));
    }

    [Fact]
    public void APosterHalfOnAWallIsOneLayerUp()
    {
        // Its centre (9, 1.3) is off the wall, a corner on it.
        var arena = Flat(Wall, [new(7f, 1f), new(7f, 2f), new(13f, 1f)]);
        Assert.Equal([0, 1], Layers.Of(arena));
    }

    [Fact]
    public void NeighboursSharingAnEdgeStayFlat()
    {
        var arena = Flat(Wall, [new(0f, 10f), new(10f, 10f), new(10f, 0f)]);
        Assert.Equal([0, 0], Layers.Of(arena));
    }

    [Fact]
    public void APosterOnAPosterIsTwoLayersUp()
    {
        var arena = Flat(Wall, [new(1f, 1f), new(1f, 5f), new(5f, 1f)], [new(1.5f, 1.5f), new(1.5f, 2f), new(2f, 1.5f)]);
        Assert.Equal([0, 1, 2], Layers.Of(arena));
    }

    [Fact]
    public void OfTwoEqualTrianglesTheLaterIsOnTop()
    {
        var arena = Flat(Wall, Wall);
        Assert.Equal([0, 1], Layers.Of(arena));
    }

    [Fact]
    public void OtherPlanesStayFlat()
    {
        var arena = Flat(Wall, [new(1f, 1f), new(1f, 2f), new(2f, 1f)]);
        arena.Vertices[3].Z = arena.Vertices[4].Z = arena.Vertices[5].Z = 1f;
        Assert.Equal([0, 0], Layers.Of(arena));
    }

    [DataFact]
    public void AHubRimHalfOnGlassIsOverIt()
    {
        var arena = Level6.Value.GetArena(Hubs);
        var layers = Layers.Of(arena);
        Assert.True(layers[HubRim] > layers[GlassFloor]);
    }

    [DataFact]
    public void AFloorHalfUnderAMirrorTileIsOverIt()
    {
        var arena = Level6.Value.GetArena(Mirrors);
        var layers = Layers.Of(arena);
        Assert.True(layers[DarkFloor] > layers[MirrorTile]);
    }
}
