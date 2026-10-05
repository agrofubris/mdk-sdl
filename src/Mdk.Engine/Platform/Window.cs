using SDL;
using static SDL.SDL3;

namespace Mdk.Engine.Platform;

/// <summary>The game window and its events (SDL3). Fills <see cref="Input"/> each frame.</summary>
public sealed unsafe class Window : IDisposable
{
    internal SDL_Window* Handle { get; }

    public Window(string title, int width, int height)
    {
        NativeLibraries.Install();
        if (!SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO | SDL_InitFlags.SDL_INIT_EVENTS))
        {
            throw new InvalidOperationException($"SDL_Init: {SDL_GetError()}");
        }

        Handle = SDL_CreateWindow(title, width, height, SDL_WindowFlags.SDL_WINDOW_RESIZABLE);
        if (Handle == null)
        {
            throw new InvalidOperationException($"SDL_CreateWindow: {SDL_GetError()}");
        }
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
                    input.SetKey(e.key.scancode, Input.State.Down);
                    break;
                case SDL_EventType.SDL_EVENT_KEY_UP:
                    input.SetKey(e.key.scancode, Input.State.Up);
                    break;
                case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
                    input.SetButton(Button(e.button.button), Input.State.Down);
                    break;
                case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
                    input.SetButton(Button(e.button.button), Input.State.Up);
                    break;
                case SDL_EventType.SDL_EVENT_MOUSE_MOTION:
                    input.AddMouseMotion(e.motion.xrel, e.motion.yrel);
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
        _ => MouseButton.Other,
    };

    public void Dispose()
    {
        SDL_DestroyWindow(Handle);
        SDL_Quit();
    }
}

public enum Capture { Off, On }
