using Mdk.Formats;

namespace Mdk.Formats.Tests;

/// <summary>Sniper mode's screen in TRAVSPRT.BNI (godot-mdk docs/gameplay.md "Sniper mode").</summary>
public class SniperScreenTests
{
    private static readonly MdkData Data = MdkData.Find() ?? throw new InvalidOperationException("MDK data not found");

    [Fact]
    public void MaskWordsSkipAndCopy()
    {
        // 1 x 4 literals, skip 3, 2 short literals, end.
        byte[] bytes = [0x01, 0x00, 1, 2, 3, 4, 0x03, 0x80, 0x02, 0xFF, 5, 6, 0x00, 0xFF];
        var mask = SniperScreen.DecodeMask(bytes, 0);
        Assert.Equal(SniperScreen.ViewWidth * SniperScreen.ViewHeight, mask.Length);
        Assert.Equal([1, 2, 3, 4, 0, 0, 0, 5, 6, 0], mask[..10]);
    }

    [Fact]
    public void ScreenDecodesWithHoles()
    {
        var sprites = Bni.Load(Data.PathOf("TRAVERSE/TRAVSPRT.BNI"));
        var frame = SniperScreen.Frame(sprites);
        Assert.Equal(SniperScreen.ScreenWidth * SniperScreen.ScreenHeight, frame.Indices.Length);

        // The scope's centre (299, 219 of the view) is a hole of the mask; its corner isn't.
        var mask = SniperScreen.Mask(sprites);
        Assert.Equal(0, mask.Indices[219 * SniperScreen.ViewWidth + 299]);
        Assert.NotEqual(0, mask.Indices[SniperScreen.ViewWidth * (SniperScreen.ViewHeight - 1)]);
    }
}
