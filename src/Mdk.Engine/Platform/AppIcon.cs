using SDL;
using static SDL.SDL3;

namespace Mdk.Engine.Platform;

/// <summary>The program's icon (assets/icon.png, made by tools/gen_icon.py), embedded in the engine.</summary>
internal static unsafe class AppIcon
{
    private const string Resource = "icon.png";

    /// <summary>The icon as an SDL surface (the caller destroys it), or null with SDL's error.</summary>
    internal static SDL_Surface* Load()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(Resource);
        if (stream == null)
        {
            return null;
        }

        var png = new byte[stream.Length];
        stream.ReadExactly(png);

        // SDL decodes from memory; closeio: the stream is freed with the call.
        fixed (byte* bytes = png)
        {
            var io = SDL_IOFromConstMem((nint)bytes, (nuint)png.Length);
            return io == null ? null : SDL_LoadPNG_IO(io, true);
        }
    }
}
