using System.Numerics;
using Mdk.Engine.Audio;
using Mdk.Formats;
using Mdk.Game.Audio;
using Mdk.Game.Collision;
using Mdk.Game.Level;
using Mdk.Game.Objects;
using Mdk.Game.Scripts;

namespace Mdk.Game.Tests;

/// <summary>The console's onehit: Kurt's hits kill, once the target's script has run; damage to
/// Kurt is unchanged.</summary>
public class OneHitTests
{
    private const int Level = 3;
    private const float Tick = 1f / 30f;
    private const int SettleTicks = 30;
    /// <summary>Kurt stands this far from the grunt, facing it.</summary>
    private const float Range = 25f;
    /// <summary>Among level 3's first grunts (tests/snapshot_test.sh).</summary>
    private const string Arena = "HMO_1";
    private static readonly Vector3 Grunts = new(-2f, 172f, 135f);

    private static readonly Lazy<MdkData> Data = new(() => MdkData.Find()!);
    private static readonly AudioDevice Device = new(Output.Muted);

    private static ScriptRuntime CreateRuntime()
    {
        var level = new LevelData(Data.Value, Level);
        var cmi = Cmi.Load(Data.Value.PathOf($"TRAVERSE/LEVEL{Level}/LEVEL{Level}.CMI"));
        var sprites = Bni.Load(Data.Value.PathOf("TRAVERSE/TRAVSPRT.BNI"));
        var space = new ArenaSpace();
        foreach (var arena in level.Arenas.Where(a => level.IsReachable(a.Name)))
        {
            space.Add(arena);
        }

        var mixer = new SoundMixer(Device, _ => null);
        return new ScriptRuntime(level, cmi, sprites, space, new TriangleGroups(), mixer, new Kurt.Kurt(space, mixer, _ => 1));
    }

    /// <summary>Kurt next to a grunt of the start arena, aiming at it; the grunt.</summary>
    private static MdkObject FaceGrunt(ScriptRuntime runtime)
    {
        runtime.TeleportKurt(Arena, Grunts, 0f);
        for (var i = 0; i < SettleTicks; i++)
        {
            runtime.Update(Tick);
        }

        var grunt = runtime.Objects.First(o => !o.Dead && o.Health > 1 && o.Arena == runtime.CurrentArena
            && GameStats.IsEnemy(o.TypeName) && (o.Flags & MdkObject.FlagWeakParts) == 0);
        var box = runtime.GetWorldBounds(grunt);
        var center = (box.Min + box.Max) * 0.5f;
        var at = new Vector3(center.X - Range, center.Y, grunt.Position.Z);
        runtime.TeleportKurt(runtime.CurrentArena, at, 0f);
        runtime.KurtPosition = at;
        runtime.TargetYaw = 0f;
        return grunt;
    }

    [DataFact]
    public void OneChainGunHitKills()
    {
        var runtime = CreateRuntime();
        var grunt = FaceGrunt(runtime);
        runtime.Lethality = Lethality.OneHit;

        // It dies once its script has run without giving health back.
        runtime.FireChainGun();
        runtime.Update(Tick);

        Assert.Equal(0, grunt.Health);
    }

    [DataFact]
    public void ChainGunHitHurtsWithoutOneHit()
    {
        var runtime = CreateRuntime();
        var grunt = FaceGrunt(runtime);
        var health = grunt.Health;

        runtime.FireChainGun();

        Assert.InRange(grunt.Health, 1, health - 1);
    }

    [DataFact]
    public void KurtIsHurtAsUsual()
    {
        var runtime = CreateRuntime();
        runtime.Lethality = Lethality.OneHit;
        var health = runtime.Kurt.Health;

        runtime.HurtKurt(1);

        Assert.Equal(health - 1, runtime.Kurt.Health);
    }
}
