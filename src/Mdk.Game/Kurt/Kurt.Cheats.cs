using System.Numerics;
using Mdk.Engine.Platform;

namespace Mdk.Game.Kurt;

/// <summary>Whether Kurt takes damage (the console's god).</summary>
public enum Mortality { Mortal, God }

/// <summary>Whether Kurt collides (the console's noclip turns it off).</summary>
public enum Clipping { On, Off }

/// <summary>The console's cheats: god (no damage, no death by falling out) and noclip (Kurt flies
/// through everything: the walking keys move him, jump and Q rise and sink, turbo is faster).</summary>
public sealed partial class Kurt
{
    private const float NoclipSpeed = 60f;
    private const float NoclipTurbo = 3f;

    public Mortality Mortality = Mortality.Mortal;
    public Clipping Clipping = Clipping.On;

    /// <summary>A noclip step; returns false when Kurt collides as usual.</summary>
    private bool UpdateNoclip(Input input, float delta)
    {
        if (Clipping == Clipping.On)
        {
            return false;
        }

        var turbo = input.IsDown(Key.Turbo);
        UpdateTurning(input, turbo, delta);
        var forward = Axis(input, Key.Forward, Key.Back);
        var strafe = Axis(input, Key.StrafeRight, Key.StrafeLeft);
        var rise = Axis(input, Key.Jump, Key.Down);
        var speed = NoclipSpeed * (turbo ? NoclipTurbo : 1f);
        Feet += (Facing * forward + Right * strafe + Vector3.UnitZ * rise) * speed * delta;
        (ForwardSpeed, StrafeSpeed, VerticalSpeed) = (0f, 0f, 0f);
        OnFloor = false;
        return true;
    }
}
