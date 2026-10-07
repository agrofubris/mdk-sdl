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

/// <summary>Doors locked until nuked: their scripts wait on flag word 5 bit 6 (their own door
/// state, locked), then set or clear an arena flag (the key stops respawning). While word 5 read
/// the linked object's flags they went on at once.</summary>
public class LockedDoorTests
{
    private const int SecondTicks = 30;
    private const int SettleSeconds = 2;
    private const int NukeSeconds = 10;
    private const float DoorReach = 2f;
    private const float Halfway = 0.5f;

    private static readonly MdkData Data = MdkData.Find() ?? throw new InvalidOperationException("MDK data not found");
    private static readonly AudioDevice Device = new(Output.Muted);

    private static ScriptRuntime CreateRuntime(int number)
    {
        var level = new LevelData(Data, number);
        var cmi = Cmi.Load(Data.PathOf($"TRAVERSE/LEVEL{number}/LEVEL{number}.CMI"));
        var sprites = Bni.Load(Data.PathOf("TRAVERSE/TRAVSPRT.BNI"));
        var space = new ArenaSpace();
        foreach (var arena in level.Arenas.Where(a => level.IsReachable(a.Name)))
        {
            space.Add(arena);
        }

        var mixer = new SoundMixer(Device, _ => null);
        return new ScriptRuntime(level, cmi, sprites, space, new TriangleGroups(), mixer, new Kurt.Kurt(space, mixer, _ => 1));
    }

    private static void StandAt(ScriptRuntime runtime, Vector3 feet, float yaw, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            runtime.Kurt.Teleport(feet, yaw);
            runtime.Update(ScriptRuntime.Tick);
        }
    }

    /// <summary>Kurt stands 15 units before the door, facing it: it stays locked and its script
    /// waits. He throws the key; the nuke unlocks the door, its script goes on, and the door opens
    /// as he steps closer. (CDANT_9's door tests word 5 bit 64 &amp; 31 = 0, "open": it goes on at once,
    /// in the original too.)</summary>
    [DataTheory]
    [InlineData(6, "OLYM_1", -1142f, -931f, -73f, -1142f, -946f, 90f)]
    [InlineData(7, "DANT_1", 0f, 481f, 15f, 0f, 466f, 90f)]
    [InlineData(7, "DANT_5", 252f, 3633f, -71f, 252f, 3618f, 90f)]
    [InlineData(7, "CDANT_5", 252f, 3633f, -71f, 252f, 3648f, 270f)]
    [InlineData(7, "DANT_9", 276f, 4782f, -37f, 261f, 4782f, 0f)]
    [InlineData(8, "GUNT_1", -68f, 93f, 12f, -53f, 93f, 180f)]
    [InlineData(8, "GUNT_6", 75f, 2571f, -269f, 90f, 2571f, 180f)]
    [InlineData(8, "CGUNT_6", 75f, 2571f, -269f, 60f, 2571f, 0f)]
    public void NukeUnlocksDoor(int level, string arena, float x, float y, float z, float kurtX, float kurtY, float yaw)
    {
        var runtime = CreateRuntime(level);
        runtime.Kurt.Mortality = Mortality.God;
        var at = new Vector3(x, y, z);
        var feet = new Vector3(kurtX, kurtY, z);
        runtime.TeleportKurt(arena, feet, yaw);
        StandAt(runtime, feet, yaw, SettleSeconds * SecondTicks);

        // Locked, its script waiting.
        var door = runtime.Objects.First(o => !o.Dead && (o.Flags & MdkObject.FlagDoor) != 0 && Vector3.Distance(o.Position, at) < DoorReach);
        var waiting = door.Restart;
        StandAt(runtime, feet, yaw, SettleSeconds * SecondTicks);
        Assert.Equal(waiting, door.Restart);
        Assert.NotEqual(0, door.DoorState & ObjectBehaviors.DoorLocked);

        // The nuke.
        var health = Inventory.MaxHealth;
        runtime.Kurt.Inventory.Collect("SW_KEY", ref health);
        runtime.Items.UseItem();
        StandAt(runtime, feet, yaw, NukeSeconds * SecondTicks);
        Assert.NotEqual(waiting, door.Restart);
        Assert.Equal(0, door.DoorState & ObjectBehaviors.DoorLocked);

        // Closer: it opens.
        var closer = Vector3.Lerp(feet, at, Halfway);
        StandAt(runtime, closer, yaw, SettleSeconds * SecondTicks);
        Assert.NotEqual(0, door.DoorState & (ObjectBehaviors.DoorOpen | ObjectBehaviors.DoorOpening));
    }
}
