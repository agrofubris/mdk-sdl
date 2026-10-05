using SDL;

namespace Mdk.Engine.Platform;

/// <summary>Keys the game uses, independent of SDL.</summary>
public enum Key
{
    Forward, Back, StrafeLeft, StrafeRight, TurnLeft, TurnRight, Jump, Turbo, Fire,
    /// <summary>Kurt's items: use the selected one, select the next, previous, or slot 1-5.</summary>
    UseItem, ItemNext, ItemPrevious, Item1, Item2, Item3, Item4, Item5,
    /// <summary>Flying camera: up and down.</summary>
    Up, Down,
    Escape, Screenshot,
    /// <summary>Switches between Kurt and the flying camera.</summary>
    Fly,
}

/// <summary>Keyboard and mouse state of the current frame.</summary>
public sealed class Input
{
    public enum State { Up, Down }

    private static readonly Dictionary<SDL_Scancode, Key> Bindings = new()
    {
        [SDL_Scancode.SDL_SCANCODE_W] = Key.Forward,
        [SDL_Scancode.SDL_SCANCODE_UP] = Key.Forward,
        [SDL_Scancode.SDL_SCANCODE_S] = Key.Back,
        [SDL_Scancode.SDL_SCANCODE_DOWN] = Key.Back,
        [SDL_Scancode.SDL_SCANCODE_A] = Key.StrafeLeft,
        [SDL_Scancode.SDL_SCANCODE_D] = Key.StrafeRight,
        [SDL_Scancode.SDL_SCANCODE_LEFT] = Key.TurnLeft,
        [SDL_Scancode.SDL_SCANCODE_RIGHT] = Key.TurnRight,
        [SDL_Scancode.SDL_SCANCODE_SPACE] = Key.Jump,
        [SDL_Scancode.SDL_SCANCODE_LSHIFT] = Key.Turbo,
        [SDL_Scancode.SDL_SCANCODE_RSHIFT] = Key.Turbo,
        [SDL_Scancode.SDL_SCANCODE_LCTRL] = Key.Fire,
        [SDL_Scancode.SDL_SCANCODE_RCTRL] = Key.Fire,
        [SDL_Scancode.SDL_SCANCODE_RETURN] = Key.UseItem,
        [SDL_Scancode.SDL_SCANCODE_TAB] = Key.ItemNext,
        [SDL_Scancode.SDL_SCANCODE_RIGHTBRACKET] = Key.ItemNext,
        [SDL_Scancode.SDL_SCANCODE_LEFTBRACKET] = Key.ItemPrevious,
        [SDL_Scancode.SDL_SCANCODE_1] = Key.Item1,
        [SDL_Scancode.SDL_SCANCODE_2] = Key.Item2,
        [SDL_Scancode.SDL_SCANCODE_3] = Key.Item3,
        [SDL_Scancode.SDL_SCANCODE_4] = Key.Item4,
        [SDL_Scancode.SDL_SCANCODE_5] = Key.Item5,
        [SDL_Scancode.SDL_SCANCODE_E] = Key.Up,
        [SDL_Scancode.SDL_SCANCODE_Q] = Key.Down,
        [SDL_Scancode.SDL_SCANCODE_ESCAPE] = Key.Escape,
        [SDL_Scancode.SDL_SCANCODE_F12] = Key.Screenshot,
        [SDL_Scancode.SDL_SCANCODE_F1] = Key.Fly,
    };

    private readonly HashSet<Key> _down = [];
    /// <summary>Keys held by mouse buttons, apart from the keyboard's.</summary>
    private readonly HashSet<Key> _buttons = [];
    private readonly HashSet<Key> _pressed = [];
    /// <summary>Keys held by the program (tests), whatever the keyboard does.</summary>
    private readonly HashSet<Key> _held = [];

    public float MouseX { get; private set; }
    public float MouseY { get; private set; }

    public bool IsDown(Key key) => _down.Contains(key) || _buttons.Contains(key) || _held.Contains(key);

    /// <summary>Holds or releases a key from the program (automated tests).</summary>
    public void Hold(Key key, State state)
    {
        if (state == State.Down)
        {
            _held.Add(key);
            return;
        }

        _held.Remove(key);
    }

    /// <summary>Pressed this frame.</summary>
    public bool WasPressed(Key key) => _pressed.Contains(key);

    internal void BeginFrame() => _pressed.Clear();

    /// <summary>The mouse motion was used (by a game step).</summary>
    public void ClearMouse()
    {
        MouseX = 0f;
        MouseY = 0f;
    }

    internal void SetKey(SDL_Scancode scancode, State state)
    {
        if (!Bindings.TryGetValue(scancode, out var key))
        {
            return;
        }

        if (state == State.Up)
        {
            _down.Remove(key);
            return;
        }

        if (_down.Add(key))
        {
            _pressed.Add(key);
        }
    }

    /// <summary>A mouse button: the left one fires.</summary>
    internal void SetButton(MouseButton button, State state)
    {
        if (button != MouseButton.Left)
        {
            return;
        }

        if (state == State.Up)
        {
            _buttons.Remove(Key.Fire);
            return;
        }

        if (_buttons.Add(Key.Fire))
        {
            _pressed.Add(Key.Fire);
        }
    }

    internal void AddMouseMotion(float x, float y)
    {
        MouseX += x;
        MouseY += y;
    }
}

/// <summary>Mouse buttons, independent of SDL.</summary>
internal enum MouseButton { Left, Other }
