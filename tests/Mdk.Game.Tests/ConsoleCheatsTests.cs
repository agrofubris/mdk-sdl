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

/// <summary>What the console's cheats do to Kurt and the level: god, noclip, the arenas' stops, enemies.</summary>
public class ConsoleCheatsTests
{
    private const int Level = 3;
    private const float Step = 1f / 60f;
    private const int Frames = 5;
    private const int Fatal = 1000;
    /// <summary>Above level 3's start pad.</summary>
    private static readonly Vector3 Pad = new(-4f, 0f, 195f);

    private static readonly Lazy<LevelData> Data = new(() => new LevelData(MdkData.Find()!, Level));
    private static readonly AudioDevice Device = new(Output.Muted);

    private static ArenaSpace SpaceOf(LevelData level)
    {
        var space = new ArenaSpace();
        foreach (var arena in level.Arenas.Where(a => level.IsReachable(a.Name)))
        {
            space.Add(arena);
        }

        return space;
    }

    private static Kurt.Kurt NewKurt() =>
        new(SpaceOf(Data.Value), new SoundMixer(Device, _ => null), _ => Frames) { Feet = Pad };

    private static void Run(Kurt.Kurt kurt, Input input, float seconds)
    {
        for (var t = 0f; t < seconds; t += Step)
        {
            kurt.Update(input, Step);
        }
    }

    [DataFact]
    public void GodModeTakesNoDamage()
    {
        var kurt = NewKurt();
        Run(kurt, new Input(), 1f);
        kurt.Mortality = Mortality.God;
        kurt.Hurt(Fatal);
        Run(kurt, new Input(), 1f);
        Assert.Equal(Kurt.Kurt.MaxHealth, kurt.Health);

        kurt.Mortality = Mortality.Mortal;
        kurt.Hurt(Fatal);
        Assert.Equal(0, kurt.Health);
    }

    [DataFact]
    public void NoclipFliesWithoutFalling()
    {
        var kurt = NewKurt();
        kurt.Clipping = Clipping.Off;
        kurt.Yaw = 0f;
        var input = new Input();
        input.Hold(Key.Forward, Input.State.Down);
        Run(kurt, input, 1f);

        // Straight ahead (yaw 0 is +X), level, through whatever is there.
        Assert.True(kurt.Feet.X > Pad.X + 10f);
        Assert.Equal(Pad.Z, kurt.Feet.Z, 3);

        input.Hold(Key.Forward, Input.State.Up);
        input.Hold(Key.Jump, Input.State.Down);
        Run(kurt, input, 0.5f);
        Assert.True(kurt.Feet.Z > Pad.Z + 5f);
    }

    [DataFact]
    public void EveryArenaHasAStop()
    {
        var level = Data.Value;
        var space = SpaceOf(level);
        var start = level.Dti.Arenas[level.Dti.StartArena].Name;
        var stop = ArenaStops.Find(level, space, start);
        Assert.NotNull(stop);
        Assert.Contains(space.At(stop.Value), b => b.Arena.Name == start);
        Assert.Null(ArenaStops.Find(level, space, "NOWHERE"));
    }

    [Fact]
    public void EnemiesAreTheOriginalsTypes()
    {
        Assert.True(GameStats.IsEnemy("xb"));
        Assert.False(GameStats.IsEnemy("SW_HBOMB"));
    }
}
