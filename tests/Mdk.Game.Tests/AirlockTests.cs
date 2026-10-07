using System.Numerics;
using Mdk.Engine.Audio;
using Mdk.Formats;
using Mdk.Game.Audio;
using Mdk.Game.Collision;
using Mdk.Game.Kurt;
using Mdk.Game.Level;
using Mdk.Game.Objects;
using Mdk.Game.Scripts;

namespace Mdk.Game.Tests;

/// <summary>Found by playtest: LEVEL4's sub airlock kept Kurt in. The nuke opens the hatch into
/// it; past its door #1998 that door waits until it is shut (flag word 5 bit 3, its own door
/// state), locks and opens #1999 to MEAT_6. Word 5 read the linked object's flags, so #1999
/// never opened.
/// <code>
///   MEAT_5 ──hatch #1000 (y 13353)──► airlock ──#1998 (y 13375)──► CMEAT_5 ──#1999 (y 13406)──► MEAT_6
/// </code></summary>
public class AirlockTests
{
    private const int Level = 4;
    private const string Arena = "MEAT_5";
    private const float X = -126f;
    private const float Floor = -1989f;
    private const float Ahead = 90f;
    /// <summary>In the nuke's box (y 13306…13351), the key thrown along +y lands in it.</summary>
    private const float NukeSpot = 13310f;
    /// <summary>In the airlock (y 13354…13376), where the hatch shuts behind Kurt and unlocks #1998.</summary>
    private const float Airlock = 13365f;
    /// <summary>Past #1998, before #1999 (within its distance 10).</summary>
    private const float BeforeLastDoor = 13398f;
    private const int OutDoor = 1999;
    private const int SecondTicks = 30;
    private const int NukeSeconds = 10;
    private const int WaitSeconds = 5;

    private static readonly MdkData Data = MdkData.Find() ?? throw new InvalidOperationException("MDK data not found");
    private static readonly AudioDevice Device = new(Output.Muted);

    private static ScriptRuntime CreateRuntime()
    {
        var level = new LevelData(Data, Level);
        var cmi = Cmi.Load(Data.PathOf($"TRAVERSE/LEVEL{Level}/LEVEL{Level}.CMI"));
        var sprites = Bni.Load(Data.PathOf("TRAVERSE/TRAVSPRT.BNI"));
        var space = new ArenaSpace();
        foreach (var arena in level.Arenas.Where(a => level.IsReachable(a.Name)))
        {
            space.Add(arena);
        }

        var mixer = new SoundMixer(Device, _ => null);
        return new ScriptRuntime(level, cmi, sprites, space, new TriangleGroups(), mixer, new Kurt.Kurt(space, mixer, _ => 1));
    }

    /// <summary>Holds Kurt at y for that many ticks.</summary>
    private static void StandAt(ScriptRuntime runtime, float y, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            runtime.Kurt.Teleport(new Vector3(X, y, Floor), Ahead);
            runtime.Update(ScriptRuntime.Tick);
        }
    }

    /// <summary>Walks Kurt along +y, a unit a tick.</summary>
    private static void Walk(ScriptRuntime runtime, float from, float to)
    {
        for (var y = from; y < to; y += 1f)
        {
            StandAt(runtime, y, 1);
        }
    }

    [DataFact]
    public void AirlockOpensToMeat6()
    {
        var runtime = CreateRuntime();
        runtime.Kurt.Mortality = Mortality.God;
        runtime.TeleportKurt(Arena, new Vector3(X, NukeSpot, Floor), Ahead);
        StandAt(runtime, NukeSpot, 1);

        // The nuke goes off by the hatch.
        var health = Inventory.MaxHealth;
        runtime.Kurt.Inventory.Collect("SW_KEY", ref health);
        runtime.Items.UseItem();
        StandAt(runtime, NukeSpot, NukeSeconds * SecondTicks);

        // Through the hatch into the airlock; it shuts behind him.
        Walk(runtime, NukeSpot, Airlock);
        StandAt(runtime, Airlock, WaitSeconds * SecondTicks);

        // Through #1998, then wait for it to shut.
        Walk(runtime, Airlock, BeforeLastDoor);
        StandAt(runtime, BeforeLastDoor, WaitSeconds * SecondTicks);

        var door = runtime.Objects.First(o => !o.Dead && (o.Flags & MdkObject.FlagDoor) != 0 && o.InstanceId == OutDoor);
        Assert.NotEqual(0, door.DoorState & (ObjectBehaviors.DoorOpen | ObjectBehaviors.DoorOpening));
    }
}
