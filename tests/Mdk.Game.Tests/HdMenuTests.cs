using Mdk.Game.HdTextures;

namespace Mdk.Game.Tests;

/// <summary>The options page's HD texture items per platform: Android can't run the upscaler (it
/// uses the mod made on a PC, switched on the Mods page).</summary>
public class HdMenuTests
{
    [Fact]
    public void DesktopMakes()
    {
        Assert.Equal([HdItem.Make], HdMenu.Items(Host.Desktop));
    }

    [Fact]
    public void AndroidDoesnt()
    {
        Assert.Empty(HdMenu.Items(Host.Android));
    }

    [Fact]
    public void OnlyDesktopMakes()
    {
        Assert.True(HdMenu.CanMake(Host.Desktop));
        Assert.False(HdMenu.CanMake(Host.Android));
    }
}
