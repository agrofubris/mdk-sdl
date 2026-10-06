using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Game.Level;

namespace Mdk.Game.Tests;

/// <summary>The scripts' sky modes (opcode 202, level.gd show_sky).</summary>
public class SkyTests
{
    private static readonly Vector4 Blue = new(0f, 0f, 1f, 1f);

    [Theory]
    [InlineData(0, Backdrop.Sky)]
    [InlineData(1, Backdrop.Clear)]
    [InlineData(-1, Backdrop.Keep)]
    public void ModesPickTheBackdrop(int mode, Backdrop backdrop) => Assert.Equal(backdrop, SkyModes.BackdropOf(mode));

    [Fact]
    public void BlackModeClearsToBlack()
    {
        Assert.Equal(new Vector4(0f, 0f, 0f, 1f), SkyModes.ClearOf(SkyModes.Black, Blue));
        Assert.Equal(Blue, SkyModes.ClearOf(SkyModes.Shown, Blue));
    }
}
