using System.Numerics;
using Mdk.Engine.Audio;
using Mdk.Engine.Platform;
using Mdk.Formats;
using Mdk.Game.Audio;
using Mdk.Game.Collision;
using Mdk.Game.Menu;
using State = Mdk.Game.Kurt.Kurt.State;

namespace Mdk.Game.Tests;

/// <summary>The 1996 demo's moves (Kurt.Beta.cs), its triangles that only stop Kurt, its ambient
/// loops, on a synthetic floor; and the menus' ARROW cursor.</summary>
public class BetaTests
{
    private const float Step = 1f / 60f;
    /// <summary>Frames of every animation: a move lasts 9 ticks (it ends on its last frame).</summary>
    private const int Frames = 10;
    private const float StartYaw = 90f;
    /// <summary>The turn of a roll over 9 of its 10 frames.</summary>
    private const float RollTurn = 90f * (Frames - 1) / Frames;
    private const float Tolerance = 1f;
    /// <summary>The demo's flag of triangles that only stop Kurt.</summary>
    private const uint Clip = BetaDemo.NotDrawn;

    private static readonly AudioDevice Device = new(Output.Muted);

    /// <summary>A square floor at z 0 (one node holding two up-facing triangles), the second one
    /// flagged <see cref="Clip"/>; a vertex high above makes the arena's box tall enough for Kurt.</summary>
    private static Arena Floor() => new()
    {
        Name = "HMO_1",
        Materials = ["WALL"],
        Vertices = [new(-100f, -100f, 0f), new(100f, -100f, 0f), new(100f, 100f, 0f), new(-100f, 100f, 0f), new(0f, 0f, 50f)],
        TriangleIndices = [0, 1, 2, 0, 2, 3],
        TriangleMaterials = [0, 0],
        TriangleUvs = new Vector2[6],
        TriangleFlags = [0, Clip],
        Nodes = [new BspNode(Vector3.UnitZ, 0f, Bsp.None, Bsp.None, 0, 2, 0, 0)],
        ClipFlag = Clip,
    };

