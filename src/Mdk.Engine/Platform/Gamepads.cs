using System.Numerics;
using SDL;
using static SDL.SDL3;

namespace Mdk.Engine.Platform;

/// <summary>The gamepads plugged in (SDL's gamepad API: Xbox, PlayStation, Switch and Android's
/// Bluetooth pads by the same buttons), driving <see cref="Input"/> through <see cref="GamepadMap"/>.</summary>
internal sealed unsafe class Gamepads : IDisposable
{
    /// <summary>An axis' reach (SDL's sticks and triggers run to ±32767).</summary>
    private const float AxisReach = short.MaxValue;
    /// <summary>A trigger pulled past this (of its reach) is pressed.</summary>
    private const float TriggerPull = 0.5f;
    private const float NanosecondsPerSecond = 1e9f;
    /// <summary>A longer gap between frames (a pause, a load) looks no farther.</summary>
    private const float MaxStep = 0.1f;

    private static readonly Key[] WalkKeys = [Key.Forward, Key.Back, Key.StrafeLeft, Key.StrafeRight];
    private static readonly MenuKey[] SteerKeys = [MenuKey.Up, MenuKey.Down, MenuKey.Left, MenuKey.Right];

    private readonly Dictionary<SDL_JoystickID, IntPtr> _open = [];
    private Vector2 _left;
    private Vector2 _right;
    private ulong _lastTicks;

    /// <summary>Handles a gamepad event; false for the others.</summary>
    public bool Event(in SDL_Event e, Input input)
    {
        switch ((SDL_EventType)e.type)
        {
            case SDL_EventType.SDL_EVENT_GAMEPAD_ADDED:
                Open(e.gdevice.which);
                return true;
            case SDL_EventType.SDL_EVENT_GAMEPAD_REMOVED:
                Close(e.gdevice.which, input);
                return true;
            case SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_DOWN:
            case SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_UP:
                if (Button((SDL_GamepadButton)e.gbutton.button) is { } button)
                {
                    input.SetPadButton(button, e.gbutton.down ? Input.State.Down : Input.State.Up);
                }

                return true;
            case SDL_EventType.SDL_EVENT_GAMEPAD_AXIS_MOTION:
                Axis((SDL_GamepadAxis)e.gaxis.axis, e.gaxis.value / AxisReach, input);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Once a frame, after the events: the left stick walks or steers the menus, the right
    /// one looks (by the time since the last frame).</summary>
    public void Update(Input input)
    {
        var now = SDL_GetTicksNS();
        var seconds = _lastTicks == 0 ? 0f : MathF.Min((now - _lastTicks) / NanosecondsPerSecond, MaxStep);
        _lastTicks = now;
        if (_open.Count == 0)
        {
            return;
        }

        foreach (var key in WalkKeys)
        {
            input.SetPad(key, GamepadMap.Walks(_left, key) ? Input.State.Down : Input.State.Up);
        }

        foreach (var key in SteerKeys)
        {
            input.SetPadMenu(key, GamepadMap.Steers(_left, key) ? Input.State.Down : Input.State.Up);
        }

        input.AddPadLook(GamepadMap.Look(_right, seconds));
    }

    private void Open(SDL_JoystickID id)
    {
        if (_open.ContainsKey(id))
        {
            return;
        }

        var pad = SDL_OpenGamepad(id);
        if (pad != null)
        {
            _open[id] = (IntPtr)pad;
        }
    }

    /// <summary>A gamepad unplugged: it lets go of everything it held.</summary>
    private void Close(SDL_JoystickID id, Input input)
    {
        if (!_open.Remove(id, out var pad))
        {
            return;
        }

        SDL_CloseGamepad((SDL_Gamepad*)pad);
        _left = _right = Vector2.Zero;
        foreach (var button in Enum.GetValues<PadButton>())
        {
            input.SetPadButton(button, Input.State.Up);
        }
    }

    private void Axis(SDL_GamepadAxis axis, float value, Input input)
    {
        switch (axis)
        {
            case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX:
                _left.X = value;
                break;
            case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY:
                _left.Y = value;
                break;
            case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTX:
                _right.X = value;
                break;
            case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY:
                _right.Y = value;
                break;
            case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER:
                input.SetPadButton(PadButton.LeftTrigger, value > TriggerPull ? Input.State.Down : Input.State.Up);
                break;
            case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER:
                input.SetPadButton(PadButton.RightTrigger, value > TriggerPull ? Input.State.Down : Input.State.Up);
                break;
        }
    }

    private static PadButton? Button(SDL_GamepadButton button) => button switch
    {
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH => PadButton.South,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST => PadButton.East,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST => PadButton.West,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH => PadButton.North,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK => PadButton.Back,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START => PadButton.Start,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_STICK => PadButton.LeftStick,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_STICK => PadButton.RightStick,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER => PadButton.LeftShoulder,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER => PadButton.RightShoulder,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP => PadButton.DpadUp,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN => PadButton.DpadDown,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT => PadButton.DpadLeft,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT => PadButton.DpadRight,
        _ => null,
    };

    public void Dispose()
    {
        foreach (var pad in _open.Values)
        {
            SDL_CloseGamepad((SDL_Gamepad*)pad);
        }

        _open.Clear();
    }
}
