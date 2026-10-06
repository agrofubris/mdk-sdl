using System.Numerics;
using Mdk.Engine.Audio;
using Mdk.Formats;
using Mdk.Game.Audio;
using Mdk.Game.Collision;
using Mdk.Game.Level;
using Mdk.Game.Scripts;

namespace Mdk.Game.Tests;

/// <summary>Found by playtest: Kurt's arena came from the arena boxes, so on level 4's MEAT_7 floor,
/// just before the doorway to CMEAT_7 (connection 1012, the plane y = 14822), he was already in the
/// corridor; the scripts then dropped MEAT_7 from the solid arenas and he fell through its floor.
/// The original switches only when his move crosses the doorway (0x41c550).</summary>
public class ArenaSwitchTests
{
    private const int Level = 4;
    private const string Room = "MEAT_7";
    private const string Corridor = "CMEAT_7";
    private const float Doorway = 14822f;
    private const float Floor = 21f;

    private static readonly MdkData Data = MdkData.Find() ?? throw new InvalidOperationException("MDK data not found");
    private static readonly AudioDevice Device = new(Output.Muted);

    private static ScriptRuntime CreateRuntime() => CreateRuntime(new ArenaSpace());

    private static ScriptRuntime CreateRuntime(ArenaSpace space)
    {
        var level = new LevelData(Data, Level);
        var cmi = Cmi.Load(Data.PathOf($"TRAVERSE/LEVEL{Level}/LEVEL{Level}.CMI"));
        var sprites = Bni.Load(Data.PathOf("TRAVERSE/TRAVSPRT.BNI"));
        foreach (var arena in level.Arenas.Where(a => level.IsReachable(a.Name)))
        {
            space.Add(arena);
        }

        var mixer = new SoundMixer(Device, _ => null);
        return new ScriptRuntime(level, cmi, sprites, space, new TriangleGroups(), mixer, new Kurt.Kurt(space, mixer, _ => 1));
    }

    /// <summary>Puts Kurt at a y on the floor by the doorway and runs a tick.</summary>
    private static void StepTo(ScriptRuntime runtime, float y)
    {
        runtime.Kurt.Teleport(new Vector3(0f, y, Floor), runtime.Kurt.Yaw);
        runtime.Update(ScriptRuntime.Tick);
    }

    [DataFact]
    public void ArenaSwitchesAtDoorway()
    {
        var runtime = CreateRuntime();
        runtime.TeleportKurt(Room, new Vector3(0f, Doorway - 100f, Floor), 90f);
        runtime.Update(ScriptRuntime.Tick);

        // Inside the corridor's box, but not through the doorway yet.
        StepTo(runtime, Doorway - 1f);
        Assert.Equal(Room, runtime.CurrentArena);

        // Through: the room stays as the active second arena.
        StepTo(runtime, Doorway + 3f);
        Assert.Equal(Corridor, runtime.CurrentArena);
        Assert.Equal(Room, runtime.SecondArena);
        Assert.True(runtime.SecondActive);

        // And back.
        StepTo(runtime, Doorway - 1f);
        Assert.Equal(Room, runtime.CurrentArena);
        Assert.Equal(Corridor, runtime.SecondArena);
    }

    /// <summary>The first tick has no move (script_runtime.gd): no velocity from the origin.</summary>
    [DataFact]
    public void FirstTickHasNoMove()
    {
        var runtime = CreateRuntime();
        runtime.Kurt.Teleport(new Vector3(0f, Doorway - 100f, Floor), 90f);

        runtime.Update(ScriptRuntime.Tick);

        Assert.Equal(Vector3.Zero, runtime.KurtVelocity);
    }

    /// <summary>An arena no connection leads to isn't solid until a script teleports Kurt into it
    /// (level.gd enter_arena): then it's in Kurt's space.</summary>
    [DataFact]
    public void TeleportMakesUnreachableArenaSolid()
    {
        var space = new ArenaSpace();
        var runtime = CreateRuntime(space);
        var arena = runtime.Level.Arenas.First(a => !runtime.Level.IsReachable(a.Name) && a.Vertices.Length > 0);
        var center = (arena.Vertices.Aggregate(Vector3.Min) + arena.Vertices.Aggregate(Vector3.Max)) / 2f;
        Assert.DoesNotContain(space.At(center), b => b.Arena.Name == arena.Name);

        runtime.TeleportKurt(arena.Name, center, 0f);

        Assert.Contains(space.At(center), b => b.Arena.Name == arena.Name);
    }
}
