using Mdk.Engine.Platform;
using static SDL.SDL3;

namespace Mdk.Game.Tests;

/// <summary>The window's icon: assets/icon.png, embedded in the engine (tools/gen_icon.py).</summary>
public unsafe class AppIconTests
{
    private const int IconSize = 512;

    [Fact]
    public void EmbeddedIconDecodes()
    {
        var surface = AppIcon.Load();
        Assert.True(surface != null, SDL_GetError());

        Assert.Equal(IconSize, surface->w);
        Assert.Equal(IconSize, surface->h);
        SDL_DestroySurface(surface);
    }
}
