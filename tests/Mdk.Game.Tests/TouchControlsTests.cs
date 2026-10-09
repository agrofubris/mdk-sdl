using System.Numerics;
using Mdk.Engine.Platform;

namespace Mdk.Game.Tests;

/// <summary>The on-screen controls of a touch screen (Android): the stick, the buttons, looking.</summary>
public class TouchControlsTests
{
    /// <summary>A phone held sideways (window pixels).</summary>
    private static readonly Vector2 Screen = new(2400f, 1080f);
    private const ulong First = 1;
    private const ulong Second = 2;

    private static (TouchControls Touch, Input Input) New()
    {
        var input = new Input();
        input.BeginFrame();
        return (new TouchControls(), input);
    }

    private static Vector2 CentreOf(TouchControls touch, Key key)
    {
        var area = touch.Layout(Screen).First(b => b.Key == key).Area;
        return new Vector2(area.X + area.Width / 2f, area.Y + area.Height / 2f);
    }

    [Theory]
    [InlineData(Key.Fire)]
    [InlineData(Key.Jump)]
    [InlineData(Key.Sniper)]
    public void AButtonHoldsItsKeyWhileTouched(Key key)
    {
        var (touch, input) = New();

        touch.Down(First, CentreOf(touch, key), Screen, input);
        Assert.True(input.IsDown(key));
        Assert.True(input.WasPressed(key));

        touch.Up(First, input);
        Assert.False(input.IsDown(key));
    }

    [Fact]
    public void PauseOpensThePauseMenu()
    {
        var (touch, input) = New();
        touch.Down(First, CentreOf(touch, Key.Escape), Screen, input);
        Assert.True(input.WasPressed(MenuKey.Back));
    }

    [Fact]
    public void TheStickWalksWhereItIsPushed()
    {
        var (touch, input) = New();
        var origin = new Vector2(400f, 700f);
        var radius = touch.StickRadius(Screen);

        touch.Down(First, origin, Screen, input);
        Assert.False(input.IsDown(Key.Forward));

        touch.Move(First, origin - new Vector2(0f, radius), Screen, input);
        Assert.True(input.IsDown(Key.Forward));
        Assert.False(input.IsDown(Key.StrafeLeft));

        touch.Move(First, origin + new Vector2(-radius, radius), Screen, input);
        Assert.False(input.IsDown(Key.Forward));
        Assert.True(input.IsDown(Key.Back));
        Assert.True(input.IsDown(Key.StrafeLeft));

        touch.Up(First, input);
        Assert.False(input.IsDown(Key.Back));
        Assert.False(input.IsDown(Key.StrafeLeft));
    }

    [Fact]
    public void TestHoldsDontReleaseTouches()
    {
        var (touch, input) = New();
        var origin = new Vector2(400f, 700f);

        // The viewer releases its test keys every frame.
        touch.Down(First, origin, Screen, input);
        touch.Move(First, origin - new Vector2(0f, touch.StickRadius(Screen)), Screen, input);
        touch.Down(Second, CentreOf(touch, Key.Fire), Screen, input);
        input.Hold(Key.Forward, Input.State.Up);
        input.Hold(Key.Fire, Input.State.Up);

        Assert.True(input.IsDown(Key.Forward));
        Assert.True(input.IsDown(Key.Fire));
    }

    [Fact]
    public void DraggingTheRightSideLooks()
    {
        var (touch, input) = New();
        var at = new Vector2(1500f, 300f);

        touch.Down(First, at, Screen, input);
        touch.Move(First, at + new Vector2(20f, -10f), Screen, input);
        Assert.True(input.MouseX > 0f);
        Assert.True(input.MouseY < 0f);
    }

    [Fact]
    public void FingersAreIndependent()
    {
        var (touch, input) = New();
        var origin = new Vector2(400f, 700f);

        touch.Down(First, origin, Screen, input);
        touch.Move(First, origin - new Vector2(0f, touch.StickRadius(Screen)), Screen, input);
        touch.Down(Second, CentreOf(touch, Key.Fire), Screen, input);
        touch.Up(Second, input);

        Assert.True(input.IsDown(Key.Forward));
        Assert.False(input.IsDown(Key.Fire));
    }

    [Fact]
    public void ReleaseLetsEverythingGo()
    {
        var (touch, input) = New();
        touch.Down(First, CentreOf(touch, Key.Fire), Screen, input);
        touch.Down(Second, new Vector2(400f, 700f), Screen, input);
        touch.Move(Second, new Vector2(400f, 400f), Screen, input);

        touch.Release(input);
        Assert.False(input.IsDown(Key.Fire));
        Assert.False(input.IsDown(Key.Forward));
        Assert.Null(touch.Stick);
    }

    [Theory]
    [InlineData(TouchButtons.Auto, InputSource.Touch, true)]
    [InlineData(TouchButtons.Auto, InputSource.Device, false)]
    [InlineData(TouchButtons.Off, InputSource.Touch, false)]
    [InlineData(TouchButtons.On, InputSource.Device, true)]
    public void ShownByTheSettingAndTheLastInput(TouchButtons mode, InputSource last, bool shown)
    {
        // Auto: a gamepad, a keyboard or a mouse hides them; a touch shows them again.
        Assert.Equal(shown, TouchControls.Shown(mode, last));
    }
}
