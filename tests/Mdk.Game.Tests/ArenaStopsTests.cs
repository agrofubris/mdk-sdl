using System.Numerics;
using Mdk.Formats;
using Mdk.Game.Collision;
using Mdk.Game.Level;

namespace Mdk.Game.Tests;

/// <summary>LEVEL4's sub airlock: MEAT_5's floor (z -1989) lies 2 above CMEAT_5's. The stop by
/// door #1999 put Kurt on MEAT_5's floor (soak tour seed 1: "Kurt stands outside his arena");
/// the one by #1998 lands on CMEAT_5's own. MEAT_6 and CMEAT_6 lie under that floor everywhere
/// near their records: they keep a stop.</summary>
public class ArenaStopsTests
{
    private const int Level = 4;
    /// <summary>Floors seen below a stop (its records are at most this high above them).</summary>
    private const float Reach = 200f;
    /// <summary>Floors this close are one height (the soak's astray reach).</summary>
    private const float SameFloor = 1f;

    private static readonly Lazy<LevelData> Data = new(() => new LevelData(MdkData.Find()!, Level));
    private static readonly Lazy<ArenaSpace> Space = new(() => SpaceOf(Data.Value));

    private static ArenaSpace SpaceOf(LevelData level)
    {
        var space = new ArenaSpace();
        foreach (var arena in level.Arenas.Where(a => level.IsReachable(a.Name)))
        {
            space.Add(arena);
        }

        return space;
    }

    [DataTheory]
    [InlineData("CMEAT_5")]
    [InlineData("MEAT_5")]
    public void StopLandsOnItsArena(string arena)
    {
        var stop = ArenaStops.Find(Data.Value, Space.Value, arena);
        Assert.NotNull(stop);
        Assert.Equal(arena, TopFloorArena(Space.Value, arena, stop.Value));
    }

    [DataTheory]
    [InlineData("MEAT_6")]
    [InlineData("CMEAT_6")]
    public void CoveredArenaKeepsAStop(string arena) =>
        Assert.NotNull(ArenaStops.Find(Data.Value, Space.Value, arena));

    /// <summary>The arena whose floor Kurt lands on below the stop, that one when it ties.</summary>
    private static string? TopFloorArena(ArenaSpace space, string arena, Vector3 stop)
    {
        var to = stop - new Vector3(0f, 0f, Reach);
        var floors = space.At(stop)
            .Select(b => (b.Arena.Name, Hit: b.Segment(stop, to, Bsp.SegmentMode.Floor, out var p) != Bsp.None, p.Z))
            .Where(f => f.Hit)
            .ToList();
        if (floors.Count == 0)
        {
            return null;
        }

        var top = floors.Max(f => f.Z);
        return floors.Any(f => f.Name == arena && f.Z >= top - SameFloor) ? arena : floors.First(f => f.Z == top).Name;
    }
}
