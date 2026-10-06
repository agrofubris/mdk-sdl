using System.Numerics;
using Mdk.Game.Fall;

namespace Mdk.Game.Tests;

/// <summary>The fall's palette effects (0x410cc0, gameplay.md "Palette effects"): towards white by
/// the brightness, colour 0 kept after the first second.</summary>
public class FallPaletteTests
{
    private const int Entries = 256;
    private const int Channels = 4;

    /// <summary>Entry i is (i, i, i).</summary>
    private static FallPalette Grey()
    {
        var rgba = new byte[Entries * Channels];
        for (var i = 0; i < Entries; i++)
        {
            (rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]) = ((byte)i, (byte)i, (byte)i, byte.MaxValue);
        }

        return new FallPalette(rgba);
    }

    [Theory]
    [InlineData(0, 1f, 0)]
    [InlineData(100, 1f, 100)]
    [InlineData(0, 0.5f, 127)]
    [InlineData(100, 0.5f, 177)]
    [InlineData(100, 0.9f, 115)]
    [InlineData(100, 0f, 255)]
    [InlineData(255, 0.3f, 255)]
    public void WhitensAsTheOriginal(int c, float brightness, int expected)
    {
        // (c × k + (256 − k) × 255) >> 8, k = round(256 b): 0.9 → 230.
        Assert.Equal(expected, FallPalette.Whiten((byte)c, FallPalette.Level(brightness)));
    }

    [Fact]
    public void ColourZeroWhitensOnlyInTheFirstSecond()
    {
        var palette = Grey();
        Assert.True(palette.Set(0.5f, 0f, FallPalette.Zero.Whitened));
        Assert.Equal(127, palette.Rgba[0]);
        Assert.Equal(177, palette.Rgba[100 * Channels]);

        Assert.True(palette.Set(0.5f, 0f, FallPalette.Zero.Kept));
        Assert.Equal(0, palette.Rgba[0]);
        Assert.Equal(177, palette.Rgba[100 * Channels + 2]);
        Assert.False(palette.Set(0.5f, 0f, FallPalette.Zero.Kept));
    }

    [Fact]
    public void RedRisesOnColoursOneTo254()
    {
        var palette = Grey();
        palette.Set(1f, 10f / byte.MaxValue, FallPalette.Zero.Kept);
        Assert.Equal((0, 0), (palette.Rgba[0], palette.Rgba[1]));
        Assert.Equal((110, 100), (palette.Rgba[100 * Channels], palette.Rgba[100 * Channels + 1]));
        Assert.Equal(255, palette.Rgba[250 * Channels]);
        Assert.Equal(255, palette.Rgba[255 * Channels]);
    }

    /// <summary>Flat colours, the radar and the trails take the effects of colours 1-254.</summary>
    [Fact]
    public void OtherColoursTakeTheEffects()
    {
        var palette = Grey();
        palette.Set(0.5f, 10f / byte.MaxValue, FallPalette.Zero.Kept);
        Assert.Equal(new Vector4(182f, 177f, 177f, byte.MaxValue) / byte.MaxValue, palette.Colour(100));

        var green = palette.Apply(new Vector4(0f, 1f, 0f, 0.25f));
        Assert.Equal(10f / byte.MaxValue * 0.5f + 0.5f, green.X, 1e-5f);
        Assert.Equal((1f, 0.5f, 0.25f), (green.Y, green.Z, green.W));
    }
}
