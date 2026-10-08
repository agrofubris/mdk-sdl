using System.Numerics;
using Mdk.Engine.Audio;
using Mdk.Engine.Platform;
using Mdk.Formats;
using Mdk.Game.Audio;
using Mdk.Game.Collision;
using Mdk.Game.Level;

namespace Mdk.Game.Tests;

/// <summary>Triangles flagged 0x2 (material NONE) are made not drawn and not solid when the
/// original activates an arena (0x40d46c(a,1)). LEVEL8 GUNT_2's one cut the floor diagonally:
/// an invisible wall Kurt couldn't pass.</summary>
public class HiddenTriangleTests
{
    private const int Level = 8;
    private const string Arena = "GUNT_2";
    private const float Step = 1f / 60f;
    private const int Frames = 5;
    private const float Seconds = 1f;
    /// <summary>A second of walking covers more than this.</summary>
    private const float MinWalk = 5f;

    private static readonly MdkData Data = MdkData.Find() ?? throw new InvalidOperationException("MDK data not found");
    private static readonly AudioDevice Device = new(Output.Muted);

    [DataFact]
    public void KurtWalksThroughHiddenWall()
    {
        var mto = Mto.Load(Data.PathOf($"TRAVERSE/LEVEL{Level}/LEVEL{Level}O.MTO"));
        var arena = mto.GetArena(Arena);
        new TriangleGroups().Add(arena);
        var space = new ArenaSpace();
        space.Add(arena);
        var start = new Vector3(-258.96f, 488.37f, 7.51f);
        var kurt = new Kurt.Kurt(space, new SoundMixer(Device, _ => null), _ => Frames) { Feet = start, Yaw = 166f };
        var input = new Input();
        input.Hold(Key.Forward, Input.State.Down);
        for (var t = 0f; t < Seconds; t += Step)
        {
            kurt.Update(input, Step);
        }

        Assert.True(Vector2.Distance(new Vector2(start.X, start.Y), new Vector2(kurt.Feet.X, kurt.Feet.Y)) > MinWalk, $"stuck at {kurt.Feet}");
    }
}
