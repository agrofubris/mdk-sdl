using System.Numerics;
using Mdk.Engine.Audio;
using Mdk.Formats;
using Mdk.Game.Audio;
using Mdk.Game.Collision;
using Mdk.Game.Level;
using Mdk.Game.Objects;
using Mdk.Game.Scripts;

namespace Mdk.Game.Tests;

/// <summary>Found by playtest: in LEVEL8's GUNT_10 a winged alien XG walked into the arena's
/// centre hole (x −61…−25 at y 3327; the X10_CAP platform over it stops Kurt only) and slid out of
/// sight below the −400 floor, so the script waiting for the last XG never went on. The original's
/// if_no_floor_at (0x45694b, probe 0x46046c) tests a point dx ahead the way add_vel_local does
/// (−dx along the yaw: the scripts' −20 is 20 units ahead), from z + 3 down to z − 3 (or z + depth),
/// up-facing faces only; the XG turns away at the edge.</summary>
public class NoFloorTests
{
    private const int Level = 8;
    private const string Arena = "GUNT_10";
    private const string Alien = "XG";
    private const float Tick = 1f / 30f;
    private const int WalkTicks = 15 * 30;
    /// <summary>The ring around the hole slopes down to about −410 at its rim.</summary>
    private const float LowestFloor = -410f;
    /// <summary>How far the feet may be from the floor (walking bumps).</summary>
    private const float FeetReach = 5f;
    /// <summary>The hole's west edge at y 3327.</summary>
    private const float HoleWest = -61f;
    /// <summary>The XG's longest edge check (if_no_floor_at −20).</summary>
    private const float EdgeReach = 20f;

    /// <summary>The XG's walk loop (0x22134): edge and wall checks, turns.</summary>
    private const int WalkScript = 0x22134;

    /// <summary>West of the hole, facing it (yaw 0 is +x).</summary>
    private static readonly Vector3 Start = new(-85f, 3327f, -399f);
    /// <summary>Behind the XG, out of its sight.</summary>
    private static readonly Vector3 KurtSpot = new(-150f, 3327f, -399f);

    private static readonly Lazy<MdkData> Data = new(() => MdkData.Find() ?? throw new InvalidOperationException("MDK data not found"));
    private static readonly Lazy<AudioDevice> Device = new(() => new AudioDevice(Output.Muted));

    private static ScriptRuntime CreateRuntime(ArenaSpace space)
    {
        var data = Data.Value;
        var level = new LevelData(data, Level);
        var cmi = Cmi.Load(data.PathOf($"TRAVERSE/LEVEL{Level}/LEVEL{Level}.CMI"));
        var sprites = Bni.Load(data.PathOf("TRAVERSE/TRAVSPRT.BNI"));
        foreach (var arena in level.Arenas.Where(a => level.IsReachable(a.Name)))
        {
            space.Add(arena);
        }

        var mixer = new SoundMixer(Device.Value, _ => null);
        return new ScriptRuntime(level, cmi, sprites, space, new TriangleGroups(), mixer, new Kurt.Kurt(space, mixer, _ => 1));
    }

    /// <summary>An up-facing face close above or below the feet.</summary>
    private static bool OnFloor(ArenaSpace space, Vector3 feet)
    {
        var reach = new Vector3(0f, 0f, FeetReach);
        return space.Floor(feet + reach, feet - reach, out _);
    }

    [DataFact]
    public void WalkerKeepsToTheFloor()
    {
        var space = new ArenaSpace();
        var runtime = CreateRuntime(space);
        runtime.TeleportKurt(Arena, KurtSpot, 0f);
        runtime.Update(Tick);
        var alien = runtime.Spawn(runtime.GetArenaState(Arena).Controller, Alien, Start, 0f, -1, WalkScript, ScriptRuntime.Spawning.Plain);
        Assert.NotNull(alien);
        alien.Flags |= MdkObject.FlagGravity | MdkObject.FlagCollides;

        // The pace 0x223a3 sets before the walk.
        alien.MaxSpeed = 40f;
        alien.Acceleration = 25f;
        alien.Deceleration = 30f;

        // Walk for a while: it reaches the edge, never sinks below the ring's floor and ends on it.
        var farthest = alien.Position.X;
        var lowest = alien.Position.Z;
        for (var i = 0; i < WalkTicks; i++)
        {
            runtime.Update(Tick);
            farthest = MathF.Max(farthest, alien.Position.X);
            lowest = MathF.Min(lowest, alien.Position.Z);
        }

        Assert.True(farthest > HoleWest - EdgeReach, $"the XG didn't reach the edge: {farthest}");
        Assert.True(lowest > LowestFloor - FeetReach, $"the XG fell off the floor: z {lowest}");
        Assert.True(OnFloor(space, alien.Position), $"the XG ended off the floor: {alien.Position}");
    }
}
