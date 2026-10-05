using System.Numerics;
using Mdk.Engine.Audio;
using Mdk.Engine.Platform;
using Mdk.Formats;
using Mdk.Game.Audio;
using Mdk.Game.Collision;
using Mdk.Game.Kurt;
using Mdk.Game.Scripts;

namespace Mdk.Game.Tests;

/// <summary>Sniper mode (kurt.gd _update_zoom, _update_clip, select_ammo; sound_mixer.gd
/// _scope_gain; follow_camera.gd _update_sniper_view; godot-mdk docs/gameplay.md "Sniper mode").</summary>
public class SniperTests
{
    private const float Tick = 1f / 30f;
    private const float Step = 1f / 60f;
    private const int Bullets = 0;
    private const int Homing = 1;
    private const int Grenades = 2;
    private const int Mortar = 4;

    private static Inventory WithAmmo(params string[] pickups)
    {
        var inventory = new Inventory();
        foreach (var pickup in pickups)
        {
            var health = Inventory.MaxHealth;
            inventory.Collect(pickup, ref health);
        }

        return inventory;
    }

    /// <summary>Runs the clip for <paramref name="seconds"/>; returns the shots fired.</summary>
    private static int RunClip(Scope scope, Inventory inventory, Scope.Trigger trigger, float seconds, Func<int, bool>? fire = null)
    {
        var shots = 0;
        for (var t = 0f; t < seconds - 1e-4f; t += Step)
        {
            if ((scope.UpdateClip(inventory, trigger, fire ?? (_ => true), Step) & Scope.Clip.Fired) != 0)
            {
                shots++;
            }
        }

        return shots;
    }

    [Fact]
    public void ZoomAcceleratesThenStopsAtTheLimit()
    {
        var scope = new Scope();

        // One tick zooming in: the speed is 0.01, the zoom divided by 1.01.
        scope.UpdateZoom(-1f, Tick);
        Assert.Equal(1f / 1.01f, scope.Zoom, 4);

        for (var i = 0; i < 60; i++)
        {
            scope.UpdateZoom(-1f, Tick);
        }

        Assert.Equal(Scope.ZoomMin, scope.Zoom, 4);
        Assert.Equal(Scope.Zooming.Still, scope.UpdateZoom(-1f, Tick));
        Assert.Equal(Scope.Zooming.Moving, scope.UpdateZoom(1f, Tick));
    }

    [Fact]
    public void ZoomStopsAtALockedTargetsLimit()
    {
        var scope = new Scope { ZoomLimit = Scope.LockLimit(10f, 300f) };
        Assert.Equal(0.0125f, scope.ZoomLimit, 5);
        for (var i = 0; i < 200; i++)
        {
            scope.UpdateZoom(-1f, Tick);
        }

        Assert.Equal(scope.ZoomLimit, scope.Zoom, 5);

        // A near target doesn't let it go past 0.25.
        Assert.Equal(Scope.ZoomMin, Scope.LockLimit(20f, 10f));
    }

    [Fact]
    public void WheelNotchZoomsLikeAHeldKey()
    {
        var wheel = new Scope();
        var keys = new Scope();
        wheel.Wheel(1f);
        for (var i = 0; i < 6; i++)
        {
            wheel.UpdateZoom(0f, Tick);
            keys.UpdateZoom(-1f, Tick);
        }

        Assert.Equal(keys.Zoom, wheel.Zoom, 5);
        Assert.True(wheel.Zoom < 1f);
    }

    [Fact]
    public void LookIsSlowerZoomedInAndPitchIsLimited()
    {
        var wide = new Scope();
        var narrow = new Scope { Zoom = Scope.ZoomMin };
        var turnWide = 0f;
        var turnNarrow = 0f;
        for (var i = 0; i < 30; i++)
        {
            turnWide += wide.Look(1f, 1f, Vector2.Zero, Scope.Turbo.Off, Tick);
            turnNarrow += narrow.Look(1f, 1f, Vector2.Zero, Scope.Turbo.Off, Tick);
        }

        // Turning right lowers the yaw; the zoom scales it.
        Assert.True(turnWide < 0f);
        Assert.Equal(turnWide * Scope.ZoomMin, turnNarrow, 3);
        Assert.True(wide.Pitch > 0f);

        wide.Look(0f, 0f, new Vector2(0f, 1000f), Scope.Turbo.Off, Tick);
        Assert.Equal(Scope.PitchLimit, wide.Pitch);
    }

