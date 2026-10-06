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

    public Window(string title, int width, int height, Visibility visibility = Visibility.Shown)
    {
        Visibility = visibility;
        NativeLibraries.Install();
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

        // Typed text (names of saved games, cheats).
        SDL_StartTextInput(Handle);
    }

    /// <summary>The window fills the screen or not.</summary>
    public void SetFullscreen(Fullscreen mode) => SDL_SetWindowFullscreen(Handle, mode == Fullscreen.On);

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
    public void CaptureMouse(Capture capture) => SDL_SetWindowRelativeMouseMode(Handle, capture == Capture.On);

    /// <summary>Handles pending events. Returns false when the window closes.</summary>
    public bool PumpEvents(Input input)
    {
        input.BeginFrame();
        SDL_Event e;
        while (SDL_PollEvent(&e))
        {
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

    private static MouseButton Button(byte button) => button switch
    {
        SDL_BUTTON_LEFT => MouseButton.Left,
        SDL_BUTTON_RIGHT => MouseButton.Right,
        SDL_BUTTON_MIDDLE => MouseButton.Middle,
        SDL_BUTTON_X1 => MouseButton.Back,
        SDL_BUTTON_X2 => MouseButton.Forward,
        _ => MouseButton.Other,
    };

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

public enum Fullscreen { Off, On }
