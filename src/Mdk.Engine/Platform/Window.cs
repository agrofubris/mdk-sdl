using System.Numerics;
using SDL;
using static SDL.SDL3;

namespace Mdk.Engine.Platform;

/// <summary>The game window and its events (SDL3). Fills <see cref="Input"/> each frame.</summary>
public sealed unsafe class Window : IDisposable
{
    internal SDL_Window* Handle { get; }

    /// <summary>The mouse cursor set by <see cref="SetCursor"/>, or null for the system's.</summary>
    private SDL_Cursor* _cursor;

    /// <summary>A hidden window (tests) is never shown nor focused: frames are drawn off screen.</summary>
    public Visibility Visibility { get; }

    /// <summary>The on-screen controls of a touch screen (Android), or null.</summary>
    private readonly TouchControls? _touch = OperatingSystem.IsAndroid() ? new TouchControls() : null;
    /// <summary>The mouse looks around (play): touches are the on-screen controls, not clicks.</summary>
    private Capture _capture;

    /// <summary>The on-screen controls while they're used (play), or null.</summary>
    public TouchControls? Touch => _capture == Capture.On ? _touch : null;

    public Window(string title, int width, int height, Visibility visibility = Visibility.Shown)
    {
        Visibility = visibility;

        // Android's back button is Esc, not "close the app".
        SDL_SetHint(SDL_HINT_ANDROID_TRAP_BACK_BUTTON, "1");
        // Phones play sideways, either way up.
        SDL_SetHint(SDL_HINT_ORIENTATIONS, "LandscapeLeft LandscapeRight");
        if (!SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO | SDL_InitFlags.SDL_INIT_EVENTS))
        {
            throw new InvalidOperationException($"SDL_Init: {SDL_GetError()}");
        }

        var flags = SDL_WindowFlags.SDL_WINDOW_RESIZABLE;
        if (visibility == Visibility.Hidden)
        {
            flags |= SDL_WindowFlags.SDL_WINDOW_HIDDEN;
        }

        Handle = SDL_CreateWindow(title, width, height, flags);
        if (Handle == null)
        {
            throw new InvalidOperationException($"SDL_CreateWindow: {SDL_GetError()}");
        }

        SetIcon();

