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
    /// <summary>Pitch changes (degrees per step) below this don't count as reversals; no step may
    /// turn the view more than <see cref="MaxPitchStep"/> (the climb term comes back by 0.8° a tick
    /// when Kurt stops).</summary>
    private const float PitchTolerance = 0.05f;
    private const float MaxPitchStep = 1f;
    /// <summary>The slope's bumps turn the view a few times; the old air tilt did it 80-160 times.</summary>
    private const int MaxTiltFlips = 10;

    private static readonly MdkData Data = MdkData.Find() ?? throw new InvalidOperationException("MDK data not found");
    private static readonly AudioDevice Device = new(Output.Muted);

    [DataTheory]
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

        // The view's pitch may follow the descent smoothly (the climb term, −40 × the smoothed
        // vertical speed) but doesn't shake: no step turns it by more than a degree, and its
        // noticeable changes rarely reverse (the old air tilt toggled every few steps).
        var pitches = eyes.Select(z => float.RadiansToDegrees(MathF.Asin(z))).ToList();
        var changes = pitches.Zip(pitches.Skip(1)).Select(p => p.Second - p.First).ToList();
        Assert.True(changes.Max(MathF.Abs) < MaxPitchStep, $"view pitch jumped {changes.Max(MathF.Abs):0.000}°");
        var tilts = Reversals(changes);
        Assert.True(tilts < MaxTiltFlips, $"view pitch reversed {tilts} times");
    }

    /// <summary>How often a series of changes flips sign (changes below the tolerance ignored).</summary>
    private static int Reversals(IReadOnlyList<float> changes)
    {
        var flips = 0;
        var previous = 0f;
        foreach (var d in changes.Where(d => MathF.Abs(d) > PitchTolerance))
        {
            if (previous != 0f && MathF.Sign(d) != MathF.Sign(previous))
            {
                flips++;
            }

            previous = d;
        }

        return flips;
    }
}
