using Mdk.Engine.Platform;
using Mdk.Game.Collision;

namespace Mdk.Game.Kurt;

/// <summary>The 1996 demo's moves (kurt.gd <c>beta_moves</c>, godot-mdk docs/beta96.md "Kurt"), drawn
/// by its <c>damp_animate</c> (0x36cf4): states 701 and 702 roll Kurt to his left or right, 703 puts
/// the sniper helmet on and 900 takes it off; backing up shows <c>K_BCKUP</c>.
/// <code>
///   Z / C ──► roll: K_ROLLL / K_ROLLR once, sideways, turning 90° the other way ──► still
///   sniper key ──► helmet on (K_HELM) ──► sniper mode ──► sniper key ──► helmet off (backwards)
/// </code></summary>
public sealed partial class Kurt
{
    /// <summary>A roll moves Kurt sideways at 8 x 0.05 units per tick and turns him 90° the other way
    /// over the animation: a quarter circle around what he faces.</summary>
    private const float RollSpeed = 8f * 0.05f * Ticks;
    private const float RollTurn = 90f;

    /// <summary>The demo's moves are on (its levels).</summary>
    public bool BetaMoves { get; init; }

    /// <summary>Walking backwards in the demo's levels: its own frames (<c>K_BCKUP</c>, a file its
    /// executable never loads; the port's guess), played forwards.</summary>
    public bool BackingUp => BetaMoves && Current == State.Run && ForwardSpeed < 0f;

    private bool InBetaMove => Current is State.RollLeft or State.RollRight or State.HelmetOn or State.HelmetOff;

    /// <summary>Z or C on the floor starts a roll (nothing in the demo asks for one: the keys are the port's).</summary>
    private bool StartRoll(Input input)
    {
        if (!BetaMoves || !OnFloor || Health == 0 || Knocked || Current == State.Throw || Walking != Walk.Normal)
        {
            return false;
        }

        var left = input.IsDown(Key.RollLeft);
        var roll = left ? State.RollLeft : State.RollRight;
        if ((!left && !input.IsDown(Key.RollRight)) || frameCount(roll) == 0)
        {
            return false;
        }

        StopFiring();
        ForwardSpeed = 0f;
        StrafeSpeed = 0f;
        mixer.Play("ROLL");
        SetState(roll);
        return true;
    }

    /// <summary>The helmet goes on before sniper mode starts (state 703).</summary>
    private void PutHelmetOn()
    {
        StopFiring();
        ForwardSpeed = 0f;
        StrafeSpeed = 0f;
        TurnRate = 0f;
        SetState(State.HelmetOn);
    }

    /// <summary>A step of a move that plays its animation through, a frame per tick: a roll moves Kurt
    /// sideways while he turns the other way; the helmet goes on (then sniper mode starts) or off.</summary>
    private void UpdateBetaMove(float delta)
    {
        var frames = Math.Max(frameCount(Current), 1);
        AnimationFrame += Ticks * delta;
        StateTime += delta;
        if (Current is State.RollLeft or State.RollRight)
        {
            var side = Current == State.RollLeft ? -1f : 1f;
            Yaw += side * RollTurn / frames * Ticks * delta;
            var move = Right * (side * RollSpeed * delta);
            Feet = Contact(space.Move(Feet, move, ArenaSpace.Motion.Walk, WalkSlide, Nearby(Feet + move))).Feet;
        }

        VerticalSpeed = MathF.Max(VerticalSpeed - Gravity * delta, -MaxFallSpeed);
        Fall(delta);
        UpdateTouched();
        if (AnimationFrame < frames - 1)
        {
            return;
        }

        var helmetOn = Current == State.HelmetOn;
        SetState(State.Still);
        if (helmetOn)
        {
            EnterSniper();
        }
    }
}
