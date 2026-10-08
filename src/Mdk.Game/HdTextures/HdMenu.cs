namespace Mdk.Game.HdTextures;

/// <summary>The platform the game runs on, for what it can offer.</summary>
public enum Host { Desktop, Android }

/// <summary>The options page's HD texture items: the switch, and the job making them.</summary>
public enum HdItem { Switch, Make }

/// <summary>Which HD texture items the options page shows. Real-ESRGAN's ncnn-Vulkan builds are
/// desktop-only, so Android never makes them; it shows the switch only for a cache copied from a PC
/// into its user folder.</summary>
public static class HdMenu
{
    public static Host Current => OperatingSystem.IsAndroid() ? Host.Android : Host.Desktop;

    public static bool CanMake(Host host) => host == Host.Desktop;

    public static IReadOnlyList<HdItem> Items(Host host, string userFolder)
    {
        if (CanMake(host))
        {
            return [HdItem.Switch, HdItem.Make];
        }

        return HdManifest.Load(HdCache.FolderIn(userFolder)) != null ? [HdItem.Switch] : [];
    }
}