        // Typed text (names of saved games, cheats); not on a touch screen, where it opens the keyboard.
        if (_touch == null)
        {
            SDL_StartTextInput(Handle);
        }
    }

    /// <summary>The program's icon on the window (title bar, task bar, dock). A missing icon is
    /// only reported: the game runs without it.</summary>
    private void SetIcon()
    {
        var icon = AppIcon.Load();
        if (icon == null)
        {
            Console.Error.WriteLine($"Window icon: {SDL_GetError()}");
            return;
        }

        if (!SDL_SetWindowIcon(Handle, icon))
        {
            Console.Error.WriteLine($"Window icon: {SDL_GetError()}");
        }

        SDL_DestroySurface(icon);
    }

    /// <summary>The setup last applied (null: none yet).</summary>
    private DisplaySetup? _setup;

    /// <summary>Windowed at a size, fullscreen on the desktop, or in a mode of the display of its
    /// own; always fullscreen on a touch screen (a phone). Acts only on a change (a window resized
    /// by hand keeps its size), never on a hidden window.</summary>
    public void Apply(DisplaySetup setup)
    {
        if (setup == _setup || Visibility == Visibility.Hidden)
        {
            return;
        }

        _setup = setup;
        if (_touch != null)
        {
            SDL_SetWindowFullscreen(Handle, true);
            return;
        }

        switch (setup.Fullscreen)
        {
            case Fullscreen.Off:
                SDL_SetWindowFullscreen(Handle, false);
                SDL_SetWindowSize(Handle, setup.Window.Width, setup.Window.Height);
                break;
            case Fullscreen.Desktop:
                SDL_SetWindowFullscreenMode(Handle, null);
                SDL_SetWindowFullscreen(Handle, true);
                break;
            case Fullscreen.Exclusive:
                SetExclusive(setup.Exclusive);
                break;
        }
    }

    public Fullscreen Fullscreen => _setup?.Fullscreen ?? Fullscreen.Off;

    /// <summary>Exclusive fullscreen in the display's mode of that size at its highest refresh rate
    /// (none, or none of that size: the desktop's).</summary>
    private void SetExclusive(Resolution? size)
    {
        var display = SDL_GetDisplayForWindow(Handle);
        var mode = SDL_GetDesktopDisplayMode(display);
        int count;
        var modes = SDL_GetFullscreenDisplayModes(display, &count);
        for (var i = 0; modes != null && size is { } wanted && i < count; i++)
        {
            var candidate = modes[i];
            if (candidate->w != wanted.Width || candidate->h != wanted.Height)
            {
                continue;
            }

            if (mode->w != wanted.Width || mode->h != wanted.Height || candidate->refresh_rate > mode->refresh_rate)
            {
                mode = candidate;
            }
        }

        if (!SDL_SetWindowFullscreenMode(Handle, mode) || !SDL_SetWindowFullscreen(Handle, true))
        {
            Console.Error.WriteLine($"Exclusive fullscreen: {SDL_GetError()}");
        }

        SDL_free(modes);
    }

    /// <summary>The window's display's desktop mode.</summary>
    public ScreenMode Desktop
    {
        get
        {
            var mode = SDL_GetDesktopDisplayMode(SDL_GetDisplayForWindow(Handle));
            return mode == null ? default : new ScreenMode(mode->w, mode->h, mode->refresh_rate);
        }
    }

    /// <summary>Window sizes that fit the display (<see cref="Resolutions.Windowed"/>).</summary>
    public IReadOnlyList<Resolution> WindowedSizes()
    {
        SDL_Rect usable;
        var current = Size;
        if (!SDL_GetDisplayUsableBounds(SDL_GetDisplayForWindow(Handle), &usable))
        {
            return [new Resolution(current.Width, current.Height)];
        }

        return Resolutions.Windowed(new Resolution(usable.w, usable.h), new Resolution(current.Width, current.Height));
    }

    /// <summary>The display's modes, one per size (<see cref="Resolutions.Exclusive"/>).</summary>
    public IReadOnlyList<ScreenMode> ExclusiveModes()
    {
        int count;
        var modes = SDL_GetFullscreenDisplayModes(SDL_GetDisplayForWindow(Handle), &count);
        if (modes == null)
        {
            return [];
        }

        var list = new List<ScreenMode>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(new ScreenMode(modes[i]->w, modes[i]->h, modes[i]->refresh_rate));
        }

        SDL_free(modes);
        return Resolutions.Exclusive(list);
    }

    /// <summary>SDL's version, e.g. "3.4.0".</summary>
    public static string SdlVersion
    {
        get
        {
            const int Major = 1000000;
            const int Minor = 1000;
            var version = SDL_GetVersion();
            return $"{version / Major}.{version / Minor % Minor}.{version % Minor}";
        }
    }

    /// <summary>The window's size in pixels.</summary>
    public (int Width, int Height) Size
    {
        get
        {
            int width, height;
            SDL_GetWindowSize(Handle, &width, &height);
            return (width, height);
        }
    }

    /// <summary>The mouse cursor becomes an image (RGBA8, <paramref name="width"/> x
    /// <paramref name="height"/>) whose hotspot is (<paramref name="hotX"/>, <paramref name="hotY"/>).</summary>
    public void SetCursor(byte[] rgba, int width, int height, int hotX, int hotY)
    {
        const int BytesPerPixel = 4;
        SDL_Cursor* cursor;
        fixed (byte* pixels = rgba)
        {
            var surface = SDL_CreateSurfaceFrom(width, height, SDL_PixelFormat.SDL_PIXELFORMAT_ABGR8888, (nint)pixels, width * BytesPerPixel);
            if (surface == null)
            {
                return;
            }

            cursor = SDL_CreateColorCursor(surface, hotX, hotY);
            SDL_DestroySurface(surface);
        }

        if (cursor == null)
        {
            return;
        }

        SDL_SetCursor(cursor);
        if (_cursor != null)
        {
            SDL_DestroyCursor(_cursor);
        }

        _cursor = cursor;
    }

    /// <summary>Mouse captured for looking around (relative motion, hidden cursor).</summary>
    public void CaptureMouse(Capture capture)
    {
        _capture = capture;
        if (_touch == null)
        {
            SDL_SetWindowRelativeMouseMode(Handle, capture == Capture.On);
        }
    }

    /// <summary>Handles pending events. Returns false when the window closes.</summary>
    public bool PumpEvents(Input input)
    {
        input.BeginFrame();

        // Out of play, touches are clicks (SDL makes mouse events of them).
        if (_capture == Capture.Off)
        {
            _touch?.Release(input);
        }

        SDL_Event e;
        while (SDL_PollEvent(&e))
        {
            if (Touch != null && TouchEvent(e, input))
            {
                continue;
            }

            switch ((SDL_EventType)e.type)
            {
                case SDL_EventType.SDL_EVENT_QUIT:
                    return false;
                case SDL_EventType.SDL_EVENT_KEY_DOWN:
                    input.SetKey(e.key.scancode, Input.State.Down, e.key.repeat ? Repeat.Yes : Repeat.No);
                    break;
                case SDL_EventType.SDL_EVENT_KEY_UP:
                    input.SetKey(e.key.scancode, Input.State.Up, Repeat.No);
                    break;
                case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
                    input.SetButton(Button(e.button.button), Input.State.Down);
                    break;
                case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
                    input.SetButton(Button(e.button.button), Input.State.Up);
                    break;
                case SDL_EventType.SDL_EVENT_MOUSE_MOTION:
                    input.AddMouseMotion(e.motion.xrel, e.motion.yrel);
                    input.SetPointer(e.motion.x, e.motion.y);
                    break;
                case SDL_EventType.SDL_EVENT_TEXT_INPUT:
                    input.AddText(e.text.GetText() ?? "");
                    break;
                case SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
                    input.AddWheel(e.wheel.y);
                    break;
            }
        }

        return true;
    }

    /// <summary>In play on a touch screen, fingers drive the on-screen controls and the mouse events
    /// SDL makes of them are dropped. Returns whether the event was a touch.</summary>
    private bool TouchEvent(in SDL_Event e, Input input)
    {
        var size = Size;
        var screen = new Vector2(size.Width, size.Height);
        var at = new Vector2(e.tfinger.x, e.tfinger.y) * screen;
        var finger = (ulong)e.tfinger.fingerID;
        switch ((SDL_EventType)e.type)
        {
            case SDL_EventType.SDL_EVENT_FINGER_DOWN:
                _touch!.Down(finger, at, screen, input);
                return true;
            case SDL_EventType.SDL_EVENT_FINGER_MOTION:
                _touch!.Move(finger, at, screen, input);
                return true;
            case SDL_EventType.SDL_EVENT_FINGER_UP:
            case SDL_EventType.SDL_EVENT_FINGER_CANCELED:
                _touch!.Up(finger, input);
                return true;
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
                return e.button.which == SDL_TOUCH_MOUSEID;
            case SDL_EventType.SDL_EVENT_MOUSE_MOTION:
                return e.motion.which == SDL_TOUCH_MOUSEID;
            default:
                return false;
        }
    }

    /// <summary><see cref="MouseButton"/> keeps SDL's numbers.</summary>
    private static MouseButton Button(byte button) => (MouseButton)button;

    public void Dispose()
    {
        if (_cursor != null)
        {
            SDL_DestroyCursor(_cursor);
        }

        SDL_DestroyWindow(Handle);
        SDL_Quit();
    }
}

public enum Capture { Off, On }

public enum Visibility { Shown, Hidden }

/// <summary>Windowed, fullscreen on the desktop (borderless), or exclusive (a mode of the display's own).</summary>
public enum Fullscreen { Off, Desktop, Exclusive }
