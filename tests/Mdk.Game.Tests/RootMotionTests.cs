using System.Numerics;
using Mdk.Engine.Audio;
using Mdk.Formats;
using Mdk.Game.Audio;
using Mdk.Game.Collision;
using Mdk.Game.Level;
using Mdk.Game.Objects;
using Mdk.Game.Scripts;

namespace Mdk.Game.Tests;

/// <summary>Found by playtest: LEVEL4's MEAT_5 never dropped its key. An alien XC ending its charge
/// (XC_STOP) sank through the ice and lived on below it; the arena brings the tank, which carries
/// the key, only once every XC is dead. The original adds an animation's root motion to the push
/// (anim_step_frames 0x43ab70), moved with collisions on the next tick, not to the position.</summary>
public class RootMotionTests
{
    private const int Level = 4;
    private const string Arena = "MEAT_5";
    private const string Alien = "XC";
    private const string ChargeEnd = "XC_STOP";
    /// <summary>Where the charge ended, on the ice (z −1989).</summary>
    private static readonly Vector3 OnTheIce = new(-4.3f, 13181.5f, -1989.05f);
    private const float Yaw = 315f;
    private const float Tolerance = 0.5f;
    private const int Ticks = 120;

    private static readonly MdkData Data = MdkData.Find() ?? throw new InvalidOperationException("MDK data not found");
    private static readonly AudioDevice Device = new(Output.Muted);

    private static ScriptRuntime CreateRuntime()
    {
        var level = new LevelData(Data, Level);
        var cmi = Cmi.Load(Data.PathOf($"TRAVERSE/LEVEL{Level}/LEVEL{Level}.CMI"));
        var sprites = Bni.Load(Data.PathOf("TRAVERSE/TRAVSPRT.BNI"));
        var space = new ArenaSpace();
        var mixer = new SoundMixer(Device, _ => null);
        return new ScriptRuntime(level, cmi, sprites, space, new TriangleGroups(), mixer, new Kurt.Kurt(space, mixer, _ => 1));
    }

    [DataFact]
    public void RootMotionStopsAtTheFloor()
    {
        var runtime = CreateRuntime();
        var obj = runtime.Spawn(runtime.GetArenaState(Arena).Controller, Alien, OnTheIce, Yaw, -1, 0, ScriptRuntime.Spawning.Plain);
        Assert.NotNull(obj);
        obj.Flags = MdkObject.FlagGravity | MdkObject.FlagCollides;
        obj.RestartAnimation(runtime.FindArenaAnimation(Arena, ChargeEnd), MdkObject.Looping.Once);

        for (var i = 0; i < Ticks; i++)
        {
            runtime.Motion.Update(obj);
        }

        Assert.InRange(obj.Position.Z, OnTheIce.Z - Tolerance, OnTheIce.Z + Tolerance);
    }
}
