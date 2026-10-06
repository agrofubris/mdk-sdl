using System.Numerics;
using Mdk.Formats;
using Mdk.Game.Collision;

namespace Mdk.Game.Tests;

/// <summary>Collisions on real arenas: level 3's start. Its DTI position lies 1.7 under the
/// ground there (the Godot port settles Kurt at z 192).</summary>
public class BspTests
{
    private const int Level = 3;
    /// <summary>Kurt's boxes (damp_collide_move 0x465e34): vertical sweeps 0.4/0.4/2.5 from feet + 0.01,
    /// horizontal ones 0.6/0.6/2.5 from feet + 0.5.</summary>
    private static readonly Vector3 FallBox = new(0.4f, 0.4f, 2.5f);
    private static readonly Vector3 WalkBox = new(0.6f, 0.6f, 2.5f);
    private const float FallLift = 0.01f;
    private const int Iterations = 4;
    private const float FallSlide = 0.5f;
    private const float WalkSlide = 0.75f;
    private const float Tolerance = 0.6f;
    /// <summary>The ground under level 3's start (the Godot port's Kurt: z 192).</summary>
    private const float GroundZ = 192f;
    private const float GroundTolerance = 1f;

    private static readonly MdkData Data = MdkData.Find() ?? throw new InvalidOperationException("MDK data not found");

    private static (Bsp Bsp, Vector3 Start) StartArena()
    {
        var dti = Dti.Load(Data.PathOf($"TRAVERSE/LEVEL{Level}/LEVEL{Level}.DTI"));
        var mto = Mto.Load(Data.PathOf($"TRAVERSE/LEVEL{Level}/LEVEL{Level}O.MTO"));
        var arena = mto.GetArena(dti.Arenas[dti.StartArena].Name);
        return (new Bsp(arena), dti.StartPosition);
    }

    [DataFact]
    public void SegmentFindsTheFloorUnderTheStart()
    {
        var (bsp, start) = StartArena();
        var triangle = bsp.Segment(start + new Vector3(0f, 0f, 5f), start - new Vector3(0f, 0f, 20f), Bsp.SegmentMode.Floor, out var point);
        Assert.NotEqual(Bsp.None, triangle);
        Assert.InRange(point.Z, GroundZ - GroundTolerance, GroundZ + GroundTolerance);
    }

    [DataFact]
    public void FallingBoxLandsOnTheFloor()
    {
        var (bsp, start) = StartArena();
        var from = start + new Vector3(0f, 0f, 10f + FallBox.Z + FallLift);
        var triangle = bsp.SweepBox(from, from - new Vector3(0f, 0f, 30f), FallBox, Iterations, FallSlide, out var end, out _);
        Assert.NotEqual(Bsp.None, triangle);
        var feet = end.Z - FallBox.Z - FallLift;
        bsp.Segment(end, end - new Vector3(0f, 0f, 20f), Bsp.SegmentMode.Floor, out var ground);
        Assert.InRange(feet, ground.Z - Tolerance, ground.Z + Tolerance);
    }

    [DataFact]
    public void WalkingFarIsStoppedByWalls()
    {
        var (bsp, start) = StartArena();
        const float Far = 5000f;
        var from = start + new Vector3(0f, 0f, WalkBox.Z + 0.5f);
        foreach (var direction in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY })
        {
            bsp.SweepBox(from, from + direction * Far, WalkBox, Iterations, WalkSlide, out var end, out _);
            Assert.True(Vector3.Distance(from, end) < Far, $"walked {direction} unhindered");
        }
    }

    [DataFact]
    public void NotSolidTrianglesAreSkipped()
    {
        var (bsp, start) = StartArena();
        var a = start + new Vector3(0f, 0f, 5f);
        var b = start - new Vector3(0f, 0f, 20f);
        var triangle = bsp.Segment(a, b, Bsp.SegmentMode.Floor, out _);
        bsp.Arena.TriangleFlags[triangle] |= Bsp.NotSolid;
        Assert.NotEqual(triangle, bsp.Segment(a, b, Bsp.SegmentMode.Floor, out _));
    }
}
