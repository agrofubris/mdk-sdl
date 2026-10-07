using System.Numerics;
using Mdk.Engine.Audio;
using Mdk.Engine.Platform;
using Mdk.Formats;
using Mdk.Game.Audio;
using Mdk.Game.Collision;
using Mdk.Game.Kurt;
using Mdk.Game.Level;
using Mdk.Game.Scripts;

namespace Mdk.Game.Tests;

/// <summary>LEVEL6's rolling boulders (OLYM_6's XBO, spawned when Kurt enters the floor area).</summary>
public class RollingBallTests
{
    private const int Level = 6;
    private const string Room = "OLYM_6";
    private const string Ball = "XBO";
    /// <summary>Its shots (hurt_kurt 6) would blur the ball's damage.</summary>
    private const string Grunt = "XC";
    private const float Tick = 1f / 30f;
    private const int RollTicks = 150;
    private const int HitTicks = 300;
    private const float FloorTolerance = 0.5f;
    private const float RayReach = 50f;

    /// <summary>Inside the spawn box (if_kurt_in_box at 0xcc34).</summary>
    private static readonly Vector3 Entry = new(-1900f, 3100f, -2160f);

    /// <summary>On the floor in the first ball's path.</summary>
    private static readonly Vector3 InPath = new(-1903f, 3010f, -2202f);

    private static readonly Lazy<MdkData> Data = new(() => MdkData.Find() ?? throw new InvalidOperationException("MDK data not found"));
    private static readonly Lazy<AudioDevice> Device = new(() => new AudioDevice(Output.Muted));

    private static ScriptRuntime CreateRuntime(ArenaSpace space, Difficulty difficulty)
    {
        var data = Data.Value;
        var level = new LevelData(data, Level);
        var cmi = Cmi.Load(data.PathOf($"TRAVERSE/LEVEL{Level}/LEVEL{Level}.CMI"));
        var sprites = Bni.Load(data.PathOf("TRAVERSE/TRAVSPRT.BNI"));
        var mixer = new SoundMixer(Device.Value, _ => null);
        var kurt = new Kurt.Kurt(space, mixer, _ => 1);
        kurt.Inventory.Difficulty = difficulty;
        var runtime = new ScriptRuntime(level, cmi, sprites, space, new TriangleGroups(), mixer, kurt);
        runtime.TeleportKurt(Room, Entry, 0f);
        return runtime;
    }

    /// <summary>The ball rests on its origin; it's drawn lifted by its height offset (set_height_offset
    /// 5 vs a radius of 5.15), so only a sliver sinks into the floor (0x43b65c: z + obj+0x5c).</summary>
    [DataFact]
    public void BallsRollOnTheFloor()
    {
        var space = new ArenaSpace();
        var runtime = CreateRuntime(space, Difficulty.Normal);
        for (var i = 0; i < RollTicks; i++)
        {
            runtime.Update(Tick);
        }

        var balls = runtime.Objects.Where(o => o.TypeName == Ball && !o.Dead).ToList();
        Assert.NotEmpty(balls);
        foreach (var ball in balls)
        {
            var lowest = ball.Model!.PartList.SelectMany(p => p.Vertices).Min(v => Vector3.Transform(v, ball.Transform).Z);
            var up = new Vector3(0f, 0f, RayReach);
            Assert.True(space.Floor(ball.Position + up, ball.Position - up, out var floor));
            Assert.InRange(lowest, floor.Z - FloorTolerance, floor.Z + FloorTolerance);
        }
    }

    /// <summary>A ball touching Kurt runs hurt_kurt 4 (0xcdb4): 2/3 on easy, doubled on hard, then knocks
    /// him down (set_hurt_flash 1), which makes him invulnerable.</summary>
    [DataTheory]
    [InlineData(Difficulty.Easy, 2)]
    [InlineData(Difficulty.Normal, 4)]
    [InlineData(Difficulty.Hard, 8)]
    public void BallHurtsKurt(Difficulty difficulty, int damage)
    {
        var runtime = CreateRuntime(new ArenaSpace(), difficulty);
        runtime.Update(Tick);
        runtime.Kill(runtime.Objects.First(o => o.TypeName == Grunt && o.Arena == Room));
        runtime.Kurt.Teleport(InPath, 0f);
        var start = runtime.Kurt.Health;
        for (var i = 0; i < HitTicks && runtime.Kurt.Health == start; i++)
        {
            runtime.Kurt.Update(new Input(), Tick);
            runtime.Update(Tick);
        }

        Assert.Equal(start - damage, runtime.Kurt.Health);
    }
}