    [Fact]
    public void ClipLoadsInAQuarterSecondPerRoundAndFiresAQuarterSecondApart()
    {
        var scope = new Scope();
        var inventory = new Inventory();

        // Entering: the timer is set to 3 and three rounds load; ready after 0.75 s.
        Assert.Equal(0, RunClip(scope, inventory, Scope.Trigger.Held, 0.7f));
        Assert.Equal(Scope.ClipSize, scope.ClipRounds);
        Assert.True(scope.ClipTime > 0f);

        // Held fire: a shot every quarter second (the timer reloads the clip meanwhile).
        var shots = RunClip(scope, inventory, Scope.Trigger.Held, 1.05f);
        Assert.Equal(4, shots);
    }

    [Fact]
    public void FullSlotsFireNothing()
    {
        var scope = new Scope();
        var inventory = new Inventory();
        Assert.Equal(0, RunClip(scope, inventory, Scope.Trigger.Held, 2f, _ => false));
        Assert.Equal(Scope.ClipSize, scope.ClipRounds);
    }

    [Fact]
    public void ClipLoadsOnlyTheRoundsInStockAndFallsBackToBullets()
    {
        var scope = new Scope();
        var inventory = WithAmmo("SW_LGREN");
        Assert.Equal(Mortar, inventory.SelectedAmmo);
        var stock = inventory.Ammo[Mortar - 1];

        RunClip(scope, inventory, Scope.Trigger.Released, 1f);
        Assert.Equal(Scope.ClipSize, scope.ClipRounds);
        var mortars = 0;
        RunClip(scope, inventory, Scope.Trigger.Held, 10f, type => type != Mortar || ++mortars > 0);
        Assert.Equal(stock, mortars);
        Assert.Equal(0, inventory.Ammo[Mortar - 1]);

        // Empty, the clip reloads normal bullets.
        Assert.Equal(Bullets, inventory.SelectedAmmo);
    }

    [Fact]
    public void AmmoSelectionSkipsEmptyTypesAndReloads()
    {
        var scope = new Scope { ClipRounds = Scope.ClipSize };
        var inventory = WithAmmo("SW_HOME", "SW_LGREN");
        Assert.Equal(Mortar, inventory.SelectedAmmo);

        scope.SelectAmmo(inventory, 1);
        Assert.Equal(Bullets, inventory.SelectedAmmo);
        Assert.Equal(0, scope.ClipRounds);
        Assert.True(scope.ClipTime > 0f);

        scope.SelectAmmo(inventory, 1);
        Assert.Equal(Homing, inventory.SelectedAmmo);
        scope.SelectAmmo(inventory, 1);
        Assert.Equal(Mortar, inventory.SelectedAmmo);
        scope.SelectAmmo(inventory, -1);
        Assert.Equal(Homing, inventory.SelectedAmmo);
        Assert.NotEqual(Grenades, inventory.NextAmmo(1));
    }

    [Fact]
    public void ScopeHearsTheCrosshairNotBehind()
    {
        // On the line of sight, near: full; at 600 units: 300 / 600 x 1.3.
        Assert.Equal(1f, SoundMixer.ScopeGain(new Vector3(0f, 0f, 50f), 1f));
        Assert.Equal(0.65f, SoundMixer.ScopeGain(new Vector3(0f, 0f, 600f), 1f), 4);
        // Zoomed in, the far sound is louder.
        Assert.Equal(1f, SoundMixer.ScopeGain(new Vector3(0f, 0f, 600f), Scope.ZoomMin));
        // Behind, or far off the line of sight: silent.
        Assert.Equal(0f, SoundMixer.ScopeGain(new Vector3(0f, 0f, -10f), 1f));
        Assert.Equal(0f, SoundMixer.ScopeGain(new Vector3(100f, 0f, 50f), 1f));
    }

