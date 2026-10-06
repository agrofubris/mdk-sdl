using System.Numerics;
using Mdk.Engine.Audio;
using Mdk.Formats;
using Mdk.Game.Audio;
using Mdk.Game.Collision;
using Mdk.Game.Level;
using Mdk.Game.Scripts;

namespace Mdk.Game.Tests;

/// <summary>Level 7's practice room (DANT_2, docs/practice_room.md of the Godot port).</summary>
public class PracticeRoomTests
{
    private const int Level = 7;
    private const string Room = "DANT_2";
    private const string Crate = "XBANG";
    private const string Target = "XGTARG";
    private const float Tick = 1f / 30f;
    private const int SettleTicks = 30;
    private const int LandingTicks = 300;
    private const float Tolerance = 1f;
    private static readonly Vector3 KurtSpot = new(0f, 900f, -27f);

    /// <summary>The pedestal tops XGEN's three targets fly to (move_to_point at 0x5458, 0x5490, 0x54c8).</summary>
    private static readonly Vector3[] Pedestals = [new(-17f, 941f, -20f), new(-24f, 959f, -15f), new(-23f, 977f, -10f)];

    private static readonly MdkData Data = MdkData.Find() ?? throw new InvalidOperationException("MDK data not found");
    private static readonly AudioDevice Device = new(Output.Muted);

    private static ScriptRuntime CreateRuntime()
    {
        var level = new LevelData(Data, Level);
        var cmi = Cmi.Load(Data.PathOf($"TRAVERSE/LEVEL{Level}/LEVEL{Level}.CMI"));
        var sprites = Bni.Load(Data.PathOf("TRAVERSE/TRAVSPRT.BNI"));
        var space = new ArenaSpace();
        var mixer = new SoundMixer(Device, _ => null);
        return new ScriptRuntime(level, cmi, sprites, space, new TriangleGroups(), mixer, new Kurt.Kurt(space, mixer, _ => 1));
    }

    private static void Run(ScriptRuntime runtime, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            runtime.Update(Tick);
        }
    }

    /// <summary>Shooting the crate makes XGEN spit three targets; each lands on its pedestal
    /// (move_to_point's Manhattan step keeps them high enough to clear the glass sides).</summary>
    [DataFact]
    public void TargetsLandOnThePedestals()
    {
        var runtime = CreateRuntime();
        runtime.TeleportKurt(Room, KurtSpot, 90f);
        Run(runtime, SettleTicks);
        runtime.Kill(runtime.Objects.First(o => o.TypeName == Crate && o.Arena == Room));

        Run(runtime, LandingTicks);

        var targets = runtime.Objects.Where(o => o.TypeName == Target && !o.Dead).ToList();
        Assert.Equal(Pedestals.Length, targets.Count);
        foreach (var pedestal in Pedestals)
        {
            Assert.Contains(targets, t => Vector3.Distance(t.Position, pedestal) < Tolerance);
        }
    }
}
