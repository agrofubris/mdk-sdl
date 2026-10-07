using System.Numerics;
using Mdk.Engine.Audio;
using Mdk.Formats;
using Mdk.Game.Audio;
using Mdk.Game.Collision;
using Mdk.Game.Level;
using Mdk.Game.Objects;
using Mdk.Game.Scripts;

namespace Mdk.Game.Tests;

/// <summary>Found by playtest: nothing showed a fan blowing on level 6. Its fire sparks start a
/// quarter unit below the fan's grate (0x414230) and bounced under it until they died. The original
/// puts a piece that hits something at the contact (0x4061d8), and a segment starting on a plane
/// crosses nothing (0x421470), so the updraft carries the spark through on the next tick.</summary>
public class FanSparkTests
{
    private const int Level = 6;
    private const string Arena = "OLYM_6";
    /// <summary>The arena's fan (hotspot 1): its bottom, 68 units below its top.</summary>
    private const float FanBottom = -2209f;
    private static readonly Vector3 NearFan = new(-1945f, 2900f, -2211f);
    private const float Yaw = 90f;
    /// <summary>Sparks rising through the box get this far above the grate.</summary>
    private const float Risen = 20f;
    private const int Seconds = 10;
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

    [DataFact]
    public void FanSparksRiseThroughTheGrate()
    {
        var runtime = CreateRuntime();
        runtime.TeleportKurt(Arena, NearFan, Yaw);
        var highest = float.MinValue;
        for (var i = 0; i < Seconds * SecondTicks; i++)
        {
            runtime.Kurt.Teleport(NearFan, Yaw);
            runtime.Update(ScriptRuntime.Tick);
            foreach (var spark in runtime.Debris.Pieces.Where(p => p.IsSpark && p.Arena == Arena))
            {
                highest = MathF.Max(highest, spark.Center.Z);
            }
        }

        Assert.True(highest > FanBottom + Risen, $"highest spark {highest}");
    }
}
