using Mdk.Engine.Render;

namespace Mdk.Game.Tests;

/// <summary>The enhanced look's textures: indices through the palette into RGBA, premultiplied,
/// and their mipmaps (box filtered), without the GPU.</summary>
public class ColourMipsTests
{
    private const int Rgba = 4;
    private const byte Opaque = 255;
    private static readonly byte[] Red = [200, 0, 0, Opaque];
    private static readonly byte[] Blue = [0, 0, 100, Opaque];
    private static readonly byte[] Clear = [0, 0, 0, 0];

    /// <summary>A palette: index 1 red, 2 blue, the rest grey.</summary>
    private static byte[] Palette()
    {
        var palette = new byte[256 * Rgba];
        Array.Fill(palette, (byte)128);
        Red.CopyTo(palette, 1 * Rgba);
        Blue.CopyTo(palette, 2 * Rgba);
        return palette;
    }

    private static byte[] Texel(byte[] rgba, int index) => rgba[(index * Rgba)..((index + 1) * Rgba)];

    [Fact]
    public void IndicesTakeThePalettesColours()
    {
        var rgba = ColourMips.Expand([1, 2, 0], Palette());

        Assert.Equal(Red, Texel(rgba, 0));
        Assert.Equal(Blue, Texel(rgba, 1));
    }

    [Fact]
    public void IndexZeroIsClear()
    {
        // Premultiplied: no colour where nothing covers, so filtering never mixes in black.
        var rgba = ColourMips.Expand([0], Palette());

        Assert.Equal(Clear, Texel(rgba, 0));
    }

    [Fact]
    public void MipsHalveDownToOneTexel()
    {
        Assert.Equal(1, ColourMips.Levels(1, 1));
        Assert.Equal(7, ColourMips.Levels(64, 64));
        Assert.Equal(8, ColourMips.Levels(128, 16));
        Assert.Equal(3, ColourMips.Levels(5, 3));

        var chain = ColourMips.Chain(new byte[128 * 16 * Rgba], 128, 16);
        Assert.Equal(8, chain.Count);
        Assert.Equal(64 * 8 * Rgba, chain[1].Length);
        Assert.Equal(1 * 1 * Rgba, chain[7].Length);
    }

    [Fact]
    public void MipsAverageTwoByTwo()
    {
        // Red and blue columns: the next level is their mean.
        var rgba = ColourMips.Expand([1, 2, 1, 2], Palette());

        var half = ColourMips.Chain(rgba, 2, 2)[1];

        Assert.Equal(new byte[] { 100, 0, 50, Opaque }, half);
    }

    [Fact]
    public void ClearEdgesDoNotDarken()
    {
        // Half red, half clear: half covered, and red again once divided by its cover.
        var rgba = ColourMips.Expand([1, 0, 1, 0], Palette());

        var half = ColourMips.Chain(rgba, 2, 2)[1];

        Assert.Equal(128, half[3]);
        Assert.InRange(half[0] * (double)Opaque / half[3], Red[0] - 1.0, Red[0] + 1.0);
        Assert.Equal(0, half[1]);
        Assert.Equal(0, half[2]);
    }

    [Fact]
    public void OddSizesKeepTheirLastTexels()
    {
        // 3 x 1: red, red, blue; the second level's one texel covers all three.
        var rgba = ColourMips.Expand([1, 1, 2], Palette());

        var next = ColourMips.Chain(rgba, 3, 1)[1];

        Assert.Equal(Rgba, next.Length);
        Assert.Equal(133, next[0]);
        Assert.Equal(33, next[2]);
    }
}
