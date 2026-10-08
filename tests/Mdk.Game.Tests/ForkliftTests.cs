using System.Numerics;
using Mdk.Engine.Audio;
using Mdk.Formats;
using Mdk.Game.Audio;
using Mdk.Game.Collision;
using Mdk.Game.Level;
using Mdk.Game.Objects;
using Mdk.Game.Scripts;

namespace Mdk.Game.Tests;

/// <summary>Level 8's forklift puzzle (GUNT_2): the turret garage XPER's death brings a forklift
/// with a driver (XFORK script 0x4bc6). Hits past 110 blow the driver and the canopy off; then each
/// hit pushes it along the shot (push_hit_dir), its script giving it its health back. On the yellow
/// pad (-435..-414, 547..568) it hides the glass (group 1) over the way down.</summary>
public class ForkliftTests
{
    private const int Level = 8;
    private const float Tick = 1f / 30f;
    private const string Arena = "GUNT_2";
    private const string Forklift = "XFORK";
    private const string Driver = "XFK_HEAD";
    private const int Glass = 1;
    private const int BulletRound = 0;
    /// <summary>West of the garage, south of the pad: the forklift drives at him.</summary>
    private static readonly Vector3 Stand = new(-440f, 548f, 8f);
    private static readonly Vector3 PadCenter = new(-424.5f, 557.5f, 8f);
    private static readonly Box Pad = new(new Vector3(-435f, 547f, 7f), new Vector3(-414f, 568f, 9f));
    private const int ArriveTicks = 60;
    private const int ShootTicks = 20 * 30;
    private const int FireEvery = 3;
    /// <summary>Shots come from this far behind the forklift, aimed at its middle.</summary>
    private const float ShotRange = 20f;
    private const float AimHeight = 4f;
    private const float Still = 0.5f;
    /// <summary>Shots from in front of a wall behind the forklift.</summary>
    private const float WallGap = 1f;

    private static readonly Lazy<MdkData> Data = new(() => MdkData.Find()!);
    private static readonly AudioDevice Device = new(Output.Muted);

    private static (ScriptRuntime Runtime, TriangleGroups Groups) CreateRuntime()
    {
        var level = new LevelData(Data.Value, Level);
        var cmi = Cmi.Load(Data.Value.PathOf($"TRAVERSE/LEVEL{Level}/LEVEL{Level}.CMI"));
        var sprites = Bni.Load(Data.Value.PathOf("TRAVERSE/TRAVSPRT.BNI"));
        var space = new ArenaSpace();
        var groups = new TriangleGroups();
        foreach (var arena in level.Arenas.Where(a => level.IsReachable(a.Name)))
        {
            space.Add(arena);
            groups.Add(arena);
        }

        var mixer = new SoundMixer(Device, _ => null);
        return (new ScriptRuntime(level, cmi, sprites, space, groups, mixer, new Kurt.Kurt(space, mixer, _ => 1)), groups);
    }

    /// <summary>Kurt by the pad, the garage killed: the forklift with the driver.</summary>
    private static MdkObject DrivenForklift(ScriptRuntime runtime)
    {
        runtime.TeleportKurt(Arena, Stand, 0f);
        Run(runtime, ArriveTicks);
        runtime.Kill(runtime.FindObjectNamed("XPER")!);
        Run(runtime, ArriveTicks);
        return runtime.Objects.Last(o => o.TypeName == Forklift && !o.Dead);
    }

    private static void Run(ScriptRuntime runtime, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            runtime.Update(Tick);
        }
    }

    /// <summary>A bullet from <paramref name="eye"/> at the forklift's middle.</summary>
    private static void Shoot(ScriptRuntime runtime, Vector3 eye, MdkObject forklift)
    {
        var to = forklift.Position + new Vector3(0f, 0f, AimHeight) - eye;
        var yaw = float.RadiansToDegrees(MathF.Atan2(to.Y, to.X));
        var pitch = -float.RadiansToDegrees(MathF.Atan2(to.Z, new Vector2(to.X, to.Y).Length()));
        runtime.SniperRounds.Fire(BulletRound, eye, yaw, pitch, null);
    }

    /// <summary>Shoots the forklift from Kurt until the driver is gone.</summary>
    private static void ShootDriver(ScriptRuntime runtime, MdkObject forklift)
    {
        var driver = 1 << forklift.FindPart(Driver);
        for (var i = 0; i < ShootTicks && !forklift.Dead && (forklift.HiddenParts & driver) == 0; i++)
        {
            if (i % FireEvery == 0)
            {
                Shoot(runtime, runtime.KurtPosition + new Vector3(0f, 0f, AimHeight), forklift);
            }

            runtime.Update(Tick);
        }
    }

    private static bool OnPad(MdkObject forklift) =>
        Vector3.Clamp(forklift.Position, Pad.Min, Pad.Max) == forklift.Position;

    [DataFact]
    public void ShotsPushTheForkliftOntoThePad()
    {
        var (runtime, groups) = CreateRuntime();
        var forklift = DrivenForklift(runtime);
        ShootDriver(runtime, forklift);

        // One shot at a time from the far side, each once it stands still.
        for (var i = 0; i < ShootTicks && !OnPad(forklift); i++)
        {
            if (new Vector2(forklift.Velocity.X, forklift.Velocity.Y).Length() < Still)
            {
                var away = Vector3.Normalize((forklift.Position - PadCenter) with { Z = 0f });
                var middle = forklift.Position + new Vector3(0f, 0f, AimHeight);
                var eye = middle + away * ShotRange;
                if (runtime.Raycast(middle, eye) is { } wall)
                {
                    eye = middle + away * (Vector3.Distance(middle, wall.Point) - WallGap);
                }

                Shoot(runtime, eye, forklift);
            }

            runtime.Update(Tick);
        }

        runtime.Update(Tick);

        Assert.False(forklift.Dead);
        Assert.True(OnPad(forklift));
        Assert.Equal(TriangleGroups.State.Hidden, groups.Get(Arena, Glass)!.State & TriangleGroups.State.Hidden);
    }

    [DataFact]
    public void OneHitBlowsOnlyTheDriverOff()
    {
        var (runtime, _) = CreateRuntime();
        var forklift = DrivenForklift(runtime);
        runtime.Lethality = Lethality.OneHit;

        ShootDriver(runtime, forklift);
        Run(runtime, ArriveTicks);

        Assert.False(forklift.Dead);
        Assert.NotEqual(0, forklift.HiddenParts & (1 << forklift.FindPart(Driver)));
    }
}
