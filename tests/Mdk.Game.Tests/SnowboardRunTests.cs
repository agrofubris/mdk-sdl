using System.Numerics;
using Mdk.Engine.Audio;
using Mdk.Engine.Platform;
using Mdk.Formats;
using Mdk.Game.Audio;
using Mdk.Game.Collision;
using Mdk.Game.Level;
using Mdk.Game.Scripts;

namespace Mdk.Game.Tests;

/// <summary>Found by playtest: LEVEL4's first board run (MEAT_1 → CMEAT_1 → MEAT_3) never ended. Its
/// script waits in MEAT_1 for Kurt in a box of MEAT_3; the board stayed in MEAT_1, frozen once Kurt
/// left CMEAT_1. The original moves objects with flag 0x80000 (the ridden board) into the arena whose
/// connection they cross (0x45e810, 0x43ca00).</summary>
public class SnowboardRunTests
{
    private const int Level = 4;
    private const float Step = 1f / 60f;
    private const int StepsPerSecond = 60;
    private const string Start = "MEAT_1";
    private const string End = "MEAT_3";
    /// <summary>Kurt lands on group 1 of MEAT_1 (its hit script spawns the board and XS); XS's death
    /// makes the board rideable.</summary>
    private const int LandingGroup = 1;
    private static readonly Vector3 Landing = new(0f, 0f, 80f);
    private static readonly Vector3 AboveBoard = new(0f, 0f, 3f);
    private const float Yaw = 90f;
    /// <summary>The run takes about 45 s; the script lets him off in MEAT_3 (y −2674…3176).</summary>
    private const int RunSeconds = 60;

    private static readonly MdkData Data = MdkData.Find() ?? throw new InvalidOperationException("MDK data not found");
    private static readonly AudioDevice Device = new(Output.Muted);

    private static (ScriptRuntime Runtime, ArenaSpace Space) CreateRuntime()
    {
        var level = new LevelData(Data, Level);
        var cmi = Cmi.Load(Data.PathOf($"TRAVERSE/LEVEL{Level}/LEVEL{Level}.CMI"));
        var sprites = Bni.Load(Data.PathOf("TRAVERSE/TRAVSPRT.BNI"));
        var space = new ArenaSpace();
        var groups = new TriangleGroups();
        foreach (var arena in level.Arenas.Where(a => level.IsReachable(a.Name)))
        {
            space.Add(arena);
            groups.Add(arena);
        }

        var mixer = new SoundMixer(Device, _ => null);
        var runtime = new ScriptRuntime(level, cmi, sprites, space, groups, mixer, new Kurt.Kurt(space, mixer, _ => 1));
        return (runtime, space);
    }

    /// <summary>Kurt and the scripts for up to that many steps, as the game runs them.</summary>
    private static void Run(ScriptRuntime runtime, ArenaSpace space, int steps, Func<bool>? until = null)
    {
        var input = new Input();
        for (var i = 0; i < steps && until?.Invoke() != true; i++)
        {
            runtime.Kurt.Update(input, Step);
            runtime.Update(Step);
            space.SetSolid(runtime.SolidArenas);
        }
    }

    [DataFact]
    public void FirstRunEndsInMeat3()
    {
        var (runtime, space) = CreateRuntime();
        runtime.TeleportKurt(Start, Landing, Yaw);
        Run(runtime, space, StepsPerSecond / 2);
        runtime.HitGroup(Start, LandingGroup, 0, ScriptRuntime.HitKurt, ScriptRuntime.HitTypeKurt);
        Run(runtime, space, StepsPerSecond);
        runtime.Kill(runtime.FindObjectNamed("XS")!);
        Run(runtime, space, StepsPerSecond);

        // Onto the board.
        var board = runtime.FindObjectNamed("XSNOWB")!;
        runtime.TeleportKurt(Start, board.Position + AboveBoard, Yaw);
        Run(runtime, space, StepsPerSecond, () => runtime.Rides.OnBoard());
        Assert.True(runtime.Rides.OnBoard());

        // Down the run, without keys: the script lets him off in MEAT_3.
        Run(runtime, space, RunSeconds * StepsPerSecond, () => !runtime.Rides.OnBoard());

        Assert.False(runtime.Rides.OnBoard());
        Assert.Equal(End, runtime.CurrentArena);
        Assert.Equal(End, board.Arena);
    }
}
