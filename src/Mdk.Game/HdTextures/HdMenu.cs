namespace Mdk.Game.HdTextures;

/// <summary>The platform the game runs on, for what it can offer.</summary>
public enum Host { Desktop, Android }

/// <summary>The options page's HD texture items: the job making them (their switch is the Mods page's).</summary>
public enum HdItem { Make }

/// <summary>Which HD texture items the options page shows. Real-ESRGAN's ncnn-Vulkan builds are
/// desktop-only, so Android never makes them; it uses the mod made on a PC (imported with mods/).</summary>
public static class HdMenu
{
    public static Host Current => OperatingSystem.IsAndroid() ? Host.Android : Host.Desktop;

    public static bool CanMake(Host host) => host == Host.Desktop;

    public static IReadOnlyList<HdItem> Items(Host host) => CanMake(host) ? [HdItem.Make] : [];
}
