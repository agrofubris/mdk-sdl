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
    /// <summary>Sniper mode on or off, and its zoom.</summary>
    Sniper, ZoomIn, ZoomOut,
    /// <summary>The 1996 demo's levels: rolls, and teleports (held with a digit).</summary>
    RollLeft, RollRight, Teleport,
}

/// <summary>Keys of the menus and prompts: fixed, whatever the game's bindings.</summary>
public enum MenuKey { Up, Down, Left, Right, Accept, Back, Backspace, Delete, Home, End, Snapshot }

/// <summary>Mouse buttons that click menu items.</summary>
public enum Pointer { Left, Right }

/// <summary>Keyboard and mouse state of the current frame.</summary>
public sealed class Input
{
    public enum State { Up, Down }

    private static readonly Dictionary<SDL_Scancode, Key> DefaultKeys = new()
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
        [SDL_Scancode.SDL_SCANCODE_PAGEUP] = Key.ZoomIn,
        [SDL_Scancode.SDL_SCANCODE_EQUALS] = Key.ZoomIn,
        [SDL_Scancode.SDL_SCANCODE_PAGEDOWN] = Key.ZoomOut,
        [SDL_Scancode.SDL_SCANCODE_MINUS] = Key.ZoomOut,
        [SDL_Scancode.SDL_SCANCODE_Z] = Key.RollLeft,
        [SDL_Scancode.SDL_SCANCODE_C] = Key.RollRight,
        [SDL_Scancode.SDL_SCANCODE_T] = Key.Teleport,
        [SDL_Scancode.SDL_SCANCODE_LALT] = Key.Teleport,
        [SDL_Scancode.SDL_SCANCODE_RALT] = Key.Teleport,
    };

    /// <summary>The digit keys, of the number row and the keypad, 0 to 9.</summary>
    private static readonly SDL_Scancode[][] DigitKeys =
    [
        [SDL_Scancode.SDL_SCANCODE_0, SDL_Scancode.SDL_SCANCODE_1, SDL_Scancode.SDL_SCANCODE_2, SDL_Scancode.SDL_SCANCODE_3,
            SDL_Scancode.SDL_SCANCODE_4, SDL_Scancode.SDL_SCANCODE_5, SDL_Scancode.SDL_SCANCODE_6, SDL_Scancode.SDL_SCANCODE_7,
            SDL_Scancode.SDL_SCANCODE_8, SDL_Scancode.SDL_SCANCODE_9],
        [SDL_Scancode.SDL_SCANCODE_KP_0, SDL_Scancode.SDL_SCANCODE_KP_1, SDL_Scancode.SDL_SCANCODE_KP_2, SDL_Scancode.SDL_SCANCODE_KP_3,
            SDL_Scancode.SDL_SCANCODE_KP_4, SDL_Scancode.SDL_SCANCODE_KP_5, SDL_Scancode.SDL_SCANCODE_KP_6, SDL_Scancode.SDL_SCANCODE_KP_7,
            SDL_Scancode.SDL_SCANCODE_KP_8, SDL_Scancode.SDL_SCANCODE_KP_9],
    ];

    /// <summary>No digit pressed (<see cref="Digit"/>).</summary>
    public const int NoDigit = -1;

    /// <summary>The keys of mouse buttons: the left one fires, the right one toggles sniper mode.</summary>
    private static readonly Dictionary<MouseButton, Key> DefaultButtons = new()
    {
        [MouseButton.Left] = Key.Fire,
        [MouseButton.Right] = Key.Sniper,
    };

    private static readonly Dictionary<SDL_Scancode, MenuKey> MenuKeys = new()
    {
        [SDL_Scancode.SDL_SCANCODE_UP] = MenuKey.Up,
        [SDL_Scancode.SDL_SCANCODE_DOWN] = MenuKey.Down,
        [SDL_Scancode.SDL_SCANCODE_LEFT] = MenuKey.Left,
        [SDL_Scancode.SDL_SCANCODE_RIGHT] = MenuKey.Right,
        [SDL_Scancode.SDL_SCANCODE_RETURN] = MenuKey.Accept,
        [SDL_Scancode.SDL_SCANCODE_KP_ENTER] = MenuKey.Accept,
        [SDL_Scancode.SDL_SCANCODE_SPACE] = MenuKey.Accept,
        [SDL_Scancode.SDL_SCANCODE_ESCAPE] = MenuKey.Back,
        [SDL_Scancode.SDL_SCANCODE_BACKSPACE] = MenuKey.Backspace,
        [SDL_Scancode.SDL_SCANCODE_DELETE] = MenuKey.Delete,
        [SDL_Scancode.SDL_SCANCODE_HOME] = MenuKey.Home,
        [SDL_Scancode.SDL_SCANCODE_END] = MenuKey.End,
        [SDL_Scancode.SDL_SCANCODE_F2] = MenuKey.Snapshot,
    };

    /// <summary>Names of mouse buttons as bindings (<see cref="Bind"/>).</summary>
    private static readonly Dictionary<MouseButton, string> ButtonNames = new()
    {
        [MouseButton.Left] = "Left mouse",
        [MouseButton.Right] = "Right mouse",
        [MouseButton.Middle] = "Middle mouse",
        [MouseButton.Back] = "Mouse 4",
        [MouseButton.Forward] = "Mouse 5",
    };

    /// <summary>The bindings: the defaults, changed by <see cref="Bind"/>.</summary>
    private readonly Dictionary<SDL_Scancode, Key> _keys = new(DefaultKeys);
    private readonly Dictionary<MouseButton, Key> _buttonKeys = new(DefaultButtons);
    private readonly HashSet<MenuKey> _menuPressed = [];
    private readonly HashSet<Pointer> _clicked = [];

    private readonly HashSet<Key> _down = [];
    /// <summary>Keys held by mouse buttons, apart from the keyboard's.</summary>
    private readonly HashSet<Key> _buttons = [];
    private readonly HashSet<Key> _pressed = [];
    /// <summary>Keys held by the program (tests), whatever the keyboard does.</summary>
    private readonly HashSet<Key> _held = [];

    public float MouseX { get; private set; }
    public float MouseY { get; private set; }
    /// <summary>Mouse wheel notches since the last game step: positive away from the user.</summary>
    public float Wheel { get; private set; }

    /// <summary>Mouse motion is scaled by this (the mouse sensitivity).</summary>
    public float MouseScale { get; set; } = 1f;
    /// <summary>Vertical mouse motion is inverted.</summary>
    public bool InvertMouse { get; set; }
    /// <summary>The pointer in the window (pixels).</summary>
    public float PointerX { get; private set; }
    public float PointerY { get; private set; }
    /// <summary>The pointer moved this frame.</summary>
    public bool PointerMoved { get; private set; }
    /// <summary>Text typed this frame.</summary>
    public string Typed { get; private set; } = "";
    /// <summary>Any key or mouse button was pressed this frame.</summary>
    public bool AnyPressed { get; private set; }
    /// <summary>The name of the key or mouse button pressed this frame (for <see cref="Bind"/>), or "".</summary>
    public string LastControl { get; private set; } = "";
    /// <summary>The digit (0-9) pressed this frame, or <see cref="NoDigit"/>.</summary>
    public int Digit { get; private set; } = NoDigit;

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

    /// <summary>Presses a menu key for this frame from the program (automated tests).</summary>
    public void Press(MenuKey key)
    {
        _menuPressed.Add(key);
        AnyPressed = true;
    }

    /// <summary>Pressed this frame.</summary>
    public bool WasPressed(Key key) => _pressed.Contains(key);

    public bool WasPressed(MenuKey key) => _menuPressed.Contains(key);

    public bool WasClicked(Pointer button) => _clicked.Contains(button);

    internal void BeginFrame()
    {
        _pressed.Clear();
        _menuPressed.Clear();
        _clicked.Clear();
        PointerMoved = false;
        Typed = "";
        AnyPressed = false;
        LastControl = "";
        Digit = NoDigit;
    }

    /// <summary>Binds a key or mouse button (by its name, as <see cref="LastControl"/> gives it) to
    /// <paramref name="key"/>, instead of its other bindings. Returns false for an unknown name.</summary>
    public bool Bind(Key key, string control)
    {
        var button = ButtonNames.FirstOrDefault(b => b.Value == control);
        var scancode = button.Value == null ? SDL3.SDL_GetScancodeFromName(control) : SDL_Scancode.SDL_SCANCODE_UNKNOWN;
        if (button.Value == null && scancode == SDL_Scancode.SDL_SCANCODE_UNKNOWN)
        {
            return false;
        }

        Unbind(key);
        if (button.Value != null)
        {
            _buttonKeys[button.Key] = key;
            return true;
        }

        _keys[scancode] = key;
        return true;
    }

    /// <summary>Back to the default bindings.</summary>
    public void ResetBindings()
    {
        _keys.Clear();
        _buttonKeys.Clear();
        foreach (var (scancode, key) in DefaultKeys)
        {
            _keys[scancode] = key;
        }

        foreach (var (button, key) in DefaultButtons)
        {
            _buttonKeys[button] = key;
        }
    }

    /// <summary>The name of a key's first binding, or "-".</summary>
    public string Describe(Key key)
    {
        foreach (var (scancode, bound) in _keys)
        {
            if (bound == key)
            {
                return SDL3.SDL_GetScancodeName(scancode) ?? "-";
            }
        }

        foreach (var (button, bound) in _buttonKeys)
        {
            if (bound == key)
            {
                return ButtonNames.GetValueOrDefault(button, "-");
            }
        }

        return "-";
    }

    private void Unbind(Key key)
    {
        foreach (var scancode in _keys.Where(k => k.Value == key).Select(k => k.Key).ToList())
        {
            _keys.Remove(scancode);
        }

        foreach (var button in _buttonKeys.Where(b => b.Value == key).Select(b => b.Key).ToList())
        {
            _buttonKeys.Remove(button);
        }
    }

    /// <summary>The mouse motion was used (by a game step).</summary>
    public void ClearMouse()
    {
        MouseX = 0f;
        MouseY = 0f;
        Wheel = 0f;
    }

    internal void SetKey(SDL_Scancode scancode, State state, Repeat repeat)
    {
        if (state == State.Down && MenuKeys.TryGetValue(scancode, out var menuKey))
        {
            _menuPressed.Add(menuKey);
        }

        if (state == State.Down && repeat == Repeat.No)
        {
            AnyPressed = true;
            LastControl = SDL3.SDL_GetScancodeName(scancode) ?? "";
            Digit = DigitKeys.Select(row => Array.IndexOf(row, scancode)).FirstOrDefault(d => d != NoDigit, Digit);
        }

        if (!_keys.TryGetValue(scancode, out var key))
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

    /// <summary>A mouse button: see <see cref="DefaultButtons"/>.</summary>
    internal void SetButton(MouseButton button, State state)
    {
        if (state == State.Down)
        {
            AnyPressed = true;
            LastControl = ButtonNames.GetValueOrDefault(button, "");
            AddClick(button);
        }

        if (!_buttonKeys.TryGetValue(button, out var key))
        {
            return;
        }

        if (state == State.Up)
        {
            _buttons.Remove(key);
            return;
        }

        if (_buttons.Add(key))
        {
            _pressed.Add(key);
        }
    }

    internal void AddWheel(float notches) => Wheel += notches;

    internal void AddMouseMotion(float x, float y)
    {
        MouseX += x * MouseScale;
        MouseY += (InvertMouse ? -y : y) * MouseScale;
    }

    internal void SetPointer(float x, float y)
    {
        PointerX = x;
        PointerY = y;
        PointerMoved = true;
    }

    internal void AddText(string text) => Typed += text;

    private void AddClick(MouseButton button)
    {
        if (button == MouseButton.Left)
        {
            _clicked.Add(Pointer.Left);
        }
        else if (button == MouseButton.Right)
        {
            _clicked.Add(Pointer.Right);
        }
    }
}

/// <summary>Mouse buttons, independent of SDL.</summary>
internal enum MouseButton { Left, Right, Middle, Back, Forward, Other }

/// <summary>Whether a key event is the keyboard's auto-repeat.</summary>
internal enum Repeat { No, Yes }
