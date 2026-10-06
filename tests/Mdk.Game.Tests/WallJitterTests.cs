using System.Numerics;
using Mdk.Engine.Audio;
using Mdk.Engine.Platform;
using Mdk.Formats;
using Mdk.Game.Audio;
using Mdk.Game.Collision;

namespace Mdk.Game.Tests;

/// <summary>Running down level 3's ramp and into its canyon walls must not shake the view: Kurt
/// loses the floor for single steps going downhill, but the camera only tilts once he really falls
/// (air ticks 0x573a48 count from vz &lt; -16).</summary>
public class WallJitterTests
{
    private const int Level = 3;
    private const float Step = 1f / 60f;
    private const int Frames = 5;
    private const float Seconds = 4f;
    private const int MaxFlips = 4;
    private const float PitchTolerance = 1e-4f;

    private static readonly MdkData Data = MdkData.Find() ?? throw new InvalidOperationException("MDK data not found");
    private static readonly AudioDevice Device = new(Output.Muted);

    [Theory]
    [InlineData(30f)]
    [InlineData(60f)]
    [InlineData(120f)]
    [InlineData(150f)]
    public void RunningDownhillKeepsViewSteady(float yaw)
    {
        var dti = Dti.Load(Data.PathOf($"TRAVERSE/LEVEL{Level}/LEVEL{Level}.DTI"));
        var mto = Mto.Load(Data.PathOf($"TRAVERSE/LEVEL{Level}/LEVEL{Level}O.MTO"));
        var space = new ArenaSpace();
        space.Add(mto.GetArena(dti.Arenas[dti.StartArena].Name));
        var kurt = new Kurt.Kurt(space, new SoundMixer(Device, _ => null), _ => Frames) { Feet = new Vector3(-10f, 60f, 146f), Yaw = yaw };
        var input = new Input();
        for (var t = 0f; t < 1f; t += Step)
        {
            kurt.Update(input, Step);
        }

        input.Hold(Key.Forward, Input.State.Down);
        var heights = new List<float>();
        var camera = new Kurt.FollowCamera();
        var eyes = new List<float>();
        for (var t = 0f; t < Seconds; t += Step)
        {
            kurt.Update(input, Step);
            camera.Update(kurt, 4f, input, Step);
            heights.Add(kurt.Feet.Z);
            eyes.Add(camera.Forward.Z);
        }


        // Up-down reversals: a steady walk goes one way or stays; shaking flips every few steps.
        var flips = 0;
        var previous = 0f;
        for (var i = 1; i < heights.Count; i++)
        {
            var d = heights[i] - heights[i - 1];
            if (MathF.Abs(d) < 1e-3f)
            {
                continue;
            }

            if (previous != 0f && MathF.Sign(d) != MathF.Sign(previous))
            {
                flips++;
            }

            previous = d;
        }

        Assert.True(flips < MaxFlips, $"height reversed {flips} times");

        // The view's pitch stays put (the arena pitch is fixed here).
        var tilts = eyes.Zip(eyes.Skip(1)).Count(e => MathF.Abs(e.First - e.Second) > PitchTolerance);
        Assert.Equal(0, tilts);
    }
}