    [Fact]
    public void ScopeCentreIsOnTheLineOfSight()
    {
        const float Near = 0.5f;
        const float Far = 1000f;
        const float Aspect = 4f / 3f;
        const float Zoom = 0.5f;

        // A point straight ahead lands on y 279 of the 480-high screen: NDC y = -(279 - 240) / 240.
        var projection = CameraMath.ScopeProjection(Zoom, Aspect, Near, Far);
        var ahead = Vector4.Transform(new Vector4(0f, 0f, -100f, 1f), projection);
        Assert.Equal(-39f / 240f, ahead.Y / ahead.W, 4);
        Assert.Equal(0f, ahead.X / ahead.W, 4);

        // A point 1 unit up at depth 384 / zoom shows 1 pixel higher.
        var depth = Scope.Focal / Zoom;
        var up = Vector4.Transform(new Vector4(0f, 1f, -depth, 1f), projection);
        Assert.Equal((-39f + 1f) / 240f, up.Y / up.W, 4);

        // The same on the 640x480 screen (the target lock's test).
        var eye = new Vector3(0f, 0f, 10f);
        var screen = Scope.ToScreen(eye, Vector3.UnitY, Zoom, eye + new Vector3(1f, depth, 1f));
        Assert.Equal(Scope.Centre + new Vector2(1f, -1f), screen!.Value);
        Assert.Null(Scope.ToScreen(eye, Vector3.UnitY, Zoom, eye - Vector3.UnitY));
        Assert.True(Scope.Locks(Scope.Centre + new Vector2(30f, 30f), Scope.Centre + new Vector2(40f, 40f)));
        Assert.False(Scope.Locks(Scope.Centre + new Vector2(33f, 0f), Scope.Centre + new Vector2(40f, 1f)));
    }

    [Fact]
    public void EyeMovesForwardLookingDown()
    {
        var scope = new Scope();
        Assert.Equal(new Vector3(0f, 0f, 4f), scope.Eye(Vector3.Zero, Vector3.UnitX));
        scope.Pitch = 60f;
        Assert.Equal(2.5f, scope.Eye(Vector3.Zero, Vector3.UnitX).X, 4);
        Assert.Equal(-MathF.Sin(float.DegreesToRadians(60f)), scope.Forward(0f).Z, 4);
    }

    [Fact]
    public void StrikePathGoesThroughItsPoints()
    {
        Vector3[] points = [new(0f, 0f, 100f), new(50f, 0f, 60f), new(100f, 0f, 40f), new(150f, 0f, 60f), new(200f, 0f, 100f)];
        var path = new BezierPath(points);
        Assert.True(path.Length > 200f);
        Assert.Equal(points[0], path.Sample(0f));
        Assert.Equal(points[^1], path.Sample(path.Length));
        var over = path.ClosestOffset(points[2]);
        Assert.Equal(points[2].X, path.Sample(over).X, 1);
        Assert.Equal(points[2].Z, path.Sample(over).Z, 1);
    }

    [Fact]
    public void KurtSnipesOnlyOnTheFloorAndAKnockDownEndsIt()
    {
        var kurt = StandingKurt();
        var input = new Input();
        input.Hold(Key.Sniper, Input.State.Down);
        kurt.Update(input, Step);
        Assert.True(kurt.Sniping);

        // He only sidesteps.
        input.Hold(Key.Sniper, Input.State.Up);
        input.Hold(Key.Forward, Input.State.Down);
        var feet = kurt.Feet;
        for (var i = 0; i < 30; i++)
        {
            kurt.Update(input, Step);
        }

        Assert.Equal(feet.X, kurt.Feet.X, 3);
        Assert.Equal(feet.Y, kurt.Feet.Y, 3);
        Assert.True(kurt.Scope.Pitch < 0f);

        kurt.KnockDown(Vector2.Zero);
        Assert.False(kurt.Sniping);
    }

    [Fact]
    public void SniperKeyLeavesSniperMode()
    {
        var kurt = StandingKurt();
        kurt.EnterSniper();
        Assert.True(kurt.Sniping);
        var input = new Input();
        input.Hold(Key.Sniper, Input.State.Down);
        kurt.Update(input, Step);
        Assert.False(kurt.Sniping);
    }

    private static readonly MdkData Data = MdkData.Find() ?? throw new InvalidOperationException("MDK data not found");
    private static readonly AudioDevice Device = new(Output.Muted);
    /// <summary>Level 3's start pad (the Godot port's Kurt stands at z 192).</summary>
    private static readonly Vector3 Pad = new(-4f, 0f, 195f);
    private const int Level = 3;

    private static Kurt.Kurt StandingKurt()
    {
        var dti = Dti.Load(Data.PathOf($"TRAVERSE/LEVEL{Level}/LEVEL{Level}.DTI"));
        var mto = Mto.Load(Data.PathOf($"TRAVERSE/LEVEL{Level}/LEVEL{Level}O.MTO"));
        var space = new ArenaSpace();
        space.Add(mto.GetArena(dti.Arenas[dti.StartArena].Name));
        var kurt = new Kurt.Kurt(space, new SoundMixer(Device, _ => null), _ => 5) { Feet = Pad };
        for (var t = 0f; t < 1f; t += Step)
        {
            kurt.Update(new Input(), Step);
        }

        Assert.True(kurt.OnFloor);
        return kurt;
    }
}
