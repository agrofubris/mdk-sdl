using System.Numerics;
using Mdk.Formats;
using Mdk.Game.Collision;

namespace Mdk.Game.Level;

/// <summary>Where Kurt can be put in an arena (the soak test's tour, the console's teleport): just
/// above a floor of that arena, under its first cover spot, else waypoint, alien or connection
/// record; one with no other arena's floor above that floor first (LEVEL4: MEAT_5's floor lies 2
/// above CMEAT_5's by door #1999; Kurt would stand outside his arena).
/// <code>
///   record ●
///          │ ≤ 200
///   floor ─┴─── ◄── stop (4 above)
/// </code></summary>
public static class ArenaStops
{
    private const uint DtiAlien = 2;
    private const uint DtiCover = 5;
    private const uint DtiWaypoint = 8;
    /// <summary>Kurt lands from slightly above the floor.</summary>
    private const float Lift = 4f;
    /// <summary>A record is at most this high above its floor.</summary>
    private const float MaxDrop = 200f;
    /// <summary>A doorway's stop is this far into its arena.</summary>
    private const float DoorwayInset = 4f;
    /// <summary>Floors this close are one height (the soak's astray reach).</summary>
    private const float SameFloor = 1f;

    private static readonly uint[] Preferred = [DtiCover, DtiWaypoint, DtiAlien, LevelData.Connection];

    /// <summary>The stop of an arena, or null (unknown, unreachable, no floor under its records).</summary>
    public static Vector3? Find(LevelData level, ArenaSpace space, string arena)
    {
        var entry = level.Dti.Arenas.FirstOrDefault(a => a.Name == arena);
        if (entry == null || !level.IsReachable(arena))
        {
            return null;
        }

        // Arenas covered by another's floor everywhere (LEVEL4 MEAT_6, CMEAT_6) still get one.
        var points = Preferred.SelectMany(type => entry.Records.Where(r => r.Type == type)).Select(StopOf).ToList();
        var floor = FirstFloor(space, arena, points, Overlap.Clear) ?? FirstFloor(space, arena, points, Overlap.Allowed);
        return floor is { } f ? f + new Vector3(0f, 0f, Lift) : null;
    }

    /// <summary>Whether another arena's floor may lie between a stop and its arena's floor.</summary>
    private enum Overlap { Clear, Allowed }

    private static Vector3? FirstFloor(ArenaSpace space, string arena, List<Vector3> points, Overlap overlap) =>
        points.Select(p => FloorBelow(space, arena, p)).FirstOrDefault(f => f != null && (overlap == Overlap.Allowed || !IsCovered(space, arena, f.Value)));

    /// <summary>Which way a doorway (DTI connection) leaves its arena; hatches go up or down.</summary>
    private enum Doorway { MinusX, PlusX, MinusY, PlusY }

    /// <summary>A record's point; for a doorway, its middle a step into the arena: its corner lies
    /// on the arena's edge by a wall (LEVEL3 HMO_1 1000, LEVEL7 DANT_6 1009 and 1010).</summary>
    private static Vector3 StopOf(Dti.Record record)
    {
        if (record.Type != LevelData.Connection)
        {
            return record.Position;
        }

        // The record's direction leaves the arena: -x, +x, -y, +y (hatches: none).
        var inward = (Doorway)BitConverter.SingleToInt32Bits(record.Angle) switch
        {
            Doorway.MinusX => Vector3.UnitX,
            Doorway.PlusX => -Vector3.UnitX,
            Doorway.MinusY => Vector3.UnitY,
            Doorway.PlusY => -Vector3.UnitY,
            _ => Vector3.Zero,
        };
        var middle = (record.Position + record.BoxEnd) / 2f;
        return middle with { Z = record.Position.Z } + inward * DoorwayInset;
    }

    /// <summary>The floor of that arena under a point (not one of an overlapping arena).</summary>
    private static Vector3? FloorBelow(ArenaSpace space, string arena, Vector3 point)
    {
        var from = point + new Vector3(0f, 0f, Lift);
        var bsp = space.At(from).FirstOrDefault(b => b.Arena.Name == arena);
        var to = from - new Vector3(0f, 0f, MaxDrop);
        return bsp != null && bsp.Segment(from, to, Bsp.SegmentMode.Floor, out var floor) != Bsp.None ? floor : null;
    }

    /// <summary>Whether Kurt, dropped onto that floor, meets another arena's floor first.</summary>
    private static bool IsCovered(ArenaSpace space, string arena, Vector3 floor)
    {
        var from = floor + new Vector3(0f, 0f, Lift);
        var to = floor + new Vector3(0f, 0f, SameFloor);
        return space.At(from).Any(b => b.Arena.Name != arena && b.Segment(from, to, Bsp.SegmentMode.Floor, out _) != Bsp.None);
    }
}
