using System.Numerics;
using Mdk.Engine.Audio;
using Mdk.Formats;
using Mdk.Game.Audio;
using Mdk.Game.Collision;
using Mdk.Game.Level;
using Mdk.Game.Objects;
using Mdk.Game.Scripts;

namespace Mdk.Game.Tests;

/// <summary>Onehit spares scripted targets: level 8's XEARTH in GUNT_4 (its script resets its
/// health and counts hits in arena variable 0: 6 a hit, 320 below 9950 hit points; at 720 it sinks
/// and explodes, breaking the glass below) and shatterable groups (level 3's HMO_8) take the
/// original's damage.</summary>
public class OneHitScriptedTests
{
    private const int EarthLevel = 8;
    private const int GroupLevel = 3;
    private const float Tick = 1f / 30f;
    private const int SettleTicks = 30;
    private const string EarthArena = "GUNT_4";
    private const string GroupArena = "HMO_8";
    private const string Earth = "XEARTH";
    private const int BulletRound = 0;
    private const int Shots = 3;
    private const int ShotTicks = 10;
    /// <summary>Eyes this far from a target, tried around it until one sees it.</summary>
    private const float EyeRange = 30f;
    private static readonly Vector3[] Around =
    [
        Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ,
    ];

    private static readonly Lazy<MdkData> Data = new(() => MdkData.Find()!);
    private static readonly AudioDevice Device = new(Output.Muted);

    /// <summary>Kurt in <paramref name="arena"/> of <paramref name="number"/>, its scripts started.</summary>
    private static ScriptRuntime CreateRuntime(int number, string arena, Lethality lethality)
    {
        var level = new LevelData(Data.Value, number);
        var cmi = Cmi.Load(Data.Value.PathOf($"TRAVERSE/LEVEL{number}/LEVEL{number}.CMI"));
        var sprites = Bni.Load(Data.Value.PathOf("TRAVERSE/TRAVSPRT.BNI"));
        var space = new ArenaSpace();
        var groups = new TriangleGroups();
        foreach (var data in level.Arenas.Where(a => level.IsReachable(a.Name)))
        {
            space.Add(data);
            groups.Add(data);
        }

        var mixer = new SoundMixer(Device, _ => null);
        var runtime = new ScriptRuntime(level, cmi, sprites, space, groups, mixer, new Kurt.Kurt(space, mixer, _ => 1));
        runtime.TeleportKurt(arena, ArenaStops.Find(level, space, arena)!.Value, 0f);
        Run(runtime, SettleTicks);
        runtime.Lethality = lethality;
        return runtime;
    }

    private static void Run(ScriptRuntime runtime, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            runtime.Update(Tick);
        }
    }

    /// <summary>A point that sees <paramref name="target"/> (the ray's first hit is
    /// <paramref name="group"/>, 0 for none), or null.</summary>
    private static Vector3? EyeOn(ScriptRuntime runtime, Vector3 target, int group)
    {
        foreach (var way in Around)
        {
            var eye = target + way * EyeRange;
            var hit = runtime.Raycast(eye, target);
            if (group == 0 ? hit == null : hit is { } h && h.Group == group)
            {
                return eye;
            }
        }

        return null;
    }

    private static Vector3 Middle(Box box) => (box.Min + box.Max) * 0.5f;

    /// <summary>Bullets from <paramref name="eye"/> at <paramref name="target"/>, one at a time.</summary>
    private static void Shoot(ScriptRuntime runtime, Vector3 eye, Vector3 target)
    {
        var to = target - eye;
        var yaw = float.RadiansToDegrees(MathF.Atan2(to.Y, to.X));
        var pitch = -float.RadiansToDegrees(MathF.Atan2(to.Z, new Vector2(to.X, to.Y).Length()));
        for (var i = 0; i < Shots; i++)
        {
            runtime.SniperRounds.Fire(BulletRound, eye, yaw, pitch, null);
            Run(runtime, ShotTicks);
        }
    }

    /// <summary>XEARTH's hit count (GUNT_4's variable 0) after the shots.</summary>
    private static float EarthCount(Lethality lethality)
    {
        var runtime = CreateRuntime(EarthLevel, EarthArena, lethality);
        var earth = runtime.Objects.First(o => o.TypeName == Earth && !o.Dead);
        var center = Middle(runtime.GetWorldBounds(earth));
        var eye = EyeOn(runtime, center, 0) ?? throw new InvalidOperationException("No eye on XEARTH");
        Shoot(runtime, eye, center);
        Assert.False(earth.Dead);
        return runtime.GetArenaState(EarthArena).Variables[0];
    }

    /// <summary>The counter of HMO_8's first group whose hit script counts shots, after the shots.</summary>
    private static int GroupCount(Lethality lethality)
    {
        var runtime = CreateRuntime(GroupLevel, GroupArena, lethality);
        var state = runtime.GetArenaState(GroupArena);
        for (var i = 0; i < ScriptRuntime.GroupCount; i++)
        {
            if (state.GroupHitScripts[i] == 0 || (state.GroupHitMasks[i] & ScriptRuntime.HitShot) == 0)
            {
                continue;
            }

            foreach (var center in runtime.GroupCenters(GroupArena, i + 1))
            {
                if (EyeOn(runtime, center, i + 1) is not { } eye)
                {
                    continue;
                }

                Shoot(runtime, eye, center);
                return state.GroupCounters[i];
            }
        }

        throw new InvalidOperationException("No shot group in " + GroupArena);
    }

    [DataFact]
    public void EarthCountsHitsAsUsual()
    {
        Assert.Equal(EarthCount(Lethality.Normal), EarthCount(Lethality.OneHit));
    }

    [DataFact]
    public void GroupCountsHitsAsUsual()
    {
        Assert.Equal(GroupCount(Lethality.Normal), GroupCount(Lethality.OneHit));
    }
}
