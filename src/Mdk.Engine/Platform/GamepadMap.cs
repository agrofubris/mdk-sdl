using System.Numerics;

namespace Mdk.Engine.Platform;

/// <summary>A gamepad's buttons by place (SDL's names: South is Xbox's A, PlayStation's cross); the
/// triggers count as buttons once pulled half way.</summary>
public enum PadButton
{
    South, East, West, North, Back, Start, LeftStick, RightStick, LeftShoulder, RightShoulder,
    DpadUp, DpadDown, DpadLeft, DpadRight, LeftTrigger, RightTrigger,
}

/// <summary>The gamepad's fixed layout: the left stick walks, the right one looks.
/// <code>
///   LT sniper   LB item ◄            RB item ►   RT fire
///   d-pad: ▲ zoom in, ▼ zoom out, ◄ ► items      Y item ►
///   left stick: walk                         X use   B turbo
///   Start: pause                                 A jump
/// </code>
/// In the menus: the d-pad or the left stick, A accepts, B and Start go back.</summary>
public static class GamepadMap
{
    /// <summary>A stick past this (of its reach) holds a direction.</summary>
    private const float Push = 0.5f;
    /// <summary>The right stick's dead zone and its curve's power (finer aim near the centre).</summary>
    private const float DeadZone = 0.15f;
    private const float Curve = 2f;

    /// <summary>The right stick's look at full tilt (degrees a second: right, down).</summary>
    public static readonly Vector2 LookRate = new(180f, 120f);

    /// <summary>The key a button holds in play, or null.</summary>
    public static Key? Game(PadButton button) => button switch
    {
        PadButton.RightTrigger => Key.Fire,
        PadButton.LeftTrigger => Key.Sniper,
        PadButton.South => Key.Jump,
        PadButton.East => Key.Turbo,
        PadButton.West => Key.UseItem,
        PadButton.North or PadButton.RightShoulder or PadButton.DpadRight => Key.ItemNext,
        PadButton.LeftShoulder or PadButton.DpadLeft => Key.ItemPrevious,
        PadButton.DpadUp => Key.ZoomIn,
        PadButton.DpadDown => Key.ZoomOut,
        PadButton.Start => Key.Escape,
        _ => null,
    };

    /// <summary>The menu key a button presses, or null.</summary>
    public static MenuKey? Menu(PadButton button) => button switch
    {
        PadButton.South => MenuKey.Accept,
        PadButton.East or PadButton.Start => MenuKey.Back,
        PadButton.DpadUp => MenuKey.Up,
        PadButton.DpadDown => MenuKey.Down,
        PadButton.DpadLeft => MenuKey.Left,
        PadButton.DpadRight => MenuKey.Right,
        _ => null,
    };

    /// <summary>The left stick holds <paramref name="key"/> (walking; its y is down, SDL's).</summary>
    public static bool Walks(Vector2 stick, Key key) => key switch
    {
        Key.Forward => stick.Y < -Push,
        Key.Back => stick.Y > Push,
        Key.StrafeLeft => stick.X < -Push,
        Key.StrafeRight => stick.X > Push,
        _ => false,
    };

    /// <summary>The left stick holds the menus' <paramref name="key"/>.</summary>
    public static bool Steers(Vector2 stick, MenuKey key) => key switch
    {
        MenuKey.Up => stick.Y < -Push,
        MenuKey.Down => stick.Y > Push,
        MenuKey.Left => stick.X < -Push,
        MenuKey.Right => stick.X > Push,
        _ => false,
    };

    /// <summary>The right stick's look over <paramref name="seconds"/> (degrees: right, down).</summary>
    public static Vector2 Look(Vector2 stick, float seconds)
    {
        var tilt = MathF.Min(stick.Length(), 1f);
        if (tilt < DeadZone)
        {
            return Vector2.Zero;
        }

        // Past the dead zone, from 0 to 1 along a curve.
        var strength = MathF.Pow((tilt - DeadZone) / (1f - DeadZone), Curve);
        return Vector2.Normalize(stick) * strength * LookRate * seconds;
    }
}
