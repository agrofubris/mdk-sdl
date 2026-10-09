using Mdk.Engine.Platform;
using SDL;

namespace Mdk.Game.Tests;

/// <summary>Default bindings and rebinding (project.godot's input map, settings.gd bind).</summary>
public class InputTests
{
    private const int WheelAway = 1;
    private const int WheelToward = -1;
    /// <summary>An SDL mouse button with no name of its own.</summary>
    private const int ExtraButton = 8;

    private static Input NewInput()
    {
        var input = new Input();
        input.BeginFrame();
        return input;
    }

    [Fact]
    public void WheelSelectsItemsUntilTheStepUsesIt()
    {
        var input = NewInput();
        input.AddWheel(WheelToward);
        Assert.True(input.IsDown(Key.ItemNext));
        Assert.Equal("Wheel down", input.LastControl);

        input.ClearMouse();
        Assert.False(input.IsDown(Key.ItemNext));

        input.AddWheel(WheelAway);
        Assert.True(input.IsDown(Key.ItemPrevious));
    }

    [Fact]
    public void EUsesTheItemAndFliesUp()
    {
        var input = NewInput();
        input.SetKey(SDL_Scancode.SDL_SCANCODE_E, Input.State.Down, Repeat.No);
        Assert.True(input.IsDown(Key.UseItem));
        Assert.True(input.IsDown(Key.Up));

        // Rebinding "use" leaves E flying up.
        input.SetKey(SDL_Scancode.SDL_SCANCODE_E, Input.State.Up, Repeat.No);
        Assert.True(input.Bind(Key.UseItem, "F"));
        input.SetKey(SDL_Scancode.SDL_SCANCODE_E, Input.State.Down, Repeat.No);
        Assert.False(input.IsDown(Key.UseItem));
        Assert.True(input.IsDown(Key.Up));
    }

    [Fact]
    public void AnyMouseButtonBinds()
    {
        var input = NewInput();
        input.SetButton((MouseButton)ExtraButton, Input.State.Down);
        Assert.Equal($"Mouse {ExtraButton}", input.LastControl);

        Assert.True(input.Bind(Key.Jump, input.LastControl));
        input.SetButton((MouseButton)ExtraButton, Input.State.Up);
        input.SetButton((MouseButton)ExtraButton, Input.State.Down);
        Assert.True(input.IsDown(Key.Jump));
        Assert.Equal($"Mouse {ExtraButton}", input.Describe(Key.Jump));
    }

    [Theory]
    [InlineData("Mouse 4", nameof(MouseButton.Back))]
    [InlineData("Mouse 5", nameof(MouseButton.Forward))]
    [InlineData("Middle mouse", nameof(MouseButton.Middle))]
    public void SavedButtonNamesStillBind(string name, string buttonName)
    {
        var button = Enum.Parse<MouseButton>(buttonName);
        var input = NewInput();
        Assert.True(input.Bind(Key.Jump, name));
        input.SetButton(button, Input.State.Down);
        Assert.True(input.IsDown(Key.Jump));
        Assert.Equal(name, input.Describe(Key.Jump));
    }

    [Fact]
    public void WheelBinds()
    {
        var input = NewInput();
        Assert.True(input.Bind(Key.Jump, "Wheel up"));
        input.AddWheel(WheelAway);
        Assert.True(input.IsDown(Key.Jump));
        Assert.False(input.IsDown(Key.ItemPrevious));
    }

    [Fact]
    public void PadButtonsPlayAndWorkTheMenus()
    {
        var input = NewInput();
        input.SetPadButton(PadButton.South, Input.State.Down);
        Assert.True(input.IsDown(Key.Jump));
        Assert.True(input.WasPressed(MenuKey.Accept));
        Assert.Equal("A", input.LastPadControl);
        Assert.Equal("", input.LastControl);

        input.SetPadButton(PadButton.South, Input.State.Up);
        Assert.False(input.IsDown(Key.Jump));
    }

    [Fact]
    public void PadButtonBinds()
    {
        var input = NewInput();
        Assert.True(input.BindPad(Key.Jump, "RB"));
        Assert.Equal("RB", input.DescribePad(Key.Jump));

        input.SetPadButton(PadButton.RightShoulder, Input.State.Down);
        Assert.True(input.IsDown(Key.Jump));
        Assert.False(input.IsDown(Key.ItemNext));

        // A is free now; back to the defaults, it jumps again.
        input.SetPadButton(PadButton.RightShoulder, Input.State.Up);
        input.SetPadButton(PadButton.South, Input.State.Down);
        Assert.False(input.IsDown(Key.Jump));
        input.ResetPadBindings();
        Assert.Equal("A", input.DescribePad(Key.Jump));
    }

    [Theory]
    [InlineData("Start")]
    [InlineData("Keypad")]
    public void StartAndUnknownNamesDontBind(string name)
    {
        // Start pauses and goes back in the menus, whatever the bindings.
        var input = NewInput();
        Assert.False(input.BindPad(Key.Jump, name));
        Assert.Equal("A", input.DescribePad(Key.Jump));
    }

    [Fact]
    public void PadLookScalesAndInverts()
    {
        var input = NewInput();
        input.AddPadLook(new System.Numerics.Vector2(0.15f, 0.15f));
        Assert.Equal(1f, input.MouseX, 3);
        Assert.Equal(1f, input.MouseY, 3);

        input.ClearMouse();
        input.PadSensitivity = 2f;
        input.InvertPad = true;
        input.AddPadLook(new System.Numerics.Vector2(0.15f, 0.15f));
        Assert.Equal(2f, input.MouseX, 3);
        Assert.Equal(-2f, input.MouseY, 3);
    }
}
