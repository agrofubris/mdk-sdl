using System.Numerics;
using Mdk.Engine.Platform;

namespace Mdk.Game.Tests;

/// <summary>The gamepad's layout (<see cref="GamepadMap"/>): its buttons in play and in the menus,
/// the left stick walking, the right one looking.</summary>
public class GamepadMapTests
{
    private const float Tolerance = 1e-4f;
    private static readonly Key[] WalkKeys = [Key.Forward, Key.Back, Key.StrafeLeft, Key.StrafeRight];

    private static Key[] Walking(Vector2 stick) => [.. WalkKeys.Where(k => GamepadMap.Walks(stick, k))];

    private static MenuKey[] Steering(Vector2 stick) =>
        [.. Enum.GetValues<MenuKey>().Where(k => GamepadMap.Steers(stick, k))];

    [Theory]
    [InlineData(PadButton.RightTrigger, Key.Fire)]
    [InlineData(PadButton.LeftTrigger, Key.Sniper)]
    [InlineData(PadButton.South, Key.Jump)]
    [InlineData(PadButton.East, Key.Turbo)]
    [InlineData(PadButton.West, Key.UseItem)]
    [InlineData(PadButton.RightShoulder, Key.ItemNext)]
    [InlineData(PadButton.LeftShoulder, Key.ItemPrevious)]
    [InlineData(PadButton.DpadUp, Key.ZoomIn)]
    [InlineData(PadButton.DpadDown, Key.ZoomOut)]
    [InlineData(PadButton.Start, Key.Escape)]
    public void ButtonsPlay(PadButton button, Key key)
    {
        Assert.Equal(key, GamepadMap.Game(button));
    }

    [Theory]
    [InlineData(PadButton.South, MenuKey.Accept)]
    [InlineData(PadButton.East, MenuKey.Back)]
    [InlineData(PadButton.Start, MenuKey.Back)]
    [InlineData(PadButton.DpadUp, MenuKey.Up)]
    [InlineData(PadButton.DpadDown, MenuKey.Down)]
    [InlineData(PadButton.DpadLeft, MenuKey.Left)]
    [InlineData(PadButton.DpadRight, MenuKey.Right)]
    public void ButtonsWorkTheMenus(PadButton button, MenuKey key)
    {
        Assert.Equal(key, GamepadMap.Menu(button));
    }

    [Fact]
    public void TheLeftStickWalks()
    {
        // Up the stick (SDL's y down) walks forward; a slight push holds nothing.
        Assert.Equal([Key.Forward], Walking(new Vector2(0f, -0.9f)));
        Assert.Equal([Key.Back, Key.StrafeRight], Walking(new Vector2(0.8f, 0.8f)));
        Assert.Empty(Walking(new Vector2(0.2f, -0.2f)));
    }

    [Fact]
    public void TheLeftStickSteersTheMenus()
    {
        Assert.Equal([MenuKey.Up], Steering(new Vector2(0f, -0.9f)));
        Assert.Equal([MenuKey.Left], Steering(new Vector2(-0.9f, 0.1f)));
        Assert.Empty(Steering(new Vector2(0.2f, 0.2f)));
    }

    [Fact]
    public void TheRightStickLooks()
    {
        // Full right turns at the full rate; inside the dead zone nothing; half way is slower than half
        // (a curve, for aiming).
        var full = GamepadMap.Look(new Vector2(1f, 0f), 1f);
        Assert.Equal(GamepadMap.LookRate.X, full.X, Tolerance);
        Assert.Equal(0f, full.Y, Tolerance);

        Assert.Equal(Vector2.Zero, GamepadMap.Look(new Vector2(0.1f, -0.1f), 1f));

        var half = GamepadMap.Look(new Vector2(0.5f, 0f), 1f);
        Assert.InRange(half.X, 0.01f, GamepadMap.LookRate.X / 2f);

        // Up the stick looks up (the look's Y is down, as the mouse's).
        Assert.True(GamepadMap.Look(new Vector2(0f, -1f), 1f).Y < 0f);
    }

    [Fact]
    public void ButtonNamesRoundTrip()
    {
        foreach (var button in Enum.GetValues<PadButton>())
        {
            Assert.Equal(button, GamepadMap.Parse(GamepadMap.Name(button)));
        }

        Assert.Null(GamepadMap.Parse("W"));
    }
}
