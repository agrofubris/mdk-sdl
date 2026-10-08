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

    /// <summary>Found by playtest: LEVEL5 MUSE_4's Gunter (XGUNTAM, flag 0x80000000) sank into his
    /// pillar a little with each XGU_BLDM loop, out of reach. Flag 0x80000000 (obj+0x14b bit 7) skips
    /// root motion; his death script clears it so that his fall moves him.</summary>
    private const int GunterLevel = 5;
    private const string GunterArena = "MUSE_4";
    private const string Gunter = "XGUNTAM";
    private const string Taunt = "XGU_BLDM";
    private static readonly Vector3 OnThePillar = new(387f, 121f, -1563f);
    private const float GunterYaw = 90f;
    /// <summary>A minute: about six taunts, each sank him about 1.5.</summary>
    private const int GunterTicks = 1800;

    private static ScriptRuntime CreateRuntime(int levelNumber = Level)
    {
        var level = new LevelData(Data, levelNumber);
        var cmi = Cmi.Load(Data.PathOf($"TRAVERSE/LEVEL{levelNumber}/LEVEL{levelNumber}.CMI"));
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

    [DataFact]
    public void NoRootMotionKeepsGunterUp()
    {
        var runtime = CreateRuntime(GunterLevel);
        var obj = runtime.Spawn(runtime.GetArenaState(GunterArena).Controller, Gunter, OnThePillar, GunterYaw, -1, 0, ScriptRuntime.Spawning.Plain);
        Assert.NotNull(obj);
        obj.Flags = MdkObject.FlagNoRootMotion;
        var taunt = runtime.FindArenaAnimation(GunterArena, Taunt);

        // His script restarts the taunt once it's done (anim_once, if_anim_done).
        for (var i = 0; i < GunterTicks; i++)
        {
            if (obj.IsAnimationDone)
            {
                obj.RestartAnimation(taunt, MdkObject.Looping.Once);
            }

            runtime.Motion.Update(obj);
        }

        Assert.Equal(OnThePillar, obj.Position);
    }
}