    /// <summary>Kurt standing on the floor, facing +Y.</summary>
    private static Kurt.Kurt Standing(bool betaMoves)
    {
        var space = new ArenaSpace();
        space.Add(Floor());
        var kurt = new Kurt.Kurt(space, new SoundMixer(Device, _ => null), _ => Frames)
        {
            Feet = new Vector3(0f, 0f, 1f),
            Yaw = StartYaw,
            BetaMoves = betaMoves,
        };
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

    /// <summary>Presses a key for one step.</summary>
    private static void Press(Kurt.Kurt kurt, Input input, Key key)
    {
        input.Hold(key, Input.State.Down);
        kurt.Update(input, Step);
        input.Hold(key, Input.State.Up);
    }

    [Fact]
    public void RollLeftMovesLeftAndTurnsRight()
    {
        var kurt = Standing(betaMoves: true);
        var input = new Input();
        Press(kurt, input, Key.RollLeft);
        Assert.Equal(State.RollLeft, kurt.Current);
        Run(kurt, input, 0.5f);
        Assert.Equal(State.Still, kurt.Current);
        Assert.InRange(kurt.Yaw, StartYaw - RollTurn - Tolerance, StartYaw - RollTurn + Tolerance);
        Assert.True(kurt.Feet.X < -1f, $"x {kurt.Feet.X}");
        Assert.True(kurt.OnFloor);
    }

    [Fact]
    public void RollRightTurnsLeft()
    {
        var kurt = Standing(betaMoves: true);
        Press(kurt, new Input(), Key.RollRight);
        Run(kurt, new Input(), 0.5f);
        Assert.InRange(kurt.Yaw, StartYaw + RollTurn - Tolerance, StartYaw + RollTurn + Tolerance);
        Assert.True(kurt.Feet.X > 1f, $"x {kurt.Feet.X}");
    }

    [Fact]
    public void RetailKurtDoesntRoll()
    {
        var kurt = Standing(betaMoves: false);
        Press(kurt, new Input(), Key.RollLeft);
        Assert.Equal(State.Still, kurt.Current);
    }

    [Fact]
    public void TheHelmetGoesOnBeforeSniperModeAndComesOffAfter()
    {
        var kurt = Standing(betaMoves: true);
        var input = new Input();
        Press(kurt, input, Key.Sniper);
        Assert.Equal(State.HelmetOn, kurt.Current);
        Assert.False(kurt.Sniping);
        Run(kurt, input, 0.5f);
        Assert.True(kurt.Sniping);

        Press(kurt, input, Key.Sniper);
        Assert.False(kurt.Sniping);
        Assert.Equal(State.HelmetOff, kurt.Current);
        Run(kurt, input, 0.5f);
        Assert.Equal(State.Still, kurt.Current);
    }

    [Fact]
    public void BackingUpPlaysItsFramesForwards()
    {
        var kurt = Standing(betaMoves: true);
        var input = new Input();
        input.Hold(Key.Back, Input.State.Down);
        Run(kurt, input, 0.5f);
        var frame = kurt.AnimationFrame;
        Run(kurt, input, 0.1f);
        Assert.True(kurt.BackingUp);
        Assert.True(kurt.AnimationFrame > frame);
    }

    [Fact]
    public void ScriptRaysPassTheTrianglesThatOnlyStopKurt()
    {
        var bsp = new Bsp(Floor());
        // Triangle 1 (flagged) is the floor's half where y > x.
        var above = new Vector3(-50f, 50f, 10f);
        var below = above with { Z = -10f };
        Assert.Equal(1, bsp.Segment(above, below, Bsp.SegmentMode.Any, out _));
        Assert.Equal(Bsp.None, bsp.Segment(above, below, Bsp.SegmentMode.Any, out _, Bsp.Clip.PassesThrough));
        Assert.Equal(0, bsp.Segment(above with { X = 50f, Y = -50f }, below with { X = 50f, Y = -50f }, Bsp.SegmentMode.Any, out _, Bsp.Clip.PassesThrough));
    }

    [Fact]
    public void ArenasPlayTheirAmbientLoop()
    {
        var device = new AudioDevice(Output.Muted);
        using var _ = device;
        var entry = new SoundMixer.Entry(new Sound(new float[1], 1, 1, Looping.Forever), 0);
        var mixer = new SoundMixer(device, _ => entry);
        var music = new LevelMusic(mixer, new Dictionary<string, string>())
        {
            Ambience = new Dictionary<string, string> { ["ARENA_1"] = "AMB1", ["ARENA_2"] = "AMB2" },
        };
        music.Enter("ARENA_1");
        Assert.True(mixer.IsPlaying("AMB1"));
        music.Enter("ARENA_2");
        Assert.False(mixer.IsPlaying("AMB1"));
        Assert.True(mixer.IsPlaying("AMB2"));
        music.Enter("CORR_1");
        Assert.False(mixer.IsPlaying("AMB2"));
    }

    [DataFact]
    public void TheCursorIsTheOriginalsArrowScaledByWholeSteps()
    {
        var fti = Fti.Load(MdkData.Find()!.PathOf("MISC/MDKFONT.FTI"));
        var one = MenuCursor.Build(fti, 1)!;
        var two = MenuCursor.Build(fti, 2)!;
        Assert.Equal((one.Width * 2, one.Height * 2), (two.Width, two.Height));
        Assert.Equal(two.Width * two.Height * 4, two.Rgba.Length);
        Assert.InRange(two.HotX, 0, two.Width - 1);

        // Opaque where the sprite is drawn, see-through elsewhere.
        var alphas = two.Rgba.Where((_, i) => i % 4 == 3).ToList();
        Assert.Contains((byte)255, alphas);
        Assert.Contains((byte)0, alphas);
    }
}
