using System.Numerics;
using Mdk.Engine.Audio;
using Mdk.Formats;
using Mdk.Game.Audio;
using Mdk.Game.Collision;
using Mdk.Game.Level;
using Mdk.Game.Objects;
using Mdk.Game.Scripts;

namespace Mdk.Game.Tests;

/// <summary>Found by playtest: LEVEL8's GUNT_5 missile aliens XT (set_height_offset 8, a model
/// centred on its origin) sank halfway into the floor after their move_to down to the floor. The
/// original sweeps an object's vertical move with its box lowered to z − height offset (0x45e810),
/// so it stops with its origin that high above the floor.</summary>
public class HeightOffsetTests
{
    private const int Level = 8;
    private const string Arena = "GUNT_5";
    private const string Alien = "XT";
    private const float Tick = 1f / 30f;
    private const float Tolerance = 0.5f;
    private const float RayReach = 10f;
    private const int FallTicks = 90;
    private const int MoveTicks = 25 * 30;

    /// <summary>Inside if_kurt_in_rect (−260, 1636)–(−252, 1773): spawns the XT pair.</summary>
    private static readonly Vector3 Trigger = new(-256f, 1700f, -226f);
    /// <summary>On GUNT_5's lower floor, where the first XT sees Kurt and moves to (−220, 1640, −226).</summary>
    private static readonly Vector3 InSight = new(-240f, 1580f, -226f);
    /// <summary>Above the lower floor (z −226).</summary>
    private static readonly Vector3 AboveFloor = new(-220f, 1640f, -200f);

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

    /// <summary>The model's lowest point and the floor under the object.</summary>
    private static (float Lowest, float Floor) Footing(ArenaSpace space, MdkObject obj)
    {
        var lowest = obj.PoseParts().SelectMany(p => p).Min(v => Vector3.Transform(v, obj.Transform).Z);
        var up = new Vector3(0f, 0f, RayReach);
        Assert.True(space.Floor(obj.Position + up, obj.Position - up, out var floor));
        return (lowest, floor.Z);
    }

    [DataFact]
    public void FallStopsAtTheHeightOffset()
    {
        var space = new ArenaSpace();
        var runtime = CreateRuntime(space);
        var obj = runtime.Spawn(runtime.GetArenaState(Arena).Controller, Alien, AboveFloor, 0f, -1, 0, ScriptRuntime.Spawning.Plain);
        Assert.NotNull(obj);
        obj.HeightOffset = 8f;
        obj.Flags = MdkObject.FlagGravity | MdkObject.FlagCollides;
        for (var i = 0; i < FallTicks; i++)
        {
            runtime.Motion.Update(obj);
        }

        var (_, floor) = Footing(space, obj);
        Assert.InRange(obj.Position.Z, floor + obj.HeightOffset - Tolerance, floor + obj.HeightOffset + Tolerance);
    }

    [DataFact]
    public void MovedAlienStandsOnTheFloor()
    {
        var space = new ArenaSpace();
        var runtime = CreateRuntime(space);
        runtime.TeleportKurt(Arena, Trigger, 0f);
        runtime.Update(Tick);
        runtime.TeleportKurt(Arena, InSight, 0f);
        var aliens = runtime.Objects.Where(o => o.TypeName == Alien).ToList();
        Assert.NotEmpty(aliens);
        for (var i = 0; i < MoveTicks; i++)
        {
            runtime.Update(Tick);
        }

        foreach (var alien in aliens)
        {
            var (lowest, floor) = Footing(space, alien);
            Assert.InRange(lowest, floor - Tolerance, floor + Tolerance);
        }
    }
}
