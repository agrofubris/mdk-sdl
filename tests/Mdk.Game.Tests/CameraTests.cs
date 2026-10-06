using System.Numerics;
using Mdk.Engine.Audio;
using Mdk.Engine.Platform;
using Mdk.Game.Audio;
using Mdk.Game.Collision;
using Mdk.Game.Kurt;

namespace Mdk.Game.Tests;

/// <summary>The follow camera's pitch (camera_update 0x4174d0): the climb term −40 × the smoothed
/// rise of Kurt's feet per tick (0x490db8).</summary>
public class CameraTests
{
    private const float Step = 1f / 60f;
    private const float ArenaPitch = 4f;
    /// <summary>The rise per tick is clamped to 0.5: the term tops out at −20°.</summary>
    private const float MaxClimbPitch = -20f;

    private static readonly AudioDevice Device = new(Output.Muted);

    /// <summary>Kurt standing on a floor that rises <paramref name="risePerTick"/> each tick.</summary>
    private static float PitchAfter(float risePerTick, float seconds)
    {
        var kurt = new Kurt.Kurt(new ArenaSpace(), new SoundMixer(Device, _ => null), _ => 1) { OnFloor = true };
        var camera = new FollowCamera();
        var input = new Input();
        for (var t = 0f; t < seconds; t += Step)
        {
            kurt.Feet += new Vector3(0f, 0f, risePerTick * Kurt.Kurt.Ticks * Step);
            camera.Update(kurt, ArenaPitch, input, Step);
        }

        // Positive pitch looks down.
        return -float.RadiansToDegrees(MathF.Asin(camera.Forward.Z));
    }

    [Fact]
    public void StandingStillKeepsTheArenaPitch()
    {
        Assert.Equal(ArenaPitch, PitchAfter(0f, 3f), 2);
    }

    [Fact]
    public void ClimbingTiltsTheViewUp()
    {
        // Rising 1 unit a tick (clamped to 0.5) for 5 s: the smoothed rise nears 0.5.
        Assert.Equal(ArenaPitch + MaxClimbPitch, PitchAfter(1f, 5f), 0);
    }

    [Fact]
    public void FallingTiltsTheViewDown()
    {
        var pitch = PitchAfter(-0.25f, 5f);
        Assert.InRange(pitch, ArenaPitch + 9f, ArenaPitch + 10.5f);
    }
}
