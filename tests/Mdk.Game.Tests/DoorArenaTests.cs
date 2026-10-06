using System.Numerics;
using Mdk.Engine.Audio;
using Mdk.Formats;
using Mdk.Game.Audio;
using Mdk.Game.Collision;
using Mdk.Game.Level;
using Mdk.Game.Objects;
using Mdk.Game.Scripts;

namespace Mdk.Game.Tests;

/// <summary>Found by playtest: on level 7, back from DANT_2 to the start tunnel CDANT_1, the doorway
/// showed the sky. The tunnel's door stayed in CDANT_1; once that arena was put away the door froze
/// shut, undrawn and not solid, so it never opened to show the tunnel. The original moves a door into
/// Kurt's arena when he is on its other side (0x43cc68), and into an arena being loaded from its
/// neighbours (arena_load 0x419d00), turned around (0x43ca00).</summary>
public class DoorArenaTests
{
    private const int Level = 7;
    private const string Tunnel = "CDANT_1";
    private const string Room = "DANT_2";
    private const string Corridor = "CDANT_2";
    /// <summary>The tunnel's door into DANT_2 (spawn_connector of CDANT_1), at yaw 90.</summary>
    private static readonly Vector3 TunnelDoor = new(0f, 673f, -3f);
    private const float DoorYaw = 90f;
    private const float TurnedYaw = 270f;
    private const float Floor = -3f;
    /// <summary>The room's floor a little in, and far enough for the door to close.</summary>
    private static readonly Vector3 NearDoor = new(0f, 688f, -5f);
    private static readonly Vector3 FarFromDoor = new(0f, 750f, -8f);
    private static readonly Vector3 CorridorSpot = new(100f, 990f, -6f);
    private const int SecondTicks = 30;

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

    /// <summary>Puts Kurt somewhere and runs that many ticks.</summary>
    private static void StandAt(ScriptRuntime runtime, Vector3 feet, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            runtime.Kurt.Teleport(feet, DoorYaw);
            runtime.Update(ScriptRuntime.Tick);
        }
    }

    private static MdkObject DoorAt(ScriptRuntime runtime, Vector3 position) =>
        runtime.Objects.First(o => !o.Dead && (o.Flags & MdkObject.FlagDoor) != 0 && Vector3.Distance(o.Position, position) < 1f);

    /// <summary>Kurt goes through the tunnel's door into DANT_2, away until it shuts, and back: it
    /// opens again and shows the tunnel.</summary>
    [DataFact]
    public void DoorShowsTunnelOnTheWayBack()
    {
        var runtime = CreateRuntime();
        runtime.TeleportKurt(Tunnel, new Vector3(0f, 655f, Floor), DoorYaw);
        for (var y = 655f; y < NearDoor.Y; y += 1f)
        {
            StandAt(runtime, new Vector3(0f, y, Floor), 1);
        }

        Assert.Equal(Room, runtime.CurrentArena);

        // Into the room, out of the door's reach: it closes and the tunnel goes.
        StandAt(runtime, FarFromDoor, 3 * SecondTicks);
        Assert.DoesNotContain(Tunnel, runtime.DrawnArenas);

        // Back to the door.
        StandAt(runtime, NearDoor, 2 * SecondTicks);

        var door = DoorAt(runtime, TunnelDoor);
        Assert.Equal(Room, door.Arena);
        Assert.Equal(Tunnel, door.Connects);
        Assert.Contains(Tunnel, runtime.DrawnArenas);
    }

    /// <summary>Loading DANT_2 ahead (from CDANT_2) pulls the tunnel's door into it, turned around.</summary>
    [DataFact]
    public void LoadedArenaPullsItsDoors()
    {
        var runtime = CreateRuntime();
        runtime.TeleportKurt(Tunnel, new Vector3(0f, 655f, Floor), DoorYaw);
        StandAt(runtime, new Vector3(0f, 655f, Floor), 1);
        runtime.TeleportKurt(Corridor, CorridorSpot, 0f);
        StandAt(runtime, CorridorSpot, 1);

        runtime.PreloadArena(Room);

        var door = DoorAt(runtime, TunnelDoor);
        Assert.Equal(Room, door.Arena);
        Assert.Equal(Tunnel, door.Connects);
        Assert.Equal(TurnedYaw, door.Yaw, 0.01f);
    }
}
