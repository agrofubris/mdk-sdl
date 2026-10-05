using System.Numerics;
using Mdk.Engine.Audio;
using Mdk.Engine.Platform;
using Mdk.Formats;
using Mdk.Game.Audio;
using Mdk.Game.Collision;
using Mdk.Game.Kurt;
using State = Mdk.Game.Kurt.Kurt.State;

namespace Mdk.Game.Tests;

/// <summary>Kurt's hits (hurt_kurt 0x46a604), knock-downs and death, standing on level 3's start pad.</summary>
public class KurtTests
{
    private const int Level = 3;
    private const float Step = 1f / 60f;
    /// <summary>Frames of every animation: a knock-down then takes 10 ticks.</summary>
    private const int Frames = 5;
    /// <summary>Above the pad (the Godot port's Kurt stands at z 192).</summary>
    private static readonly Vector3 Pad = new(-4f, 0f, 195f);

    private static readonly MdkData Data = MdkData.Find() ?? throw new InvalidOperationException("MDK data not found");
    private static readonly AudioDevice Device = new(Output.Muted);
    private static readonly ArenaSpace Space = CreateSpace();

    private static ArenaSpace CreateSpace()
    {
        var dti = Dti.Load(Data.PathOf($"TRAVERSE/LEVEL{Level}/LEVEL{Level}.DTI"));
        var mto = Mto.Load(Data.PathOf($"TRAVERSE/LEVEL{Level}/LEVEL{Level}O.MTO"));
        var space = new ArenaSpace();
        space.Add(mto.GetArena(dti.Arenas[dti.StartArena].Name));
        return space;
    }

    /// <summary>Kurt standing on the pad.</summary>
    private static Kurt.Kurt Standing(Difficulty difficulty = Difficulty.Normal)
    {
        var kurt = new Kurt.Kurt(Space, new SoundMixer(Device, _ => null), _ => Frames) { Feet = Pad };
        kurt.Inventory.Difficulty = difficulty;
        Run(kurt, new Input(), 1f);
        Assert.True(kurt.OnFloor);
        return kurt;
    }

    private static void Run(Kurt.Kurt kurt, Input input, float seconds)
    {
        for (var t = 0f; t < seconds; t += Step)
        {
            kurt.Update(input, Step);
        }
    }

    [Theory]
    [InlineData(Difficulty.Easy, 9, 6)]
    [InlineData(Difficulty.Easy, 1, 1)]
    [InlineData(Difficulty.Normal, 9, 9)]
    [InlineData(Difficulty.Hard, 9, 18)]
    public void DamageIsScaledByDifficulty(Difficulty difficulty, int damage, int taken)
    {
        var kurt = Standing(difficulty);
        kurt.Hurt(damage);
        Assert.Equal(Kurt.Kurt.MaxHealth - taken, kurt.Health);
    }

    [Fact]
    public void InvulnerableKurtTakesNoDamage()
    {
        var kurt = Standing();
        kurt.Invulnerable = 1f;
        kurt.Hurt(10);
        Assert.Equal(Kurt.Kurt.MaxHealth, kurt.Health);
        Assert.Equal(0f, kurt.KnockDamage);
    }

    [Fact]
    public void KnockDamageAddsUpAndDrains()
    {
        var kurt = Standing();
        kurt.Hurt(2);
        kurt.Hurt(2);
        Assert.Equal(4f, kurt.KnockDamage);

        // It drains by 2 per second.
        Run(kurt, new Input(), 0.5f);
        Assert.Equal(3f, kurt.KnockDamage, 0.1f);
        Assert.Equal(State.Still, kurt.Current);
    }

    [Fact]
    public void FiveDamageKnocksKurtDown()
    {
        var kurt = Standing();
        kurt.Hurt(3);
        kurt.Hurt(2);
        kurt.Update(new Input(), Step);
        Assert.Equal(State.Knocked, kurt.Current);
        Assert.True(kurt.Invulnerable > 0f);

        // Knocked down he takes no damage, then he gets up.
        kurt.Hurt(10);
        Assert.Equal(Kurt.Kurt.MaxHealth - 5, kurt.Health);
        Run(kurt, new Input(), 1f);
        Assert.Equal(State.Still, kurt.Current);
    }

    [Fact]
    public void KnockedDownKurtStopsFiring()
    {
        var kurt = Standing();
        var input = new Input();
        input.Hold(Key.Fire, Input.State.Down);
        kurt.Update(input, Step);
        Assert.True(kurt.Firing);
        Assert.Equal(State.Shot, kurt.Current);

        kurt.KnockDown(Vector2.Zero);
        kurt.Update(input, Step);
        Assert.False(kurt.Firing);
    }

    [Fact]
    public void KurtDiesAndTheSkullFadesIn()
    {
        var kurt = Standing();
        var died = false;
        kurt.Died += () => died = true;
        kurt.Hurt(Kurt.Kurt.MaxHealth);
        kurt.Update(new Input(), Step);
        Assert.Equal(State.Dead, kurt.Current);

        // The skull fades in by 2 per tick up to 255: about 4 seconds.
        Run(kurt, new Input(), 5f);
        Assert.True(died);
    }

    /// <summary>A fan's updraft of 10 u/s from z 0 to 20 (fans.gd type 6), Kurt in the air above no arena.</summary>
    private static Kurt.Kurt InFan(Vector3 feet, Func<float, float, float> updraft) =>
        new(new ArenaSpace(), new SoundMixer(Device, _ => null), _ => Frames) { Feet = feet, Updraft = updraft };

    [Fact]
    public void KurtRisesInAnUpdraft()
    {
        const float Lift = 10f;
        const float Acceleration = 64f;
        Kurt.Kurt? kurt = null;
        kurt = InFan(new Vector3(0f, 0f, 5f), (vz, dt) => kurt!.Feet.Z < 20f ? MathF.Min(vz + (Lift + Acceleration) * dt, Lift) : float.NaN);

        Run(kurt, new Input(), 1f);

        Assert.True(kurt.InUpdraft);
        Assert.True(kurt.ChuteOpen);
        Assert.True(kurt.Feet.Z > 5f);
    }

    [Fact]
    public void KurtLeavesAnUpdraftAtMost40()
    {
        var kurt = InFan(new Vector3(0f, 0f, 100f), (_, _) => float.NaN);
        kurt.VerticalSpeed = 60f;

        kurt.Update(new Input(), Step);

        Assert.False(kurt.InUpdraft);
        Assert.True(kurt.VerticalSpeed <= Kurt.Kurt.UpdraftExitSpeed);
    }
}
