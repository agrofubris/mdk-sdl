using Mdk.Game.HdTextures;

namespace Mdk.Game.Tests;

/// <summary>The options page's HD texture items per platform: Android can't run the upscaler, and
/// shows the switch only for a cache copied from a PC.</summary>
public class HdMenuTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("mdk-hdmenu-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private void AddCache()
    {
        var cache = HdCache.FolderIn(_folder);
        Directory.CreateDirectory(cache);
        new HdManifest("general", 2).Save(cache);
    }

    [Fact]
    public void DesktopShowsBoth()
    {
        Assert.Equal([HdItem.Switch, HdItem.Make], HdMenu.Items(Host.Desktop, _folder));
    }

    [Fact]
    public void AndroidHidesBothWithoutCache()
    {
        Assert.Empty(HdMenu.Items(Host.Android, _folder));
    }

    [Fact]
    public void AndroidShowsSwitchWithCache()
    {
        AddCache();

        Assert.Equal([HdItem.Switch], HdMenu.Items(Host.Android, _folder));
    }

    [Fact]
    public void OnlyDesktopMakes()
    {
        Assert.True(HdMenu.CanMake(Host.Desktop));
        Assert.False(HdMenu.CanMake(Host.Android));
    }
}
