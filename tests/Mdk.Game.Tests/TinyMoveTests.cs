using System.Numerics;
using Mdk.Formats;
using Mdk.Game.Collision;

namespace Mdk.Game.Tests;

/// <summary>Found by the soak test: a walk move of a hundred-thousandth of a unit (Kurt's speed
/// fading out) against level 4's MEAT_3 slope threw his feet 5 units away, into the rock, and he
/// fell through the level.</summary>
public class TinyMoveTests
{
    private const int Level = 4;
    private const float WalkSlide = 0.75f;
    /// <summary>A move ends at most this far beyond its length (the push off a face).</summary>
    private const float Tolerance = 0.1f;

    private static readonly MdkData Data = MdkData.Find() ?? throw new InvalidOperationException("MDK data not found");

    [DataFact]
    public void TinyWalkMoveStaysPut()
    {
        var mto = Mto.Load(Data.PathOf($"TRAVERSE/LEVEL{Level}/LEVEL{Level}O.MTO"));
        var space = new ArenaSpace();
        space.Add(mto.GetArena("MEAT_3"));
        var feet = new Vector3(36.463993f, 2648.5732f, -502.07703f);
        var move = new Vector3(3.6144081E-06f, -1.18592025E-05f, 0f);

        var result = space.Move(feet, move, ArenaSpace.Motion.Walk, WalkSlide);

        Assert.InRange(Vector3.Distance(feet, result.Feet), 0f, move.Length() + Tolerance);
    }
}
